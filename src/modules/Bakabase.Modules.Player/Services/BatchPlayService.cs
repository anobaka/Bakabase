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
    IBatchPlayResourceSource resourceSource,
    IBatchPlayFileResolver fileResolver,
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
        var snapshot = await resourceSource.GetSnapshotAsync(resourceIds, ct);
        var candidates = await BuildCandidatesAsync(snapshot, ct);

        // Annotate each candidate with how many of the selected resources it
        // can actually open, so the menu never advertises a player that has
        // nothing to play (e.g. a comic viewer over a video selection).
        return candidates.Select(c => c with
        {
            MatchedResourceCount =
                snapshot.Resources.Count(r => r.Files.Any(f => MatchesPlayer(c.SupportedExtensions, f))),
        }).ToList();
    }

    public async Task<BatchPlayResult> PlayAsync(BatchPlayInputModel input, CancellationToken ct)
    {
        if (input.ResourceIds.Length == 0)
        {
            throw new InvalidOperationException("No resources selected.");
        }

        var snapshot = await resourceSource.GetSnapshotAsync(input.ResourceIds, ct);
        var candidate = FindCandidate(await BuildCandidatesAsync(snapshot, ct), input.PlayerKey);

        var (files, includedResources, skipped, missingFileCount) =
            CollectFiles(input.ResourceIds, snapshot, candidate.SupportedExtensions, input.FileSelectionMode);

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
        var candidates = await BuildCandidatesAsync(await resourceSource.GetSnapshotAsync(resourceIds, ct), ct);

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
        var candidate = FindCandidate(
            await BuildCandidatesAsync(await resourceSource.GetSnapshotAsync(resourceIds, ct), ct), playerKey);

        var matched = snapshot.Entries
            .Where(e => MatchesPlayer(candidate.SupportedExtensions, e.Path))
            .ToList();
        var existing = matched
            .Select(e => (Entry: e, Resolved: fileResolver.Resolve(e.Path)))
            .Where(e => e.Resolved != null)
            .ToList();
        var missingFileCount = matched.Count - existing.Count;

        if (existing.Count == 0)
        {
            throw new InvalidOperationException(
                $"The playlist has no existing file that {candidate.DisplayName} supports.");
        }

        GuardTotalFiles(existing.Count);

        var launchMethod = await LaunchAsync(candidate, existing.Select(e => e.Resolved!).ToList(), ct);

        // One history entry per distinct backing resource, first included file each — as
        // the library names it, not as this machine reached it.
        var includedResources = existing.Where(e => e.Entry.ResourceId.HasValue)
            .GroupBy(e => e.Entry.ResourceId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Entry.Path);
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
    private async Task<List<BatchPlayCandidate>> BuildCandidatesAsync(BatchPlayResourceSnapshot snapshot,
        CancellationToken ct)
    {
        var candidates = new List<BatchPlayCandidate>();
        var coveredExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var player in snapshot.ConfiguredPlayers)
        {
            ct.ThrowIfCancellationRequested();

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

    private (List<string> Files, Dictionary<int, string> IncludedResources,
        List<BatchPlaySkippedResource> Skipped, int MissingFileCount)
        CollectFiles(int[] resourceIds, BatchPlayResourceSnapshot snapshot,
            IReadOnlySet<string>? supportedExtensions, BatchPlayFileSelectionMode mode)
    {
        var filesByResource = snapshot.Resources.ToDictionary(r => r.ResourceId, r => r.Files);
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
            var existing = matching
                .Select(p => (Source: p, Resolved: fileResolver.Resolve(p)))
                .Where(p => p.Resolved != null)
                .ToList();
            missingFileCount += matching.Count - existing.Count;

            if (existing.Count == 0)
            {
                skipped.Add(new BatchPlaySkippedResource(resourceId, BatchPlaySkipReason.AllFilesMissing));
                continue;
            }

            var selected = mode == BatchPlayFileSelectionMode.FirstFilePerResource
                ? existing[..1]
                : existing;

            files.AddRange(selected.Select(s => s.Resolved!));

            // History records the file as the library names it, not as this machine
            // reached it — the server owns that record and has never seen a local mount.
            includedResources[resourceId] = selected[0].Source;
        }

        return (files, includedResources, skipped, missingFileCount);
    }

    /// <param name="files">Already resolved for this machine by <see cref="IBatchPlayFileResolver"/>.</param>
    private async Task<BatchPlayLaunchMethod> LaunchAsync(BatchPlayCandidate candidate, List<string> files,
        CancellationToken ct)
    {
        string arguments;
        BatchPlayLaunchMethod method;

        if (files.Count == 1)
        {
            // A single file works through the plain template for any player.
            arguments = BatchPlayArguments.BuildFromTemplate(candidate.CommandTemplate, files[0]);
            method = BatchPlayLaunchMethod.MultiFileArguments;
        }
        else if (candidate.Capabilities.HasFlag(BatchPlayCapability.PlaylistFile))
        {
            var directory = options.Value.TempPlaylistDirectory ??
                            Path.Combine(Path.GetTempPath(), "bakabase", "playlists");
            M3u8Playlist.SweepOldFiles(directory, options.Value.TempPlaylistRetention);
            var playlistPath = await M3u8Playlist.WriteTempFileAsync(directory,
                files.Select(f => new M3u8Entry(f)), ct);
            arguments = BatchPlayArguments.BuildFromTemplate(candidate.CommandTemplate, playlistPath);
            method = BatchPlayLaunchMethod.PlaylistFile;
        }
        else if (candidate.Capabilities.HasFlag(BatchPlayCapability.MultiFileArguments))
        {
            arguments = BatchPlayArguments.BuildMultiFile(files);
            method = BatchPlayLaunchMethod.MultiFileArguments;
        }
        else
        {
            throw new InvalidOperationException(
                $"{candidate.DisplayName} does not support opening multiple files at once.");
        }

        var argumentPrefix = KnownPlayerDefinitions.MatchByExecutable(candidate.ExecutablePath)?.ArgumentPrefix;
        if (argumentPrefix != null) arguments = $"{argumentPrefix} {arguments}";
        if (method == BatchPlayLaunchMethod.MultiFileArguments &&
            arguments.Length > options.Value.MaxCommandLineLength)
        {
            throw new InvalidOperationException(
                "Too many files for this player's command line. Pick a player with playlist support.");
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
            await resourceSource.MarkPlayedAsync(includedResources, CancellationToken.None);
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
}
