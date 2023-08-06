using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.DownloaderOptionsValidator
{
    public class PixivDownloaderOptionsValidator : IDownloaderOptionsValidator
    {
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly IWebHostEnvironment _env;
        private readonly IBOptionsManager<PixivOptions> _optionsManager;

        public PixivDownloaderOptionsValidator(IStringLocalizer<SharedResource> localizer, IWebHostEnvironment env,
            IBOptionsManager<PixivOptions> optionsManager)
        {
            _localizer = localizer;
            _env = env;
            _optionsManager = optionsManager;
        }

        public ThirdPartyId ThirdPartyId => ThirdPartyId.Pixiv;

        public async Task<BaseResponse> Validate()
        {
            var options = _optionsManager.Value;
            if (options.Cookie.IsNullOrEmpty())
            {
                return BaseResponseBuilder.BuildBadRequest("Cookie is not set.");
            }

            return BaseResponseBuilder.Ok;
        }
    }
}