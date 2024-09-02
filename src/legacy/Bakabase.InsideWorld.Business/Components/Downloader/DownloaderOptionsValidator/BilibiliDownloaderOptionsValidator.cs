using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.DownloaderOptionsValidator
{
    public class BilibiliDownloaderOptionsValidator : IDownloaderOptionsValidator
    {
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly IWebHostEnvironment _env;
        private readonly InsideWorldOptionsManagerPool _optionsManager;
        private readonly FfMpegService _ffMpegService;
        private readonly LuxService _luxService;

        public BilibiliDownloaderOptionsValidator(IStringLocalizer<SharedResource> localizer, IWebHostEnvironment env,
            InsideWorldOptionsManagerPool optionsManager, FfMpegService ffMpegService, LuxService luxService)
        {
            _localizer = localizer;
            _env = env;
            _optionsManager = optionsManager;
            _ffMpegService = ffMpegService;
            _luxService = luxService;
        }

        public ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;

        public async Task<BaseResponse> Validate()
        {
            var options = (_optionsManager.Bilibili).Value;

            if (_ffMpegService.Status != DependentComponentStatus.Installed)
            {
                throw new Exception("ffmpeg is not ready, please check it in Configuration");
            }

            if (_luxService.Status != DependentComponentStatus.Installed)
            {
                throw new Exception("lux is not ready, please check it in Configuration");
            }

            if (options.Cookie.IsNullOrEmpty())
            {
                return SingletonResponseBuilder<SimpleBiliBiliFavoriteCollectProgressorRequestModel>.BuildBadRequest(
                    "Cookie is not set");
            }

            if (options.Downloader != null && options.Downloader.DefaultPath.IsNullOrEmpty())
            {
                return SingletonResponseBuilder<SimpleBiliBiliFavoriteCollectProgressorRequestModel>.BuildBadRequest(
                    "Download path is not set");
            }

            return BaseResponseBuilder.Ok;
        }
    }
}