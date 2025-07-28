using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;

public class BilibiliDownloaderHelper(
    IBOptionsManager<BilibiliOptions> optionsManager,
    IDownloaderLocalizer localizer,
    BilibiliClient bilibiliClient,
    HttpClient httpClient,
    FfMpegService ffMpegService,
    LuxService luxService) : AbstractDownloaderHelper<BilibiliOptions>(optionsManager, localizer, httpClient)
{
    private readonly IDownloaderLocalizer _localizer = localizer;
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;

    protected override async Task<BaseResponse> ValidateAdditional(DownloaderOptions options)
    {
        if (ffMpegService.Status != DependentComponentStatus.Installed)
        {
            return BaseResponseBuilder.BuildBadRequest(_localizer.FfMpegIsNotReady());
        }

        if (luxService.Status != DependentComponentStatus.Installed)
        {
            return BaseResponseBuilder.BuildBadRequest(_localizer.LuxIsNotReady());
        }

        return await base.ValidateAdditional(options);
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        if (model.Keys.Any())
        {
            var key = model.Keys[0];
            if (long.TryParse(key, out var fId))
            {
                var favoritesList = await bilibiliClient.GetFavorites();
                var favorites = favoritesList.FirstOrDefault(f => f.Id == fId);
                if (favorites != null)
                {
                    model.Names = [favorites.Title];
                    return BaseResponseBuilder.Ok;
                }
            }
        }

        return BaseResponseBuilder.BuildBadRequest(_localizer.InvalidFavorites());
    }
}