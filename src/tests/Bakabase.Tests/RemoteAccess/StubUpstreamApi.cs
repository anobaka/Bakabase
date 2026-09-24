using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Modules.Player.Abstractions.Components;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The server, as far as a user-machine handler can tell.
/// </summary>
/// <remarks>
/// One stub for every handler test, because the distinction each of them turns on is the
/// same one: a field left null means the server did not answer, and the handler has to
/// report that as its own kind of failure rather than blaming the user's configuration.
/// </remarks>
public sealed class StubUpstreamApi : IUpstreamApi
{
    public UpstreamResource? Resource;
    public ResourceProfilePlayerOptions? PlayerOptions;
    public string? ArtifactPath;
    public List<PlayableItem>? PlayableItems;
    public UpstreamRandomPick? RandomPick;

    /// <summary>Every play the handlers reported, in order.</summary>
    public readonly List<(int Id, string Item)> Played = [];

    public Task<UpstreamResource?> GetResourceAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Resource);

    public Task<ResourceProfilePlayerOptions?> GetEffectivePlayerOptionsAsync(int id,
        CancellationToken ct = default) => Task.FromResult(PlayerOptions);

    public Task MarkPlayedAsync(int id, string item, CancellationToken ct = default)
    {
        Played.Add((id, item));

        return Task.CompletedTask;
    }

    public Task<string?> GetAigcArtifactPathAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(ArtifactPath);

    public Task<List<PlayableItem>?> GetPlayableItemsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(PlayableItems);

    public Task<UpstreamRandomPick?> PickRandomPlayableItemAsync(CancellationToken ct = default) =>
        Task.FromResult(RandomPick);

    public BatchPlayResourceSnapshot? BatchPlayResources;
    public UpstreamPlaylistSnapshot? BatchPlayPlaylist;

    /// <summary>Every bulk history write, in order.</summary>
    public readonly List<IReadOnlyDictionary<int, string>> BulkPlayed = [];

    /// <summary>Set to make the history write fail, as an unreachable server would.</summary>
    public Exception? MarkManyPlayedThrows;

    public Task<BatchPlayResourceSnapshot?> GetBatchPlayResourceSnapshotAsync(int[] resourceIds,
        CancellationToken ct = default) => Task.FromResult(BatchPlayResources);

    public Task<UpstreamPlaylistSnapshot?> GetBatchPlayPlaylistSnapshotAsync(int playlistId,
        CancellationToken ct = default) => Task.FromResult(BatchPlayPlaylist);

    public DLsiteWorkLaunchTarget? DLsiteLaunchTarget;

    public Task<DLsiteWorkLaunchTarget?> GetDLsiteWorkLaunchTargetAsync(string workId,
        CancellationToken ct = default) => Task.FromResult(DLsiteLaunchTarget);

    public Task MarkManyPlayedAsync(IReadOnlyDictionary<int, string> playedByResourceId,
        CancellationToken ct = default)
    {
        if (MarkManyPlayedThrows != null)
        {
            return Task.FromException(MarkManyPlayedThrows);
        }

        BulkPlayed.Add(playedByResourceId);

        return Task.CompletedTask;
    }
}
