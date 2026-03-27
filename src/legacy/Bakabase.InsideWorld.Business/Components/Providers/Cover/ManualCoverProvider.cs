using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.Cover;

/// <summary>
/// Returns covers explicitly set via ReservedProperty.Cover (user-set or enhancer-set).
/// Does NOT fall back to custom Attachment properties to avoid overriding
/// higher-accuracy providers (Steam, DLsite, local file discovery).
/// </summary>
public class ManualCoverProvider : ICoverProvider
{
    public DataOrigin Origin => DataOrigin.Manual;
    public int Priority => 0;

    public bool AppliesTo(DomainResource resource) => true;

    public DataStatus GetStatus(DomainResource resource) => DataStatus.Ready;

    public Task<List<string>?> GetCoversAsync(DomainResource resource, CancellationToken ct)
    {
        var covers = resource.Properties?.GetValueOrDefault((int)PropertyPool.Reserved)
            ?.GetValueOrDefault((int)Abstractions.Models.Domain.Constants.ReservedProperty.Cover)?.Values?.OrderBy(x => x.Scope)
            .Select(x => x.Value as List<string>)
            .OfType<List<string>>()
            .FirstOrDefault(x => x.Any());

        return Task.FromResult(covers is { Count: > 0 } ? covers : null);
    }

    public Task InvalidateAsync(int resourceId)
    {
        return Task.CompletedTask;
    }
}
