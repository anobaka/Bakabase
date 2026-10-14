using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.PlayableItem;

public class PlayableItemProviderService : IPlayableItemProviderService
{
    private readonly IEnumerable<IPlayableItemProvider> _providers;

    public PlayableItemProviderService(IEnumerable<IPlayableItemProvider> providers)
    {
        _providers = providers;
    }

    public async Task<AggregatedPlayableItems> GetPlayableItemsAsync(DomainResource resource, CancellationToken ct)
    {
        var allItems = new List<Abstractions.Models.Domain.PlayableItem>();

        var sortedProviders = _providers
            .Where(p => p.AppliesTo(resource))
            .OrderBy(p => p.Priority);

        foreach (var provider in sortedProviders)
        {
            var result = await provider.GetPlayableItemsAsync(resource, ct);
            allItems.AddRange(result.Items);
        }

        return new AggregatedPlayableItems(allItems);
    }

    public async Task PlayAsync(DomainResource resource, DataOrigin origin, string key, CancellationToken ct)
    {
        var provider = _providers.FirstOrDefault(p => p.Origin == origin);
        if (provider == null)
        {
            // Download origins describe where files came from, not an alternative
            // playable target. A stale page/client must not report a website visit
            // (or no action at all) as a successful play in the resource history.
            throw new InvalidOperationException(origin is DataOrigin.DLsite or DataOrigin.ExHentai
                ? "Play the downloaded files from this resource. Refresh its playable files; opening the original website is a separate action."
                : $"No playable item provider is available for {origin}.");
        }

        var item = new Abstractions.Models.Domain.PlayableItem
        {
            Origin = origin,
            Key = key
        };

        await provider.PlayAsync(resource, item, ct);
    }

    public async Task InvalidateAllAsync(int resourceId)
    {
        foreach (var provider in _providers)
        {
            await provider.InvalidateAsync(resourceId);
        }
    }
}
