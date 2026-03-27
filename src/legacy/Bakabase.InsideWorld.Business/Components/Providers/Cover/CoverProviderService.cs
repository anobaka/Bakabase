using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.Cover;

public class CoverProviderService : ICoverProviderService
{
    private readonly IReadOnlyList<ICoverProvider> _providers;

    public CoverProviderService(IEnumerable<ICoverProvider> providers)
    {
        _providers = providers.OrderBy(p => p.Priority).ToList();
    }

    public async Task<List<string>?> ResolveCoversAsync(DomainResource resource, CancellationToken ct)
    {
        foreach (var provider in _providers)
        {
            if (!provider.AppliesTo(resource))
            {
                continue;
            }

            var covers = await provider.GetCoversAsync(resource, ct);
            if (covers != null)
            {
                return covers;
            }
        }

        return null;
    }

    public async Task InvalidateAllAsync(int resourceId)
    {
        foreach (var provider in _providers)
        {
            await provider.InvalidateAsync(resourceId);
        }
    }
}
