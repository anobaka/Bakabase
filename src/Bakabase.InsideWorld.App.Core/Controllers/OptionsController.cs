using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Configs.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Models.RequestModels.Options;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("options")]
    public class OptionsController : Controller
    {
        private readonly IStringLocalizer<SharedResource> _prevLocalizer;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
        private readonly InsideWorldLocalizer _localizer;
        private readonly IGuiAdapter _guiAdapter;
        private readonly FfMpegService _ffMpegInstaller;

        public OptionsController(IStringLocalizer<SharedResource> prevLocalizer,
            IBOptionsManager<AppOptions> appOptionsManager,
            InsideWorldOptionsManagerPool insideWorldOptionsManager,  InsideWorldLocalizer localizer, IGuiAdapter guiAdapter, FfMpegService ffMpegInstaller)
        {
            _prevLocalizer = prevLocalizer;
            _appOptionsManager = appOptionsManager;
            _insideWorldOptionsManager = insideWorldOptionsManager;
            _localizer = localizer;
            _guiAdapter = guiAdapter;
            _ffMpegInstaller = ffMpegInstaller;
        }

        [HttpGet("app")]
        [SwaggerOperation(OperationId = "GetAppOptions")]
        public async Task<SingletonResponse<AppOptions>> GetAppOptions()
        {
            return new SingletonResponse<AppOptions>(_appOptionsManager.Value);
        }

        [HttpPatch("app")]
        [SwaggerOperation(OperationId = "PatchAppOptions")]
        public async Task<BaseResponse> PatchAppOptions([FromBody] AppOptionsPatchRequestModel model)
        {
            UiTheme? newUiTheme = null;
            await _appOptionsManager.SaveAsync(options =>
            {
                if (model.Language.IsNotEmpty())
                {
                    if (options.Language != model.Language)
                    {
                        options.Language = model.Language;
                        AppService.SetCulture(options.Language);
                    }
                }

                if (model.EnableAnonymousDataTracking.HasValue)
                {
                    options.EnableAnonymousDataTracking = model.EnableAnonymousDataTracking.Value;
                }

                if (model.EnablePreReleaseChannel.HasValue)
                {
                    options.EnablePreReleaseChannel = model.EnablePreReleaseChannel.Value;
                }

                if (model.CloseBehavior.HasValue)
                {
                    options.CloseBehavior = model.CloseBehavior.Value;
                }

                if (model.UiTheme.HasValue && model.UiTheme != options.UiTheme)
                {
                    options.UiTheme = model.UiTheme.Value;
                    newUiTheme = model.UiTheme;
                }
            });

            if (newUiTheme.HasValue)
            {
                _guiAdapter.ChangeUiTheme(newUiTheme.Value);
            }

            return BaseResponseBuilder.Ok;
        }

        //[HttpPatch("app-options")]
        //[SwaggerOperation(OperationId = "PatchAppOptions")]
        //public async Task<BaseResponse> PatchOptions([FromBody] InsideWorldAppOptionsUpdateRequestModel model)
        //{
        //    if (options.FileMover != null)
        //    {
        //        var result = options.FileMover.StandardizeAndValidate(_prevLocalizer);
        //        if (result.Code != 0)
        //        {
        //            return result;
        //        }
        //    }

        //    await InsideWorldAppService.SaveInsideWorldOptions((Action<InsideWorldAppOptions>)(a =>
        //    {

        //        if (options.ResourceColCount > 0)
        //        {
        //            a.ResourceColCount = options.ResourceColCount;
        //        }


        //        if (options.ResourceSearchRequestModelSlot != null)
        //        {
        //            a.ResourceSearchRequestModelSlot = options.ResourceSearchRequestModelSlot;
        //        }

        //        if (options.JavLibraryCookie != null)
        //        {
        //            a.JavLibraryCookie = options.JavLibraryCookie;
        //        }

        //        if (options.JavLibraryDownloadPath != null)
        //        {
        //            a.JavLibraryDownloadPath = options.JavLibraryDownloadPath;
        //        }

        //        if (options.JavLibraryTorrentLinkKeywords != null)
        //        {
        //            a.JavLibraryTorrentLinkKeywords = options.JavLibraryTorrentLinkKeywords.Distinct().ToArray();
        //        }

        //        if (options.JavLibraryUrls != null)
        //        {
        //            a.JavLibraryUrls = options.JavLibraryUrls.Distinct().ToArray();
        //        }

        //        if (options.MediaLibrarySyncInterval.HasValue)
        //        {
        //            a.MediaLibrarySyncInterval = options.MediaLibrarySyncInterval.Value;
        //        }

        //        if (options.AdditionalCoverDiscoveringSources != null)
        //        {
        //            a.AdditionalCoverDiscoveringSources = options.AdditionalCoverDiscoveringSources;
        //        }

        //        if (options.FileExplorerWorkingDirectory.IsNotEmpty())
        //        {
        //            a.FileExplorerWorkingDirectory = options.FileExplorerWorkingDirectory;
        //        }

        //        if (options.ExHentaiCookie.IsNotEmpty())
        //        {
        //            a.ExHentaiCookie = options.ExHentaiCookie;
        //        }

        //        if (options.ExHentaiExcludedTags != null)
        //        {
        //            a.ExHentaiExcludedTags =
        //                options.ExHentaiExcludedTags.Where(t => t.IsNotEmpty()).Distinct().ToArray();
        //        }

        //        if (options.ExHentaiDownloadPath.IsNotEmpty())
        //        {
        //            a.ExHentaiDownloadPath = options.ExHentaiDownloadPath;
        //        }

        //        if (options.ExHentaiLinks != null)
        //        {
        //            a.ExHentaiLinks =
        //                options.ExHentaiLinks.Where(t => t.IsNotEmpty()).Select(a => a.Trim()).Distinct().ToArray();
        //        }

        //        if (options.ExHentaiDownloadThreads > 0)
        //        {
        //            a.ExHentaiDownloadThreads = options.ExHentaiDownloadThreads;
        //        }

        //        if (options.FileMover != null)
        //        {
        //            a.FileMover = options.FileMover.ToOptions();
        //        }

        //        if (options.PixivDownloadInterval.HasValue)
        //        {
        //            a.PixivDownloadInterval = options.PixivDownloadInterval.Value;
        //        }

        //        if (options.PixivCookie.IsNotEmpty())
        //        {
        //            a.PixivCookie = options.PixivCookie;
        //        }

        //        if (options.PixivDownloadPath.IsNotEmpty())
        //        {
        //            a.PixivDownloadPath = options.PixivDownloadPath;
        //        }

        //        if (options.PixivDownloadThreads > 0)
        //        {
        //            a.PixivDownloadThreads = options.PixivDownloadThreads;
        //        }
        //    }));
        //    return BaseResponseBuilder.Ok.WithLocalization(_prevLocalizer);
        //}

        [HttpGet("ui")]
        [SwaggerOperation(OperationId = "GetUIOptions")]
        public async Task<SingletonResponse<UIOptions>> GetUIOptions()
        {
            return new SingletonResponse<UIOptions>((_insideWorldOptionsManager.UI).Value);
        }

        [HttpPatch("ui")]
        [SwaggerOperation(OperationId = "PatchUIOptions")]
        public async Task<BaseResponse> PatchUIOptions([FromBody] UIOptionsPatchRequestModel model)
        {
            await _insideWorldOptionsManager.UI.SaveAsync(options =>
            {
                if (model.Resource != null)
                {
                    options.Resource = model.Resource;
                }

                if (model.StartupPage.HasValue)
                {
                    options.StartupPage = model.StartupPage.Value;
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("bilibili")]
        [SwaggerOperation(OperationId = "GetBilibiliOptions")]
        public async Task<SingletonResponse<BilibiliOptions>> GetBilibiliOptions()
        {
            return new SingletonResponse<BilibiliOptions>((_insideWorldOptionsManager.Bilibili).Value);
        }

        [HttpPatch("bilibili")]
        [SwaggerOperation(OperationId = "PatchBilibiliOptions")]
        public async Task<BaseResponse> PatchBilibiliOptions([FromBody] BilibiliOptions model)
        {
            await _insideWorldOptionsManager.Bilibili.SaveAsync(options =>
            {
                if (model.Cookie.IsNotEmpty())
                {
                    options.Cookie = model.Cookie;
                }

                if (model.Downloader != null)
                {
                    options.Downloader = model.Downloader;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("exhentai")]
        [SwaggerOperation(OperationId = "GetExHentaiOptions")]
        public async Task<SingletonResponse<ExHentaiOptions>> GetExHentaiOptions()
        {
            return new SingletonResponse<ExHentaiOptions>((_insideWorldOptionsManager.ExHentai).Value);
        }

        [HttpPatch("exhentai")]
        [SwaggerOperation(OperationId = "PatchExHentaiOptions")]
        public async Task<BaseResponse> PatchExHentaiOptions([FromBody] ExHentaiOptions model)
        {
            await _insideWorldOptionsManager.ExHentai.SaveAsync(options =>
            {
                if (model.Cookie.IsNotEmpty())
                {
                    options.Cookie = model.Cookie;
                }

                if (model.Downloader != null)
                {
                    options.Downloader = model.Downloader;
                }

                if (model.Enhancer != null)
                {
                    options.Enhancer = model.Enhancer;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("filesystem")]
        [SwaggerOperation(OperationId = "GetFileSystemOptions")]
        public async Task<SingletonResponse<FileSystemOptions>> GetFileSystemOptions()
        {
            return new SingletonResponse<FileSystemOptions>((_insideWorldOptionsManager.FileSystem)
                .Value);
        }

        [HttpPatch("filesystem")]
        [SwaggerOperation(OperationId = "PatchFileSystemOptions")]
        public async Task<BaseResponse> PatchFileSystemOptions([FromBody] FileSystemOptions model)
        {
            var result = model.FileMover.StandardizeAndValidate(_prevLocalizer);
            if (result.Code != 0)
            {
                return result;
            }

            await _insideWorldOptionsManager.FileSystem.SaveAsync(options =>
            {
                if (model.FileMover != null)
                {
                    options.FileMover = model.FileMover;
                }

                if (model.RecentMovingDestinations != null)
                {
                    options.RecentMovingDestinations = model.RecentMovingDestinations;
                }

                if (model.FileProcessor != null)
                {
                    options.FileProcessor = model.FileProcessor;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("javlibrary")]
        [SwaggerOperation(OperationId = "GetJavLibraryOptions")]
        public async Task<SingletonResponse<JavLibraryOptions>> GetJavLibraryOptions()
        {
            return new SingletonResponse<JavLibraryOptions>((_insideWorldOptionsManager.JavLibrary)
                .Value);
        }

        [HttpPatch("javlibrary")]
        [SwaggerOperation(OperationId = "PatchJavLibraryOptions")]
        public async Task<BaseResponse> PatchJavLibraryOptions([FromBody] JavLibraryOptions model)
        {
            await _insideWorldOptionsManager.JavLibrary.SaveAsync(options =>
            {
                if (model.Cookie.IsNotEmpty())
                {
                    options.Cookie = model.Cookie;
                }

                if (model.Collector != null)
                {
                    options.Collector = model.Collector;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("pixiv")]
        [SwaggerOperation(OperationId = "GetPixivOptions")]
        public async Task<SingletonResponse<PixivOptions>> GetPixivOptions()
        {
            return new SingletonResponse<PixivOptions>((_insideWorldOptionsManager.Pixiv).Value);
        }

        [HttpPatch("pixiv")]
        [SwaggerOperation(OperationId = "PatchPixivOptions")]
        public async Task<BaseResponse> PatchPixivOptions([FromBody] PixivOptions model)
        {
            await _insideWorldOptionsManager.Pixiv.SaveAsync(options =>
            {
                if (model.Cookie.IsNotEmpty())
                {
                    options.Cookie = model.Cookie;
                }

                if (model.Downloader != null)
                {
                    options.Downloader = model.Downloader;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("resource")]
        [SwaggerOperation(OperationId = "GetResourceOptions")]
        public async Task<SingletonResponse<ResourceOptionsDto>> GetResourceOptions()
        {
            return new SingletonResponse<ResourceOptionsDto>((_insideWorldOptionsManager.Resource)
                .Value.ToDto());
        }

        [HttpPatch("resource")]
        [SwaggerOperation(OperationId = "PatchResourceOptions")]
        public async Task<BaseResponse> PatchResourceOptions([FromBody] ResourceOptionsPatchRequestModel model)
        {
            await _insideWorldOptionsManager.Resource.SaveAsync(options =>
            {
                if (model.LastSearch != null)
                {
                    options.LastSearch = model.LastSearch.ToOptions();
                }

                if (model.LastNfoGenerationDt.HasValue)
                {
                    options.LastNfoGenerationDt = model.LastNfoGenerationDt.Value;
                }

                if (model.LastSyncDt.HasValue)
                {
                    options.LastSyncDt = model.LastSyncDt.Value;
                }

                if (model.AdditionalCoverDiscoveringSources != null)
                {
                    options.AdditionalCoverDiscoveringSources = model.AdditionalCoverDiscoveringSources;
                }

                if (model.SearchSlots != null)
                {
                    options.SearchSlots = model.SearchSlots?.Select(a => a.ToOptions()).ToList();
                }

                if (model.CoverOptions != null)
                {
                    options.CoverOptions = model.CoverOptions;
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("thirdparty")]
        [SwaggerOperation(OperationId = "GetThirdPartyOptions")]
        public async Task<SingletonResponse<ThirdPartyOptions>> GetThirdPartyOptions()
        {
            return new SingletonResponse<ThirdPartyOptions>((_insideWorldOptionsManager.ThirdParty)
                .Value);
        }

        [HttpPatch("thirdparty")]
        [SwaggerOperation(OperationId = "PatchThirdPartyOptions")]
        public async Task<BaseResponse> PatchThirdPartyOptions([FromBody] ThirdPartyOptions model)
        {
            await _insideWorldOptionsManager.ThirdParty.SaveAsync(options =>
            {
                if (model.SimpleSearchEngines != null)
                {
                    options.SimpleSearchEngines = model.SimpleSearchEngines;
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("network")]
        [SwaggerOperation(OperationId = "GetNetworkOptions")]
        public async Task<SingletonResponse<NetworkOptions>> GetNetworkOptions()
        {
            return new SingletonResponse<NetworkOptions>((_insideWorldOptionsManager.Network).Value);
        }

        [HttpPatch("network")]
        [SwaggerOperation(OperationId = "PatchNetworkOptions")]
        public async Task<BaseResponse> PatchNetworkOptions([FromBody] NetworkOptions model)
        {
            await _insideWorldOptionsManager.Network.SaveAsync(options =>
            {
                if (model.Proxy != null)
                {
                    options.Proxy = model.Proxy;
                }

            });
            return BaseResponseBuilder.Ok;
        }
    }
}