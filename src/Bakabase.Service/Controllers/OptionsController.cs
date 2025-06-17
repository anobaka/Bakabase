using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.RequestModels.Options;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
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
            InsideWorldOptionsManagerPool insideWorldOptionsManager, InsideWorldLocalizer localizer,
            IGuiAdapter guiAdapter, FfMpegService ffMpegInstaller)
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

                if (model.ListeningPort.HasValue)
                {
                    options.ListeningPort = model.ListeningPort.Value;
                }

            });

            if (newUiTheme.HasValue)
            {
                _guiAdapter.ChangeUiTheme(newUiTheme.Value);
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpPut("app")]
        [SwaggerOperation(OperationId = "PutAppOptions")]
        public async Task<BaseResponse> PutAppOptions([FromBody] AppOptions model)
        {
            await _appOptionsManager.SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }

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
        public async Task<SingletonResponse<ResourceOptions>> GetResourceOptions()
        {
            return new SingletonResponse<ResourceOptions>(_insideWorldOptionsManager.Resource.Value);
        }

        [HttpPatch("resource")]
        [SwaggerOperation(OperationId = "PatchResourceOptions")]
        public async Task<BaseResponse> PatchResourceOptions([FromBody] ResourceOptionsPatchInputModel model)
        {
            await _insideWorldOptionsManager.Resource.SaveAsync(options =>
            {
                if (model.AdditionalCoverDiscoveringSources != null)
                {
                    options.AdditionalCoverDiscoveringSources = model.AdditionalCoverDiscoveringSources;
                }

                if (model.CoverOptions != null)
                {
                    options.CoverOptions = model.CoverOptions;
                }

                if (model.PropertyValueScopePriority?.Any() == true)
                {
                    options.PropertyValueScopePriority = model.PropertyValueScopePriority;
                }

                if (model.SearchCriteria != null)
                {
                    options.LastSearchV2 = model.SearchCriteria.ToDbModel();
                }

                if (model.SynchronizationOptions != null)
                {
                    options.SynchronizationOptions = model.SynchronizationOptions.Optimize();
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
        public async Task<BaseResponse> PatchThirdPartyOptions([FromBody] ThirdPartyOptionsPatchInput model)
        {
            await _insideWorldOptionsManager.ThirdParty.SaveAsync(options =>
            {
                if (model.CurlExecutable != null)
                {
                    options.CurlExecutable = model.CurlExecutable;
                }

                if (model.AutomaticallyParsingPosts.HasValue)
                {
                    options.AutomaticallyParsingPosts = model.AutomaticallyParsingPosts.Value;
                }

                if (model.SimpleSearchEngines != null)
                {
                    // Replace the entire list if provided
                    options.SimpleSearchEngines = model.SimpleSearchEngines
                        .Select(x => new ThirdPartyOptions.SimpleSearchEngineOptions
                        {
                            Name = x.Name ?? string.Empty,
                            UrlTemplate = x.UrlTemplate ?? string.Empty
                        })
                        .ToList();
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("thirdparty")]
        [SwaggerOperation(OperationId = "PutThirdPartyOptions")]
        public async Task<BaseResponse> PutThirdPartyOptions([FromBody] ThirdPartyOptions model)
        {
            await _insideWorldOptionsManager.ThirdParty.SaveAsync(model);
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
        public async Task<BaseResponse> PatchNetworkOptions([FromBody] NetworkOptionsPatchInputModel model)
        {
            await _insideWorldOptionsManager.Network.SaveAsync(options =>
            {
                if (model.Proxy != null)
                {
                    options.Proxy = model.Proxy;
                }

                if (model.CustomProxies != null)
                {
                    options.CustomProxies = model.CustomProxies.Select(c => c.ToOptions()).ToList();
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("enhancer")]
        [SwaggerOperation(OperationId = "GetEnhancerOptions")]
        public async Task<SingletonResponse<EnhancerOptions>> GetEnhancerOptions()
        {
            return new SingletonResponse<EnhancerOptions>((_insideWorldOptionsManager.Enhancer).Value);
        }

        [HttpPatch("enhancer")]
        [SwaggerOperation(OperationId = "PatchEnhancerOptions")]
        public async Task<BaseResponse> PatchEnhancerOptions([FromBody] EnhancerOptions model)
        {
            await _insideWorldOptionsManager.Enhancer.SaveAsync(options =>
            {
                if (model.RegexEnhancer != null)
                {
                    options.RegexEnhancer = model.RegexEnhancer;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("task")]
        [SwaggerOperation(OperationId = "GetTaskOptions")]
        public async Task<SingletonResponse<TaskOptions>> GetTaskOptions()
        {
            return new SingletonResponse<TaskOptions>((_insideWorldOptionsManager.Task).Value);
        }

        [HttpPatch("task")]
        [SwaggerOperation(OperationId = "PatchTaskOptions")]
        public async Task<BaseResponse> PatchTaskOptions([FromBody] TaskOptions model)
        {
            await _insideWorldOptionsManager.Task.SaveAsync(options =>
            {
                if (model.Tasks != null)
                {
                    options.Tasks = model.Tasks.OrderBy(x => x.Id).ToList();
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("ai")]
        [SwaggerOperation(OperationId = "GetAIOptions")]
        public async Task<SingletonResponse<AiOptions>> GetAiOptions()
        {
            return new SingletonResponse<AiOptions>(_insideWorldOptionsManager.Ai.Value);
        }

        [HttpPatch("ai")]
        [SwaggerOperation(OperationId = "PatchAIOptions")]
        public async Task<BaseResponse> PatchAiOptions([FromBody] AiOptions model)
        {
            await _insideWorldOptionsManager.Ai.SaveAsync(options =>
            {
                if (model.OllamaEndpoint.IsNotEmpty())
                {
                    options.OllamaEndpoint = model.OllamaEndpoint;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("ai")]
        [SwaggerOperation(OperationId = "PutAIOptions")]
        public async Task<BaseResponse> PutAiOptions([FromBody] AiOptions model)
        {
            await _insideWorldOptionsManager.Ai.SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("soulplus")]
        [SwaggerOperation(OperationId = "GetSoulPlusOptions")]
        public async Task<SingletonResponse<SoulPlusOptions>> GetSoulPlusOptions()
        {
            return new SingletonResponse<SoulPlusOptions>(_insideWorldOptionsManager.SoulPlus.Value);
        }

        [HttpPatch("soulplus")]
        [SwaggerOperation(OperationId = "PatchSoulPlusOptions")]
        public async Task<BaseResponse> PatchSoulPlusOptions([FromBody] SoulPlusOptionsPatchInputModel model)
        {
            await _insideWorldOptionsManager.SoulPlus.SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.AutoBuyThreshold.HasValue)
                {
                    options.AutoBuyThreshold = model.AutoBuyThreshold.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("soulplus")]
        [SwaggerOperation(OperationId = "PutSoulPlusOptions")]
        public async Task<BaseResponse> PutSoulPlusOptions([FromBody] SoulPlusOptions model)
        {
            await _insideWorldOptionsManager.SoulPlus.SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }
    }
}