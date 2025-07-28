using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
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
        private readonly BakabaseOptionsManagerPool _bakabaseOptionsManager;
        private readonly IGuiAdapter _guiAdapter;
        private readonly IPropertyService _propertyService;
        private readonly IPropertyLocalizer _propertyLocalizer;

        public OptionsController(IStringLocalizer<SharedResource> prevLocalizer,
            IBOptionsManager<AppOptions> appOptionsManager, BakabaseOptionsManagerPool bakabaseOptionsManager,
            IGuiAdapter guiAdapter, IPropertyService propertyService, IPropertyLocalizer propertyLocalizer)
        {
            _prevLocalizer = prevLocalizer;
            _appOptionsManager = appOptionsManager;
            _bakabaseOptionsManager = bakabaseOptionsManager;
            _guiAdapter = guiAdapter;
            _propertyService = propertyService;
            _propertyLocalizer = propertyLocalizer;
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
            return new SingletonResponse<UIOptions>(_bakabaseOptionsManager.Get<UIOptions>().Value);
        }

        [HttpPatch("ui")]
        [SwaggerOperation(OperationId = "PatchUIOptions")]
        public async Task<BaseResponse> PatchUIOptions([FromBody] UIOptionsPatchRequestModel model)
        {
            await _bakabaseOptionsManager.Get<UIOptions>().SaveAsync(options =>
            {
                if (model.Resource != null)
                {
                    options.Resource = model.Resource;
                }

                if (model.StartupPage.HasValue)
                {
                    options.StartupPage = model.StartupPage.Value;
                }

                if (model.IsMenuCollapsed.HasValue)
                {
                    options.IsMenuCollapsed = model.IsMenuCollapsed.Value;
                }

                if (model.LatestUsedProperties != null)
                {
                    options.LatestUsedProperties = model.LatestUsedProperties;
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("ui/latest-used-property")]
        [SwaggerOperation(OperationId = "AddLatestUsedProperty")]
        public async Task<BaseResponse> AddLatestUsedProperty([FromBody] UIOptions.PropertyKey[] models)
        {
            await _bakabaseOptionsManager.Get<UIOptions>().SaveAsync(options =>
            {
                foreach (var m in models)
                {
                    options.AddLatestUsedProperty(m.Pool, m.Id);
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("bilibili")]
        [SwaggerOperation(OperationId = "GetBilibiliOptions")]
        public async Task<SingletonResponse<BilibiliOptions>> GetBilibiliOptions()
        {
            return new SingletonResponse<BilibiliOptions>(_bakabaseOptionsManager.Get<BilibiliOptions>().Value);
        }

        [HttpPatch("bilibili")]
        [SwaggerOperation(OperationId = "PatchBilibiliOptions")]
        public async Task<BaseResponse> PatchBilibiliOptions([FromBody] BilibiliOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<BilibiliOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("exhentai")]
        [SwaggerOperation(OperationId = "GetExHentaiOptions")]
        public async Task<SingletonResponse<ExHentaiOptions>> GetExHentaiOptions()
        {
            return new SingletonResponse<ExHentaiOptions>(_bakabaseOptionsManager.Get<ExHentaiOptions>().Value);
        }

        [HttpPatch("exhentai")]
        [SwaggerOperation(OperationId = "PatchExHentaiOptions")]
        public async Task<BaseResponse> PatchExHentaiOptions([FromBody] ExHentaiOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<ExHentaiOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("filesystem")]
        [SwaggerOperation(OperationId = "GetFileSystemOptions")]
        public async Task<SingletonResponse<FileSystemOptions>> GetFileSystemOptions()
        {
            return new SingletonResponse<FileSystemOptions>(_bakabaseOptionsManager.Get<FileSystemOptions>().Value);
        }

        [HttpPatch("filesystem")]
        [SwaggerOperation(OperationId = "PatchFileSystemOptions")]
        public async Task<BaseResponse> PatchFileSystemOptions([FromBody] FileSystemOptionsPatchInputModel model)
        {
            if (model.FileMover != null)
            {
                var result = model.FileMover.StandardizeAndValidate(_prevLocalizer);
                if (result.Code != 0)
                {
                    return result;
                }
            }

            await _bakabaseOptionsManager.Get<FileSystemOptions>().SaveAsync(options =>
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
            return new SingletonResponse<JavLibraryOptions>(_bakabaseOptionsManager.Get<JavLibraryOptions>().Value);
        }

        [HttpPatch("javlibrary")]
        [SwaggerOperation(OperationId = "PatchJavLibraryOptions")]
        public async Task<BaseResponse> PatchJavLibraryOptions([FromBody] JavLibraryOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<JavLibraryOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
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
            return new SingletonResponse<PixivOptions>(_bakabaseOptionsManager.Get<PixivOptions>().Value);
        }

        [HttpPatch("pixiv")]
        [SwaggerOperation(OperationId = "PatchPixivOptions")]
        public async Task<BaseResponse> PatchPixivOptions([FromBody] PixivOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<PixivOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("resource")]
        [SwaggerOperation(OperationId = "GetResourceOptions")]
        public async Task<SingletonResponse<ResourceOptions>> GetResourceOptions()
        {
            return new SingletonResponse<ResourceOptions>(_bakabaseOptionsManager.Get<ResourceOptions>().Value);
        }

        [HttpPatch("resource")]
        [SwaggerOperation(OperationId = "PatchResourceOptions")]
        public async Task<BaseResponse> PatchResourceOptions([FromBody] ResourceOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<ResourceOptions>().SaveAsync(options =>
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

                if (model.RecentFilters != null)
                {
                    options.RecentFilters = model.RecentFilters;
                }

            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("resource/recent-filters")]
        [SwaggerOperation(OperationId = "GetRecentResourceFilters")]
        public async Task<ListResponse<ResourceSearchFilterViewModel>> GetRecentResourceFilters()
        {
            var recentFilters = _bakabaseOptionsManager.Get<ResourceOptions>().Value.RecentFilters ?? [];
            var dbModels = recentFilters.ToList();
            var propertyPool = (PropertyPool)dbModels.Select(x => x.PropertyPool).Cast<int>().Sum();
            var propertyMap = (await _propertyService.GetProperties(propertyPool)).ToMap();
            var viewModels = dbModels.Select(d =>
            {
                var p = propertyMap.GetValueOrDefault(d.PropertyPool)?.GetValueOrDefault(d.PropertyId);
                if (p == null)
                {
                    return null;
                }
                
                p = p.ConvertPropertyIfNecessary(d.Operation);
                
                var filter = new ResourceSearchFilter
                {
                    PropertyPool = d.PropertyPool,
                    PropertyId = d.PropertyId,
                    DbValue = d.DbValue.DeserializeDbValueAsStandardValue(p.Type),
                    Operation = d.Operation,
                    Property = p
                };

                return filter.ToViewModel(_propertyLocalizer);
            }).OfType<ResourceSearchFilterViewModel>().ToList();
            return new ListResponse<ResourceSearchFilterViewModel>(viewModels);
        }

        [HttpPost("resource/recent-filters")]
        [SwaggerOperation(OperationId = "AddRecentResourceFilter")]
        public async Task<BaseResponse> AddRecentResourceFilter([FromBody] ResourceOptions.ResourceFilter model)
        {
            var p = await _propertyService.GetProperty(model.PropertyPool, model.PropertyId);
            p = p.ConvertPropertyIfNecessary(model.Operation);
            var f = new ResourceSearchFilter
            {
                PropertyPool = model.PropertyPool,
                PropertyId = model.PropertyId,
                Operation = model.Operation,
                DbValue = model.DbValue.DeserializeDbValueAsStandardValue(p.Type),
                Property = p
            };
            
            if (f.IsValid())
            {
                await _bakabaseOptionsManager.Get<ResourceOptions>().SaveAsync(options =>
                {
                    options.AddRecentFilter(model);
                });
            }
            
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("thirdparty")]
        [SwaggerOperation(OperationId = "GetThirdPartyOptions")]
        public async Task<SingletonResponse<ThirdPartyOptions>> GetThirdPartyOptions()
        {
            return new SingletonResponse<ThirdPartyOptions>(_bakabaseOptionsManager.Get<ThirdPartyOptions>().Value);
        }

        [HttpPatch("thirdparty")]
        [SwaggerOperation(OperationId = "PatchThirdPartyOptions")]
        public async Task<BaseResponse> PatchThirdPartyOptions([FromBody] ThirdPartyOptionsPatchInput model)
        {
            await _bakabaseOptionsManager.Get<ThirdPartyOptions>().SaveAsync(options =>
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
            await _bakabaseOptionsManager.Get<ThirdPartyOptions>().SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("network")]
        [SwaggerOperation(OperationId = "GetNetworkOptions")]
        public async Task<SingletonResponse<NetworkOptions>> GetNetworkOptions()
        {
            return new SingletonResponse<NetworkOptions>(_bakabaseOptionsManager.Get<NetworkOptions>().Value);
        }

        [HttpPatch("network")]
        [SwaggerOperation(OperationId = "PatchNetworkOptions")]
        public async Task<BaseResponse> PatchNetworkOptions([FromBody] NetworkOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<NetworkOptions>().SaveAsync(options =>
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
            return new SingletonResponse<EnhancerOptions>(_bakabaseOptionsManager.Get<EnhancerOptions>().Value);
        }

        [HttpPatch("enhancer")]
        [SwaggerOperation(OperationId = "PatchEnhancerOptions")]
        public async Task<BaseResponse> PatchEnhancerOptions([FromBody] EnhancerOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<EnhancerOptions>().SaveAsync(options =>
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
            return new SingletonResponse<TaskOptions>(_bakabaseOptionsManager.Get<TaskOptions>().Value);
        }

        [HttpPatch("task")]
        [SwaggerOperation(OperationId = "PatchTaskOptions")]
        public async Task<BaseResponse> PatchTaskOptions([FromBody] TaskOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<TaskOptions>().SaveAsync(options =>
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
            return new SingletonResponse<AiOptions>(_bakabaseOptionsManager.Get<AiOptions>().Value);
        }

        [HttpPatch("ai")]
        [SwaggerOperation(OperationId = "PatchAIOptions")]
        public async Task<BaseResponse> PatchAiOptions([FromBody] AiOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<AiOptions>().SaveAsync(options =>
            {
                if (model.OllamaEndpoint != null)
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
            await _bakabaseOptionsManager.Get<AiOptions>().SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("soulplus")]
        [SwaggerOperation(OperationId = "GetSoulPlusOptions")]
        public async Task<SingletonResponse<SoulPlusOptions>> GetSoulPlusOptions()
        {
            return new SingletonResponse<SoulPlusOptions>(_bakabaseOptionsManager.Get<SoulPlusOptions>().Value);
        }

        [HttpPatch("soulplus")]
        [SwaggerOperation(OperationId = "PatchSoulPlusOptions")]
        public async Task<BaseResponse> PatchSoulPlusOptions([FromBody] SoulPlusOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<SoulPlusOptions>().SaveAsync(options =>
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
            await _bakabaseOptionsManager.Get<SoulPlusOptions>().SaveAsync(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("bangumi")]
        [SwaggerOperation(OperationId = "GetBangumiOptions")]
        public async Task<SingletonResponse<BangumiOptions>> GetBangumiOptions()
        {
            return new SingletonResponse<BangumiOptions>(_bakabaseOptionsManager.Get<BangumiOptions>().Value);
        }

        [HttpPatch("bangumi")]
        [SwaggerOperation(OperationId = "PatchBangumiOptions")]
        public async Task<BaseResponse> PatchBangumiOptions([FromBody] BangumiOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<BangumiOptions>().SaveAsync(options =>
            {
                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.UserAgent != null)
                {
                    options.UserAgent = model.UserAgent;
                }

                if (model.Referer != null)
                {
                    options.Referer = model.Referer;
                }

                if (model.Headers != null)
                {
                    options.Headers = model.Headers;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("cien")]
        [SwaggerOperation(OperationId = "GetCienOptions")]
        public async Task<SingletonResponse<CienOptions>> GetCienOptions()
        {
            return new SingletonResponse<CienOptions>(_bakabaseOptionsManager.Get<CienOptions>().Value);
        }

        [HttpPatch("cien")]
        [SwaggerOperation(OperationId = "PatchCienOptions")]
        public async Task<BaseResponse> PatchCienOptions([FromBody] CienOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<CienOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("dlsite")]
        [SwaggerOperation(OperationId = "GetDLsiteOptions")]
        public async Task<SingletonResponse<DLsiteOptions>> GetDLsiteOptions()
        {
            return new SingletonResponse<DLsiteOptions>(_bakabaseOptionsManager.Get<DLsiteOptions>().Value);
        }

        [HttpPatch("dlsite")]
        [SwaggerOperation(OperationId = "PatchDLsiteOptions")]
        public async Task<BaseResponse> PatchDLsiteOptions([FromBody] DLsiteOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<DLsiteOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.UserAgent != null)
                {
                    options.UserAgent = model.UserAgent;
                }

                if (model.Referer != null)
                {
                    options.Referer = model.Referer;
                }

                if (model.Headers != null)
                {
                    options.Headers = model.Headers;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("fanbox")]
        [SwaggerOperation(OperationId = "GetFanboxOptions")]
        public async Task<SingletonResponse<FanboxOptions>> GetFanboxOptions()
        {
            return new SingletonResponse<FanboxOptions>(_bakabaseOptionsManager.Get<FanboxOptions>().Value);
        }

        [HttpPatch("fanbox")]
        [SwaggerOperation(OperationId = "PatchFanboxOptions")]
        public async Task<BaseResponse> PatchFanboxOptions([FromBody] FanboxOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<FanboxOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("fantia")]
        [SwaggerOperation(OperationId = "GetFantiaOptions")]
        public async Task<SingletonResponse<FantiaOptions>> GetFantiaOptions()
        {
            return new SingletonResponse<FantiaOptions>(_bakabaseOptionsManager.Get<FantiaOptions>().Value);
        }

        [HttpPatch("fantia")]
        [SwaggerOperation(OperationId = "PatchFantiaOptions")]
        public async Task<BaseResponse> PatchFantiaOptions([FromBody] FantiaOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<FantiaOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("patreon")]
        [SwaggerOperation(OperationId = "GetPatreonOptions")]
        public async Task<SingletonResponse<PatreonOptions>> GetPatreonOptions()
        {
            return new SingletonResponse<PatreonOptions>(_bakabaseOptionsManager.Get<PatreonOptions>().Value);
        }

        [HttpPatch("patreon")]
        [SwaggerOperation(OperationId = "PatchPatreonOptions")]
        public async Task<BaseResponse> PatchPatreonOptions([FromBody] PatreonOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<PatreonOptions>().SaveAsync(options =>
            {
                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.DefaultPath != null)
                {
                    options.DefaultPath = model.DefaultPath;
                }

                if (model.NamingConvention != null)
                {
                    options.NamingConvention = model.NamingConvention;
                }

                if (model.SkipExisting.HasValue)
                {
                    options.SkipExisting = model.SkipExisting.Value;
                }

                if (model.MaxRetries.HasValue)
                {
                    options.MaxRetries = model.MaxRetries.Value;
                }

                if (model.RequestTimeout.HasValue)
                {
                    options.RequestTimeout = model.RequestTimeout.Value;
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("tmdb")]
        [SwaggerOperation(OperationId = "GetTmdbOptions")]
        public async Task<SingletonResponse<TmdbOptions>> GetTmdbOptions()
        {
            return new SingletonResponse<TmdbOptions>(_bakabaseOptionsManager.Get<TmdbOptions>().Value);
        }

        [HttpPatch("tmdb")]
        [SwaggerOperation(OperationId = "PatchTmdbOptions")]
        public async Task<BaseResponse> PatchTmdbOptions([FromBody] TmdbOptionsPatchInputModel model)
        {
            await _bakabaseOptionsManager.Get<TmdbOptions>().SaveAsync(options =>
            {
                if (model.MaxConcurrency.HasValue)
                {
                    options.MaxConcurrency = model.MaxConcurrency.Value;
                }

                if (model.RequestInterval.HasValue)
                {
                    options.RequestInterval = model.RequestInterval.Value;
                }

                if (model.Cookie != null)
                {
                    options.Cookie = model.Cookie;
                }

                if (model.UserAgent != null)
                {
                    options.UserAgent = model.UserAgent;
                }

                if (model.Referer != null)
                {
                    options.Referer = model.Referer;
                }

                if (model.Headers != null)
                {
                    options.Headers = model.Headers;
                }

                if (model.ApiKey != null)
                {
                    options.ApiKey = model.ApiKey;
                }
            });
            return BaseResponseBuilder.Ok;
        }
    }
}