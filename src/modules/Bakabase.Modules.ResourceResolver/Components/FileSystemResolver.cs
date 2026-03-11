using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ResourceResolver.Components;

/// <summary>
/// FileSystem resolver that discovers resources from PathMark resource marks.
/// All filesystem resource discovery logic is consolidated here.
/// </summary>
public class FileSystemResolver : IResourceResolver
{
    private readonly IExtensionGroupService _extensionGroupService;
    private readonly ILogger<FileSystemResolver> _logger;

    public FileSystemResolver(
        IExtensionGroupService extensionGroupService,
        ILogger<FileSystemResolver> logger)
    {
        _extensionGroupService = extensionGroupService;
        _logger = logger;
    }

    public ResourceSource Source => ResourceSource.FileSystem;

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
        CancellationToken ct)
    {
        var results = new List<FileSystemDiscoveredResource>();

        // Pre-compute resource boundary paths from all marks
        var boundaryPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mark in resourceMarks)
        {
            var config = JsonSerializer.Deserialize<ResourceMarkConfig>(mark.ConfigJson);
            if (config is { IsResourceBoundary: true })
            {
                var normalizedPath = mark.Path.StandardizePath();
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    boundaryPaths[normalizedPath] = mark.Id;
                }
            }
        }

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
                var config = JsonSerializer.Deserialize<ResourceMarkConfig>(mark.ConfigJson);
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

                var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config, regexCache);

                foreach (var path in matchedPaths)
                {
                    ct.ThrowIfCancellationRequested();

                    var standardizedPath = path.StandardizePath()!;

                    // Skip if blocked by another mark's boundary
                    if (IsPathBlockedByBoundary(standardizedPath, mark.Id, boundaryPaths))
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FileSystemResolver] Failed to process mark {MarkId} on path {Path}",
                    mark.Id, mark.Path);
                throw;
            }
        }

        return results;
    }

    #region Discovery Helpers

    private List<string> GetMatchingPathsForResourceMark(
        string rootPath,
        ResourceMarkConfig config,
        ConcurrentDictionary<string, Regex> regexCache)
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
                    initialMatches = GetEntriesAtLayer(normalizedRoot, layer, config.FsTypeFilter, config.Extensions);
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
                var entries = GetAllEntries(normalizedRoot, config.FsTypeFilter, config.Extensions);
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
                    if (Directory.Exists(match))
                    {
                        var subdirEntries = GetAllEntries(match, config.FsTypeFilter, config.Extensions);
                        matchedPaths.AddRange(subdirEntries);
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return matchedPaths;
    }

    private static List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter,
        List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    entries.Add(dir.StandardizePath()!);
                }
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var extensionSet = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    if (extensionSet == null || extensionSet.Count == 0 ||
                        extensionSet.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(file.StandardizePath()!);
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    private static List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter,
        List<string>? extensions)
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
        int currentMarkId,
        Dictionary<string, int> boundaryPaths)
    {
        foreach (var (boundaryPath, markId) in boundaryPaths)
        {
            if (markId == currentMarkId)
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
        List<Resource> fileSystemResources, CancellationToken ct)
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
}
