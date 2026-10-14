using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Components;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.BatchPlay;

/// <summary>
/// Reads the selection from the server instead of a database.
/// </summary>
/// <remarks>
/// The orchestration downstream of this is the server's own — which player, which files,
/// how to launch it — running here so it uses this machine's installed players and this
/// machine's filesystem. Only where the data comes from changes.
/// </remarks>
public sealed class UpstreamBatchPlayResourceSource(
    IUpstreamApi upstream,
    IPlayerExecutableLocator players,
    ILogger<UpstreamBatchPlayResourceSource> logger,
    ActiveConnection? connection = null,
    ISourceResourceProfilePlaybackService? profiles = null) : IBatchPlayResourceSource
{
    public async Task<BatchPlayResourceSnapshot> GetSnapshotAsync(int[] resourceIds, CancellationToken ct)
    {
        var snapshot = await upstream.GetBatchPlayResourceSnapshotAsync(resourceIds, ct) ??
                       throw new InvalidOperationException(
                           "Could not ask the server what is playable in the selection. " +
                           "Check that it is still reachable.");

        // Profile paths name programs on the managed server, not this computer.
        // Like single-file playback, only a known player found installed here
        // can carry that choice across. The shared orchestration then builds
        // its keys and deduplicates discovery against the actual local path.
        var configuredPlayers = new List<MediaLibraryPlayer>();
        var upstreamPlayers = snapshot.ConfiguredPlayers;
        if (profiles != null && connection?.Server != null)
        {
            var local = new Dictionary<int, ResourceProfileOpeningOptions>();
            foreach (var chunk in snapshot.Resources.Select(r => r.ResourceId).Distinct().Chunk(256))
                foreach (var match in await profiles.ResolveManyAsync(connection.Server.ServerId, null, chunk, ct))
                    local[match.Key] = match.Value;
            if (local.Values.Any(p => p.PlayerOptions != null))
            {
                upstreamPlayers = [];
                foreach (var resource in snapshot.Resources)
                {
                    ct.ThrowIfCancellationRequested();
                    if (local.TryGetValue(resource.ResourceId, out var matched) && matched.PlayerOptions != null)
                    {
                        // These paths and arguments were saved here. Never relocate a custom
                        // local application using the remote-known-player compatibility path below.
                        configuredPlayers.AddRange(matched.PlayerOptions.Players ?? []);
                    }
                    else
                    {
                        var inherited = snapshot.PlayerOptionsByResourceId != null &&
                                        snapshot.PlayerOptionsByResourceId.TryGetValue(resource.ResourceId, out var captured)
                            ? captured
                            : await upstream.GetEffectivePlayerOptionsAsync(resource.ResourceId, ct);
                        upstreamPlayers.AddRange(inherited?.Players ?? []);
                    }
                }
            }
        }
        foreach (var player in upstreamPlayers)
        {
            ct.ThrowIfCancellationRequested();
            var known = KnownPlayerDefinitions.MatchByExecutable(player.ExecutablePath);
            if (known == null) continue;
            var executable = players.Locate(known).FirstOrDefault();
            if (executable == null) continue;

            var extensions = player.Extensions?
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Select(e => e.StartsWith('.') ? e : $".{e}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (extensions is {Count: > 0} && known.SupportedExtensions != null)
            {
                // A remote profile cannot turn an AV player into an image
                // viewer. An empty intersection must be omitted, not passed
                // as an empty set that the orchestration treats as a default.
                extensions.IntersectWith(known.SupportedExtensions);
                if (extensions.Count == 0) continue;
            }
            else if (extensions is not {Count: > 0} && known.SupportedExtensions != null)
            {
                extensions = known.SupportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            configuredPlayers.Add(player with {ExecutablePath = executable, Extensions = extensions});
        }

        return snapshot with
        {
            ConfiguredPlayers = configuredPlayers.DistinctBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public async Task MarkPlayedAsync(IReadOnlyDictionary<int, string> playedByResourceId, CancellationToken ct)
    {
        try
        {
            await upstream.MarkManyPlayedAsync(playedByResourceId, ct);
        }
        catch (Exception e)
        {
            // Best effort by contract: the players are already running, and losing a
            // history row is not worth turning a successful play into a failure.
            logger.LogWarning(e, "Failed to record batch-play history");
        }
    }
}

/// <summary>
/// Reads a playlist from the server, already resolved to the files it would play.
/// </summary>
public sealed class UpstreamBatchPlayPlaylistSource(IUpstreamApi upstream) : IBatchPlayPlaylistSource
{
    public async Task<BatchPlayPlaylistSnapshot?> GetSnapshotAsync(int playlistId, CancellationToken ct)
    {
        var answer = await upstream.GetBatchPlayPlaylistSnapshotAsync(playlistId, ct) ??
                     throw new InvalidOperationException(
                         $"Could not ask the server about playlist {playlistId}. " +
                         "Check that it is still reachable.");

        // Null inside the answer is the server saying the playlist does not exist, which
        // the orchestration already reports properly. Not being able to ask is different,
        // and is the exception above.
        return answer.Snapshot;
    }
}
