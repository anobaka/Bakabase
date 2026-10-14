using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Player.Abstractions.Components;

namespace Bakabase.Modules.Player.Services;

/// <summary>
/// The all-in-one's answer to <see cref="IBatchPlayResourceSource"/>: read the selection
/// straight out of the resource layer.
/// </summary>
/// <remarks>
/// Resources are walked in the order they were selected — the same order the playlist
/// will follow — so the players a profile configures come out in a stable order too,
/// rather than whichever one the database happened to return rows in.
/// </remarks>
public class ResourceBatchPlaySource(
    IResourceService resourceService,
    IResourceProfileService resourceProfileService) : IBatchPlayResourceSource
{
    public async Task<BatchPlayResourceSnapshot> GetSnapshotAsync(int[] resourceIds, CancellationToken ct)
    {
        var byId = (await resourceService.GetByKeys(resourceIds)).ToDictionary(r => r.Id);
        var files = new List<BatchPlayResourceFiles>();
        var players = new List<MediaLibraryPlayer>();
        var optionsByResource = new Dictionary<int, ResourceProfilePlayerOptions?>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceId in resourceIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!byId.TryGetValue(resourceId, out var resource))
            {
                continue;
            }

            var items = await resourceService.DiscoverPlayableItems(resourceId, ct);
            files.Add(new BatchPlayResourceFiles(resourceId, items
                .Where(i => i.Origin == DataOrigin.FileSystem && !string.IsNullOrEmpty(i.Key))
                .Select(i => i.Key)
                .ToList()));

            var playerOptions = await resourceProfileService.GetEffectivePlayerOptions(resource);
            optionsByResource[resourceId] = playerOptions;
            foreach (var player in playerOptions?.Players ?? [])
            {
                if (!string.IsNullOrWhiteSpace(player.ExecutablePath) && covered.Add(player.ExecutablePath))
                {
                    players.Add(player);
                }
            }
        }

        return new BatchPlayResourceSnapshot(files, players, optionsByResource);
    }

    public Task MarkPlayedAsync(IReadOnlyDictionary<int, string> playedByResourceId, CancellationToken ct) =>
        resourceService.MarkPlayed(playedByResourceId);
}
