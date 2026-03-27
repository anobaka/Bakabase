using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiPlayableItemProvider : IPlayableItemProvider
{
    private readonly IExHentaiGalleryService _galleryService;

    public ExHentaiPlayableItemProvider(IExHentaiGalleryService galleryService)
    {
        _galleryService = galleryService;
    }

    public DataOrigin Origin => DataOrigin.ExHentai;
    public int Priority => 10;

    public bool AppliesTo(Resource resource)
    {
        return resource.SourceLinks?.Any(l => l.Source == ResourceSource.ExHentai) == true;
    }

    public DataStatus GetStatus(Resource resource) => DataStatus.Ready;

    public async Task<PlayableItemProviderResult> GetPlayableItemsAsync(Resource resource, CancellationToken ct)
    {
        var sourceLink = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.ExHentai);
        if (sourceLink == null)
            return new PlayableItemProviderResult([]);

        var sourceKey = sourceLink.SourceKey;

        // sourceKey format is "GalleryId/GalleryToken"
        var galleries = await _galleryService.GetAll();
        var gallery = galleries.FirstOrDefault(g => $"{g.GalleryId}/{g.GalleryToken}" == sourceKey);
        var displayName = gallery?.Title ?? gallery?.TitleJpn ?? $"Gallery {sourceKey}";

        return new PlayableItemProviderResult(
        [
            new PlayableItem
            {
                Origin = DataOrigin.ExHentai,
                Key = sourceKey,
                DisplayName = displayName
            }
        ]);
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        var url = $"https://exhentai.org/g/{item.Key}/";
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
