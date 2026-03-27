using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

public class DLsitePlayableItemProvider : IPlayableItemProvider
{
    private readonly IDLsiteWorkService _workService;

    public DLsitePlayableItemProvider(IDLsiteWorkService workService)
    {
        _workService = workService;
    }

    public DataOrigin Origin => DataOrigin.DLsite;
    public int Priority => 10;

    public bool AppliesTo(Resource resource)
    {
        return resource.SourceLinks?.Any(l => l.Source == ResourceSource.DLsite) == true;
    }

    public DataStatus GetStatus(Resource resource) => DataStatus.Ready;

    public async Task<PlayableItemProviderResult> GetPlayableItemsAsync(Resource resource, CancellationToken ct)
    {
        var sourceLink = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.DLsite);
        if (sourceLink == null)
            return new PlayableItemProviderResult([]);

        var sourceKey = sourceLink.SourceKey;
        var work = await _workService.GetByWorkId(sourceKey);
        var displayName = work?.Title ?? sourceKey;

        return new PlayableItemProviderResult(
        [
            new PlayableItem
            {
                Origin = DataOrigin.DLsite,
                Key = sourceKey,
                DisplayName = displayName
            }
        ]);
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        var url = $"https://www.dlsite.com/maniax/work/=/product_id/{item.Key}.html";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(url)
            {
                UseShellExecute = true
            }
        };
        process.Start();
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(int resourceId)
    {
        return Task.CompletedTask;
    }
}
