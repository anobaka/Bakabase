using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Resolvers;

/// <summary>
/// FileSystem resolver that discovers resources from PathMark resource marks.
/// All filesystem resource discovery logic is consolidated here.
/// </summary>
public class FileSystemResolver : IResourceResolver
{
    private readonly IExtensionGroupService _extensionGroupService;
    private readonly IResourceProfileService _resourceProfileService;
    private readonly ISystemPlayer _systemPlayer;
    private readonly ILogger<FileSystemResolver> _logger;

    public FileSystemResolver(
        IExtensionGroupService extensionGroupService,
        IResourceProfileService resourceProfileService,
        ISystemPlayer systemPlayer,
        ILogger<FileSystemResolver> logger)
    {
        _extensionGroupService = extensionGroupService;
        _resourceProfileService = resourceProfileService;
        _systemPlayer = systemPlayer;
        _logger = logger;
    }

    public ResourceSource Source => ResourceSource.PathMark;

    /// <summary>
    /// Not used directly - filesystem discovery is done via <see cref="DiscoverFromMarks"/>.
    /// </summary>
    public Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct)
    {
        return Task.FromResult(new List<ResolvedResource>());
    }

    /// <summary>
    /// Discovers filesystem resources from the given resource marks.
    /// Each mark's configuration determines which paths are discovered.
    /// </summary>
    /// <param name="resourceMarks">Active resource marks to process (should be ordered by priority descending).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of discovered resources with their associated mark IDs.</returns>
    public async Task<List<FileSystemDiscoveredResource>> DiscoverFromMarks(
        List<PathMark> resourceMarks,
        CancellationToken ct,
        IReadOnlyCollection<PathMark>? boundaryMarks = null)
    {
        var results = new List<FileSystemDiscoveredResource>();

        // Pre-compute resource boundary paths from all marks
        var boundaryPaths = BoundaryPathsOf(boundaryMarks ?? resourceMarks);

        // Pre-load all extension groups for resolving ExtensionGroupIds
        var allExtensionGroups = await _extensionGroupService.GetAll();
        var groupMap = allExtensionGroups.ToDictionary(g => g.Id, g => g);

        // Regex cache for performance
        var regexCache = new ConcurrentDictionary<string, Regex>();

        foreach (var mark in resourceMarks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var config = JsonSerializer.Deserialize<ResourceMarkConfig>(mark.ConfigJson, JsonSerializerOptions.Web);
                if (config == null) continue;

                // Resolve extension group IDs to actual extensions
                if (config.ExtensionGroupIds is { Count: > 0 })
                {
                    var groupExtensions = config.ExtensionGroupIds
                        .Select(id => groupMap.GetValueOrDefault(id))
                        .Where(g => g?.Extensions != null)
                        .SelectMany(g => g!.Extensions!);

                    var merged = new List<string>(config.Extensions ?? []);
                    merged.AddRange(groupExtensions);
                    config.Extensions = merged.Distinct().ToList();
                }

                var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config, regexCache, ct);

                foreach (var path in matchedPaths)
                {
                    ct.ThrowIfCancellationRequested();

                    var standardizedPath = path.StandardizePath()!;

                    // Skip if blocked by another mark's boundary
                    if (IsPathBlockedByBoundary(standardizedPath, mark.Path.StandardizePath()!, boundaryPaths))
                        continue;

                    // Check if path exists on filesystem
                    if (!File.Exists(path) && !Directory.Exists(path))
                        continue;

                    results.Add(new FileSystemDiscoveredResource
                    {
                        MarkId = mark.Id,
                        Path = standardizedPath
                    });
                }
            }
            // Both arms rethrow; the filter is only so a stop request does not get reported as a
            // mark that failed to process.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[FileSystemResolver] Failed to process mark {MarkId} on path {Path}",
                    mark.Id, mark.Path);
                throw;
            }
        }

        return results;
    }

    /// <summary>Rechecks completed discovery against boundaries persisted while the scan ran.</summary>
    public static List<FileSystemDiscoveredResource> ApplyResourceBoundaries(
        List<FileSystemDiscoveredResource> discovered,
        IReadOnlyCollection<PathMark> sourceMarks,
        IReadOnlyCollection<PathMark> boundaryMarks)
    {
        var markPaths = sourceMarks.ToDictionary(mark => mark.Id, mark => mark.Path.StandardizePath()!);
        var boundaries = BoundaryPathsOf(boundaryMarks);
        return discovered.Where(candidate => markPaths.TryGetValue(candidate.MarkId, out var markPath) &&
                                              !IsPathBlockedByBoundary(candidate.Path, markPath, boundaries)).ToList();
    }

    private static Dictionary<string, int> BoundaryPathsOf(IEnumerable<PathMark> marks)
    {
        var paths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mark in marks)
        {
            var config = JsonSerializer.Deserialize<ResourceMarkConfig>(mark.ConfigJson, JsonSerializerOptions.Web);
            if (config is {IsResourceBoundary: true} && mark.Path.StandardizePath() is {Length: > 0} path)
                paths[path] = mark.Id;
        }

        return paths;
    }

    #region Discovery Helpers

    /// <param name="ct">
    /// Threaded all the way down to the individual filesystem entry. A mark rooted at a large
    /// library expands into a full recursive walk, which is by far the longest stretch of this
    /// resolver — leaving it uncancellable meant a stop request (the app exiting included) sat
    /// behind the whole scan before <see cref="DiscoverFromMarks"/> got to its next checkpoint.
    /// </param>
    private List<string> GetMatchingPathsForResourceMark(
        string rootPath,
        ResourceMarkConfig config,
        ConcurrentDictionary<string, Regex> regexCache,
        CancellationToken ct)
    {
        var matchedPaths = new List<string>();
        var normalizedRoot = rootPath.StandardizePath()!;

        if (!Directory.Exists(normalizedRoot)) return matchedPaths;

        try
        {
            List<string> initialMatches;

            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer < 0)
                {
                    initialMatches = GetParentAtLayer(normalizedRoot, Math.Abs(layer), config.FsTypeFilter);
                }
                else
                {
                    initialMatches = GetEntriesAtLayer(normalizedRoot, layer, config.FsTypeFilter, config.Extensions,
                        ct);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regexPattern = config.Regex;
                if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
                {
                    regexPattern = regexPattern.EndsWith("$")
                        ? regexPattern.Substring(0, regexPattern.Length - 1) + @"[^/\\]*$"
                        : regexPattern + @"[^/\\]*$";
                }

                var regex = GetOrCreateRegex(regexPattern, regexCache);
                var entries = GetAllEntries(normalizedRoot, config.FsTypeFilter, config.Extensions, ct);
                initialMatches = new List<string>();

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(normalizedRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
                    {
                        initialMatches.Add(entry);
                    }
                }
            }
            else
            {
                return matchedPaths;
            }

            if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
            {
                matchedPaths.AddRange(initialMatches);
            }
            else if (config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories)
            {
                matchedPaths.AddRange(initialMatches);
                foreach (var match in initialMatches)
                {
                    // Each iteration is a whole recursive walk of its own.
                    ct.ThrowIfCancellationRequested();

                    if (Directory.Exists(match))
                    {
                        var subdirEntries = GetAllEntries(match, config.FsTypeFilter, config.Extensions, ct);
                        matchedPaths.AddRange(subdirEntries);
                    }
                }
            }
        }
        // Cancellation is not an access error. These catches exist to let an unreadable directory
        // be skipped rather than abort the mark, and a bare one swallows the stop request too.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ignore access errors
        }

        return matchedPaths;
    }

    private static List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter,
        List<string>? extensions, CancellationToken ct)
    {
        var entries = new List<string>();

        try
        {
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    entries.Add(dir.StandardizePath()!);
                }
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var extensionSet = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();

                    if (extensionSet == null || extensionSet.Count == 0 ||
                        extensionSet.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(file.StandardizePath()!);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    private static List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter,
        List<string>? extensions, CancellationToken ct)
    {
        var entries = new List<string>();

        try
        {
            var currentPaths = new List<string> { rootPath };

            for (int i = 1; i <= layer; i++)
            {
                var nextPaths = new List<string>();
                foreach (var path in currentPaths)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        nextPaths.AddRange(Directory.EnumerateDirectories(path));
                        if (i == layer && (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File))
                        {
                            var extensionSet = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                            foreach (var file in Directory.EnumerateFiles(path))
                            {
                                if (extensionSet == null || extensionSet.Count == 0 ||
                                    extensionSet.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                                {
                                    nextPaths.Add(file);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore access errors
                    }
                }

                currentPaths = nextPaths;
            }

            if (fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(currentPaths.Where(Directory.Exists));
            }
            else if (fsTypeFilter == PathFilterFsType.File)
            {
                entries.AddRange(currentPaths.Where(File.Exists));
            }
            else
            {
                entries.AddRange(currentPaths);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private static List<string> GetParentAtLayer(string rootPath, int absLayer, PathFilterFsType? fsTypeFilter)
    {
        var entries = new List<string>();

        try
        {
            var currentPath = rootPath;
            for (int i = 0; i < absLayer; i++)
            {
                var parentPath = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parentPath))
                {
                    return entries;
                }

                currentPath = parentPath;
            }

            if (Directory.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
                {
                    entries.Add(currentPath.StandardizePath()!);
                }
            }
            else if (File.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
                {
                    entries.Add(currentPath.StandardizePath()!);
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    private static bool IsPathBlockedByBoundary(
        string path,
        string currentMarkPath,
        Dictionary<string, int> boundaryPaths)
    {
        foreach (var boundaryPath in boundaryPaths.Keys)
        {
            // A boundary only blocks marks rooted above it. Marks on or inside the boundary
            // define that subtree's own resource layout and must remain effective.
            if (!boundaryPath.StartsWith(currentMarkPath.TrimEnd('/') + InternalOptions.DirSeparator,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (path.StartsWith(boundaryPath + InternalOptions.DirSeparator, StringComparison.OrdinalIgnoreCase) ||
                path.Equals(boundaryPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Regex GetOrCreateRegex(string pattern, ConcurrentDictionary<string, Regex> cache)
    {
        return cache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

    #endregion

    #region IResourceResolver (unchanged)

    public ResolverConfigurationSchema GetConfigurationSchema()
    {
        return new ResolverConfigurationSchema();
    }

    public ResolverPlayerConfig? GetDefaultPlayerConfig()
    {
        return null;
    }

    public IPlayableFileSelector? GetPlayableFileSelector()
    {
        return null;
    }

    public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<DomainResource> fileSystemResources, CancellationToken ct)
    {
        return Task.FromResult(new List<MigrationCandidate>());
    }

    public Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys)
    {
        var result = new Dictionary<string, string>();
        foreach (var sourceKey in sourceKeys)
        {
            if (!string.IsNullOrEmpty(sourceKey))
            {
                var fileName = Path.GetFileName(sourceKey);
                if (!string.IsNullOrEmpty(fileName))
                {
                    result[sourceKey] = fileName;
                }
            }
        }

        return Task.FromResult(result);
    }

    #endregion

    #region Playable Items

    public async Task<List<PlayableItem>> DiscoverPlayableItemsAsync(DomainResource resource, string sourceKey, CancellationToken ct)
    {
        var items = new List<PlayableItem>();

        if (!resource.HasLocalPath)
            return items;

        // For file resources, the file itself is playable
        if (resource.IsFile)
        {
            if (File.Exists(resource.Path))
            {
                items.Add(new PlayableItem
                {
                    Origin = DataOrigin.FileSystem,
                    Key = resource.Path.StandardizePath()!,
                    DisplayName = Path.GetFileName(resource.Path!)
                });
            }
            return items;
        }

        // For directory resources, use ResourceProfile playable file options
        var playableFileOptions = await _resourceProfileService.GetEffectivePlayableFileOptions(resource);
        if (playableFileOptions?.Extensions is not { Count: > 0 })
            return items;

        var extensions = playableFileOptions.Extensions
            .Select(e => e.StartsWith('.') ? e : $".{e}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Per entry, for the same reason as the mark walks above: a resource directory can be
        // arbitrarily large, and this is the only long stretch between the caller's checkpoints.
        var result = new List<string>();
        if (Directory.Exists(resource.Path))
        {
            foreach (var file in Directory.EnumerateFiles(resource.Path, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                if (extensions.Contains(Path.GetExtension(file)))
                {
                    result.Add(file);
                }
            }
        }

        // Apply file name pattern filter if configured
        if (!string.IsNullOrEmpty(playableFileOptions.FileNamePattern))
        {
            try
            {
                var regex = new Regex(playableFileOptions.FileNamePattern, RegexOptions.IgnoreCase);
                result = result.Where(f => regex.IsMatch(Path.GetFileName(f))).ToList();
            }
            catch
            {
                // Invalid regex, ignore the filter
            }
        }

        items.AddRange(result.Select(f => new PlayableItem
        {
            Origin = DataOrigin.FileSystem,
            Key = f.StandardizePath()!,
            DisplayName = Path.GetFileName(f)
        }));

        return items;
    }

    #endregion
}
