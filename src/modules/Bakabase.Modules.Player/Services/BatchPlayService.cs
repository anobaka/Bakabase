using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Abstractions.Services;
using Bakabase.Modules.Player.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Services;

public class BatchPlayService(
    IResourceService resourceService,
    IResourceProfileService resourceProfileService,
    IPlayerDiscoveryService discoveryService,
    IBatchPlayPlaylistSource playlistSource,
    IBatchPlayProcessLauncher launcher,
    IOptions<PlayerModuleOptions> options,
    ILogger<BatchPlayService> logger) : IBatchPlayService
{
    private const string ProfileKeyPrefix = "profile|";
    private const string KnownKeyPrefix = "known|";

    // ========================================================================
    // Resource-selection flow
    // ========================================================================

    public async Task<List<BatchPlayCandidate>> GetCandidatesAsync(int[] resourceIds, CancellationToken ct)
    {
        var filesByResource = await ResolvePlayableFilesAsync(resourceIds, ct);
        var candidates = await BuildCandidatesAsync(resourceIds, ct);

        // Annotate each candidate with how many of the selected resources it
        // can actually open, so the menu never advertises a player that has
        // nothing to play (e.g. a comic viewer over a video selection).
        return candidates.Select(c => c with
        {
            MatchedResourceCount =
                filesByResource.Count(kv => kv.Value.Any(f => MatchesPlayer(c.SupportedExtensions, f))),
        }).ToList();
    }

    public async Task<BatchPlayResult> PlayAsync(BatchPlayInputModel input, CancellationToken ct)
    {
        if (input.ResourceIds.Length == 0)
        {
            throw new InvalidOperationException("No resources selected.");
        }

        var candidates = await BuildCandidatesAsync(input.ResourceIds, ct);
        var candidate = FindCandidate(candidates, input.PlayerKey);

        var filesByResource = await ResolvePlayableFilesAsync(input.ResourceIds, ct);
        var (files, includedResources, skipped, missingFileCount) =
            CollectFiles(input.ResourceIds, filesByResource, candidate.SupportedExtensions,
                input.FileSelectionMode);

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"None of the selected resources has a playable file that {candidate.DisplayName} supports.");
        }

        GuardTotalFiles(files.Count);

        var launchMethod = await LaunchAsync(candidate, files, ct);

        await TryMarkPlayedAsync(includedResources);

        return new BatchPlayResult
        {
            PlayerName = candidate.DisplayName,
            LaunchMethod = launchMethod,
            ResourceCount = includedResources.Count,
            FileCount = files.Count,
            SkippedResources = skipped,
            MissingFileCount = missingFileCount,
        };
    }

    // ========================================================================
    // Playlist flow
    // ========================================================================

    public async Task<List<BatchPlayCandidate>> GetPlaylistCandidatesAsync(int playlistId, CancellationToken ct)
    {
        var snapshot = await GetSnapshotOrThrowAsync(playlistId, ct);
        var resourceIds = snapshot.Entries.Where(e => e.ResourceId.HasValue)
            .Select(e => e.ResourceId!.Value).Distinct().ToArray();
        var candidates = await BuildCandidatesAsync(resourceIds, ct);

        return candidates.Select(c => c with
        {
            MatchedFileCount = snapshot.Entries.Count(e => MatchesPlayer(c.SupportedExtensions, e.Path)),
        }).ToList();
    }

    public async Task<BatchPlayResult> PlayPlaylistAsync(int playlistId, string playerKey, CancellationToken ct)
    {
        var snapshot = await GetSnapshotOrThrowAsync(playlistId, ct);
        var resourceIds = snapshot.Entries.Where(e => e.ResourceId.HasValue)
            .Select(e => e.ResourceId!.Value).Distinct().ToArray();
        var candidate = FindCandidate(await BuildCandidatesAsync(resourceIds, ct), playerKey);

        var matched = snapshot.Entries
            .Where(e => MatchesPlayer(candidate.SupportedExtensions, e.Path))
            .ToList();
        var existing = matched.Where(e => File.Exists(e.Path)).ToList();
        var missingFileCount = matched.Count - existing.Count;

        if (existing.Count == 0)
        {
            throw new InvalidOperationException(
                $"The playlist has no existing file that {candidate.DisplayName} supports.");
        }

        GuardTotalFiles(existing.Count);

        var launchMethod = await LaunchAsync(candidate, existing.Select(e => e.Path).ToList(), ct);

        // One history entry per distinct backing resource, first included file each.
        var includedResources = existing.Where(e => e.ResourceId.HasValue)
            .GroupBy(e => e.ResourceId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Path);
        await TryMarkPlayedAsync(includedResources);

        return new BatchPlayResult
        {
            PlayerName = candidate.DisplayName,
            LaunchMethod = launchMethod,
            ResourceCount = includedResources.Count,
            FileCount = existing.Count,
            MissingFileCount = missingFileCount,
        };
    }

    // ========================================================================
    // Shared internals
    // ========================================================================

    /// <summary>
    /// Builds the candidate list for a set of resources: players configured
    /// in the matching resource profiles first (they reflect an explicit
    /// per-source choice), then known players discovered on this machine.
    /// </summary>
    private async Task<List<BatchPlayCandidate>> BuildCandidatesAsync(int[] resourceIds, CancellationToken ct)
    {
        var candidates = new List<BatchPlayCandidate>();
        var coveredExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resources = await resourceService.GetByKeys(resourceIds);
        foreach (var resource in resources)
        {
            ct.ThrowIfCancellationRequested();
            var playerOptions = await resourceProfileService.GetEffectivePlayerOptions(resource);
            foreach (var player in playerOptions?.Players ?? [])
            {
                if (string.IsNullOrWhiteSpace(player.ExecutablePath) ||
                    !coveredExecutables.Add(player.ExecutablePath))
                {
                    continue;
                }

                var known = KnownPlayerDefinitions.MatchByExecutable(player.ExecutablePath);
                candidates.Add(new BatchPlayCandidate
                {
                    Key = $"{ProfileKeyPrefix}{player.ExecutablePath.ToLowerInvariant()}",
                    Type = BatchPlayCandidateType.ProfilePlayer,
                    DisplayName = known?.DisplayName ??
                                  CrossPlatformPath.GetFileNameWithoutExtension(player.ExecutablePath),
                    ExecutablePath = player.ExecutablePath,
                    // Unrecognized players get the playlist route by
                    // assumption — most accept an m3u8 as their file argument.
                    Capabilities = known?.Capabilities ?? BatchPlayCapability.PlaylistFile,
                    CommandTemplate = player.Command,
                    CapabilitiesAssumed = known == null,
                    // The user's configured extension set wins over the
                    // catalog's; an empty/missing set means "any file".
                    SupportedExtensions = NormalizeExtensions(player.Extensions) ??
                                          known?.SupportedExtensions,
                });
            }
        }

        foreach (var discovered in await discoveryService.GetDiscoveredPlayersAsync(ct: ct))
        {
            if (!coveredExecutables.Add(discovered.ExecutablePath))
            {
                continue;
            }

            var definition = KnownPlayerDefinitions.All.First(d => d.Id == discovered.DefinitionId);
            candidates.Add(new BatchPlayCandidate
            {
                Key = $"{KnownKeyPrefix}{discovered.DefinitionId}",
                Type = BatchPlayCandidateType.KnownPlayer,
                DisplayName = discovered.DisplayName,
                ExecutablePath = discovered.ExecutablePath,
                Capabilities = discovered.Capabilities,
                CapabilitiesAssumed = false,
                SupportedExtensions = definition.SupportedExtensions,
            });
        }

        return candidates;
    }

    /// <summary>
    /// Resolves the FileSystem playable files of each resource, in selection
    /// order. Cache-first; uncached resources are discovered on demand.
    /// </summary>
    private async Task<Dictionary<int, List<string>>> ResolvePlayableFilesAsync(int[] resourceIds,
        CancellationToken ct)
    {
        var knownIds = (await resourceService.GetByKeys(resourceIds)).Select(r => r.Id).ToHashSet();
        var result = new Dictionary<int, List<string>>();
        foreach (var resourceId in resourceIds)
        {
            ct.ThrowIfCancellationRequested();
            if (!knownIds.Contains(resourceId))
            {
                continue;
            }

            var items = await resourceService.DiscoverPlayableItems(resourceId, ct);
            result[resourceId] = items
                .Where(i => i.Origin == DataOrigin.FileSystem && !string.IsNullOrEmpty(i.Key))
                .Select(i => i.Key)
                .ToList();
        }

        return result;
    }

    private static (List<string> Files, Dictionary<int, string> IncludedResources,
        List<BatchPlaySkippedResource> Skipped, int MissingFileCount)
        CollectFiles(int[] resourceIds, Dictionary<int, List<string>> filesByResource,
            IReadOnlySet<string>? supportedExtensions, BatchPlayFileSelectionMode mode)
    {
        var files = new List<string>();
        var includedResources = new Dictionary<int, string>();
        var skipped = new List<BatchPlaySkippedResource>();
        var missingFileCount = 0;

        // Iterate in the order the user selected so the playlist follows it.
        foreach (var resourceId in resourceIds)
        {
            if (!filesByResource.TryGetValue(resourceId, out var paths))
            {
                skipped.Add(new BatchPlaySkippedResource(resourceId, BatchPlaySkipReason.ResourceNotFound));
                continue;
            }

            if (paths.Count == 0)
            {
                skipped.Add(new BatchPlaySkippedResource(resourceId, BatchPlaySkipReason.NoPlayableFiles));
                continue;
            }

            var matching = paths.Where(p => MatchesPlayer(supportedExtensions, p)).ToList();
            if (matching.Count == 0)
            {
                skipped.Add(new BatchPlaySkippedResource(resourceId, BatchPlaySkipReason.NoFilesMatchingPlayer));
                continue;
            }

            // Files may have moved since they were cached; silently feeding a
            // player dead paths produces confusing in-player errors instead.
            var existing = matching.Where(File.Exists).ToList();
            missingFileCount += matching.Count - existing.Count;

            if (existing.Count == 0)
            {
                skipped.Add(new BatchPlaySkippedResource(resourceId, BatchPlaySkipReason.AllFilesMissing));
                continue;
            }

            List<string> selected = mode == BatchPlayFileSelectionMode.FirstFilePerResource
                ? [existing[0]]
                : existing;

            files.AddRange(selected);
            includedResources[resourceId] = selected[0];
        }

        return (files, includedResources, skipped, missingFileCount);
    }

    private async Task<BatchPlayLaunchMethod> LaunchAsync(BatchPlayCandidate candidate, List<string> files,
        CancellationToken ct)
    {
        var osSafeFiles = files.Select(ToOsSafePath).ToList();

        string arguments;
        BatchPlayLaunchMethod method;

        if (osSafeFiles.Count == 1)
        {
            // A single file works through the plain template for any player.
            arguments = BatchPlayArguments.BuildFromTemplate(candidate.CommandTemplate, osSafeFiles[0]);
            method = BatchPlayLaunchMethod.MultiFileArguments;
        }
        else if (candidate.Capabilities.HasFlag(BatchPlayCapability.PlaylistFile))
        {
            var directory = options.Value.TempPlaylistDirectory ??
                            Path.Combine(Path.GetTempPath(), "bakabase", "playlists");
            M3u8Playlist.SweepOldFiles(directory, options.Value.TempPlaylistRetention);
            var playlistPath = await M3u8Playlist.WriteTempFileAsync(directory,
                osSafeFiles.Select(f => new M3u8Entry(f)), ct);
            arguments = BatchPlayArguments.BuildFromTemplate(candidate.CommandTemplate, playlistPath);
            method = BatchPlayLaunchMethod.PlaylistFile;
        }
        else if (candidate.Capabilities.HasFlag(BatchPlayCapability.MultiFileArguments))
        {
            arguments = BatchPlayArguments.BuildMultiFile(osSafeFiles);
            if (arguments.Length > options.Value.MaxCommandLineLength)
            {
                throw new InvalidOperationException(
                    "Too many files for this player's command line. Pick a player with playlist support.");
            }

            method = BatchPlayLaunchMethod.MultiFileArguments;
        }
        else
        {
            throw new InvalidOperationException(
                $"{candidate.DisplayName} does not support opening multiple files at once.");
        }

        try
        {
            await launcher.LaunchAsync(candidate.ExecutablePath, arguments, ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Batch-play launch of '{Executable}' failed", candidate.ExecutablePath);
            throw new InvalidOperationException(
                $"Failed to launch {candidate.DisplayName}: {e.Message}");
        }

        return method;
    }

    private async Task<BatchPlayPlaylistSnapshot> GetSnapshotOrThrowAsync(int playlistId, CancellationToken ct)
        => await playlistSource.GetSnapshotAsync(playlistId, ct) ??
           throw new InvalidOperationException("The playlist does not exist.");

    private static BatchPlayCandidate FindCandidate(List<BatchPlayCandidate> candidates, string playerKey)
        => candidates.FirstOrDefault(c => c.Key == playerKey) ??
           throw new InvalidOperationException(
               "The selected player is no longer available. Please pick another one.");

    private void GuardTotalFiles(int count)
    {
        if (count > options.Value.MaxTotalFiles)
        {
            throw new InvalidOperationException(
                $"The selection expands to {count} files, which exceeds the limit of " +
                $"{options.Value.MaxTotalFiles}. Try the first-file-per-resource mode or a smaller selection.");
        }
    }

    private async Task TryMarkPlayedAsync(Dictionary<int, string> includedResources)
    {
        if (includedResources.Count == 0)
        {
            return;
        }

        try
        {
            await resourceService.MarkPlayed(includedResources);
        }
        catch (Exception e)
        {
            // Play history is best-effort; the player is already running.
            logger.LogWarning(e, "Failed to record batch-play history");
        }
    }

    private static bool MatchesPlayer(IReadOnlySet<string>? supportedExtensions, string path)
    {
        if (supportedExtensions == null)
        {
            return true;
        }

        var extension = Path.GetExtension(CrossPlatformPath.GetFileName(path));
        return !string.IsNullOrEmpty(extension) && supportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Profile player extensions are user input and may omit the leading dot;
    /// normalize so matching behaves the same as the playable-file options.
    /// </summary>
    private static IReadOnlySet<string>? NormalizeExtensions(IEnumerable<string>? extensions)
    {
        var set = extensions?
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Select(e => e.StartsWith('.') ? e : $".{e}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set is { Count: > 0 } ? set : null;
    }

    private static string ToOsSafePath(string path) =>
        OperatingSystem.IsWindows()
            ? path.Replace(InternalOptions.DirSeparator, InternalOptions.WindowsSpecificDirSeparator)
            : path;
}
