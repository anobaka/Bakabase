using Bakabase.Modules.Player.Abstractions.Components;

namespace Bakabase.Modules.Player.Tests.Helpers;

internal sealed class FakeBatchPlayPlaylistSource : IBatchPlayPlaylistSource
{
    private readonly Dictionary<int, BatchPlayPlaylistSnapshot> _snapshots = [];

    public void Set(int playlistId, string name, params BatchPlayPlaylistEntry[] entries)
        => _snapshots[playlistId] = new BatchPlayPlaylistSnapshot(name, entries.ToList());

    public Task<BatchPlayPlaylistSnapshot?> GetSnapshotAsync(int playlistId, CancellationToken ct)
        => Task.FromResult(_snapshots.GetValueOrDefault(playlistId));
}
