using Bakabase.Remoting.Components.Connection;
using Bakabase.Modules.Player.Abstractions.Components;
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
    ILogger<UpstreamBatchPlayResourceSource> logger) : IBatchPlayResourceSource
{
    public async Task<BatchPlayResourceSnapshot> GetSnapshotAsync(int[] resourceIds, CancellationToken ct)
    {
        return await upstream.GetBatchPlayResourceSnapshotAsync(resourceIds, ct) ??
               throw new InvalidOperationException(
                   "Could not ask the server what is playable in the selection. " +
                   "Check that it is still reachable.");
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
