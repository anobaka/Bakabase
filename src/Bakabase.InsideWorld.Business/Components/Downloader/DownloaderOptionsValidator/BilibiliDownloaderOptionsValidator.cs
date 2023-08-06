using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Constants;
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

        public BilibiliDownloaderOptionsValidator(IStringLocalizer<SharedResource> localizer, IWebHostEnvironment env,
            InsideWorldOptionsManagerPool optionsManager)
        {
            _localizer = localizer;
            _env = env;
            _optionsManager = optionsManager;
        }

        public ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;

        public async Task<BaseResponse> Validate()
        {
            var options = (_optionsManager.Bilibili).Value;
            if (options.Cookie.IsNullOrEmpty())
            {
                return BaseResponseBuilder.BuildBadRequest("Cookie is empty");
            }

            const string ffmpegExecutable = "ffmpeg.exe";
            if (FileUtils.GetFullname(ffmpegExecutable) == null)
            {
                throw new Exception("ffmpeg.exe is not found in environment variables.");
            }

            var luxPath = BilibiliDownloader.BuildLuxBinPath(_env);
            if (!File.Exists(luxPath))
            {
                throw new Exception("lux.exe is not found");
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