using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.PlayList.Services;
using Bakabase.Modules.Player.Abstractions.Components;
using Bootstrap.Extensions;

namespace Bakabase.Service.Services;

/// <summary>
/// Adapter behind <see cref="IBatchPlayPlaylistSource"/>: resolves playlist
/// items to playable files. File items pass through as-is; resource items go
/// through playable-file discovery (profile-filtered, cache-first) so a
/// resource behaves the same whether batch-played from a selection or from a
/// playlist.
/// </summary>
public class PlaylistBatchPlaySource(IPlayListService playListService, IResourceService resourceService)
    : IBatchPlayPlaylistSource
{
    public async Task<BatchPlayPlaylistSnapshot?> GetSnapshotAsync(int playlistId, CancellationToken ct)
    {
        var playlist = await playListService.Get(playlistId);
        if (playlist == null)
        {
            return null;
        }

        var entries = new List<BatchPlayPlaylistEntry>();
        foreach (var item in playlist.Items ?? [])
        {
            ct.ThrowIfCancellationRequested();

            if (item.File.IsNotEmpty())
            {
                entries.Add(new BatchPlayPlaylistEntry(item.File!, null));
            }
            else if (item.ResourceId.HasValue)
            {
                var playableItems = await resourceService.DiscoverPlayableItems(item.ResourceId.Value, ct);
                entries.AddRange(playableItems
                    .Where(i => i.Origin == DataOrigin.FileSystem && !string.IsNullOrEmpty(i.Key))
                    .Select(i => new BatchPlayPlaylistEntry(i.Key, item.ResourceId)));
            }
        }

        return new BatchPlayPlaylistSnapshot(playlist.Name, entries);
    }
}
