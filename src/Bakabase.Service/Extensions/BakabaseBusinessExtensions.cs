using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Ai;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Migration;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Extensions;
using Bakabase.InsideWorld.Business.Components.Legacy;
using Bakabase.InsideWorld.Business.Components.PlayList.Extensions;
using Bakabase.InsideWorld.Business.Components.PlayList.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Components.Search.Index;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bakabase.InsideWorld.Business.Components.ThirdParty;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Migrations.V190;
using Bakabase.Modules.Alias.Extensions;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Presets.Extensions;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.Comparison.Extensions;
using Bakabase.Modules.ThirdParty.Extensions;
using Bakabase.Modules.ThirdParty.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CategoryDbModel = Bakabase.Abstractions.Models.Db.CategoryDbModel;
using MediaLibraryDbModel = Bakabase.Abstractions.Models.Db.MediaLibraryDbModel;
using SpecialText = Bakabase.Abstractions.Models.Db.SpecialText;

namespace Bakabase.Service.Extensions
{
    public static class BakabaseBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            // services.AddScoped<BulkModificationService>();
            // services.AddScoped<BulkModificationDiffService>();
            // services.AddScoped<BulkModificationTempDataService>();
            services.TryAddScoped<ComponentService>();
            services.TryAddScoped<ComponentOptionsService>();
            services.TryAddScoped<CategoryComponentService>();

            // services.AddScoped<SubscriptionService>();
            // services.AddScoped<SubscriptionProgressService>();
            // services.AddScoped<SubscriptionRecordService>();

            services.TryAddSingleton<SelfPlayer>();
            services.TryAddSingleton<PotPlayer>();
            services.RegisterAllRegisteredTypeAs<IPlayer>();

            services.TryAddSingleton<ImagePlayableFileSelector>();
            services.TryAddSingleton<VideoPlayableFileSelector>();
            services.TryAddSingleton<AudioPlayableFileSelector>();
            services.RegisterAllRegisteredTypeAs<IPlayableFileSelector>();

            // services.TryAddScoped<InsideWorldEnhancer>();
            // services.TryAddScoped<DLsiteEnhancer>();
            // //services.TryAddSingleton<DMMEnhancer>();
            // services.TryAddScoped<JavLibraryEnhancer>();
            // services.TryAddScoped<BangumiEnhancer>();
            // services.TryAddScoped<ExHentaiEnhancer>();
            // services.TryAddScoped<NfoEnhancer>();
            //services.TryAddTransient<IOptions<InsideWorldAppOptions>>(t =>
            //{
            //    var options = InsideWorldAppService.GetInsideWorldOptionsAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            //    return Options.Create(options);
            //});
            //services.TryAddTransient<IOptions<ExHentaiEnhancerOptions>>(t =>
            //{
            //    var options = t.GetRequiredService<IOptions<InsideWorldAppOptions>>();
            //    return Options.Create(new ExHentaiEnhancerOptions
            //    {
            //        ExcludedTags = options.Value.ExHentaiExcludedTags,
            //        Cookie = options.Value.ExHentaiCookie
            //    });
            //});

            services.AddScoped<PasswordService>();

            services.TryAddSingleton<IwFsWatcher>();

            // services.AddScoped<BmCategoryProcessor>();
            // services.AddScoped<BmMediaLibraryProcessor>();
            // services.AddScoped<BmTagProcessor>();

            #region Optimized after V190

            services.AddBakabaseComponents();

            services.AddScoped<V190Migrator>();

            services.AddLegacies();

            services.AddAlias<BakabaseDbContext>();
            services.AddProperty<BakabaseDbContext>();
            services.AddEnhancers<BakabaseDbContext>();
            services.AddStandardValue<SpecialTextService>();
            services.AddReservedProperty();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
            services.AddScoped<IResourceLegacySearchService, ResourceLegacySearchService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, SpecialText, int>>();
            services.AddScoped<SpecialTextService>();
            services.AddScoped<ISpecialTextService>(sp => sp.GetRequiredService<SpecialTextService>());
            services.AddScoped<ResourceService<BakabaseDbContext, MediaLibraryDbModel, int>>();
            services.AddScoped<IMediaLibraryService, MediaLibraryService>();
            services.AddScoped<ResourceService<BakabaseDbContext, CategoryDbModel, int>>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, PlayHistoryDbModel, int>>();
            services.AddScoped<IPlayHistoryService, PlayHistoryService>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ThirdPartyContentTrackerDbModel, int>>();
            services.AddScoped<IThirdPartyContentTrackerService, ThirdPartyContentTrackerService>();

            // todo: this can be moved into abstraction layer.
            services.AddSingleton<Abstractions.Components.Cover.ICoverDiscoverer, CoverDiscoverer>();


            services.AddSingleton<IThirdPartyStatisticsNotificationService, ThirdPartyStatisticsNotificationService>();
            services
                .AddThirdParty<BilibiliOptions, BangumiOptions, DLsiteOptions, ExHentaiOptions, PixivOptions,
                    SoulPlusOptions, TmdbOptions>();

            services.AddDownloaders();

            services.AddBulkModification<BakabaseDbContext>();
            services.AddComparison<BakabaseDbContext>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int>>();
            services.AddScoped<IExtensionGroupService, ExtensionGroupService>();

            services.AddSingleton<ISystemPlayer, SelfPlayer>();

            services.AddPostParser<BakabaseDbContext>();
            services.AddSingleton<OllamaApiClientAccessor>();
            services.AddSingleton<TampermonkeyService>();

            services.AddPresets();

            services.AddFileNameModifier();

            services.AddPlayList();

            services.AddBakaTracing();

            // ResourceProfile index service (singleton for in-memory caching)
            services.AddSingleton<ResourceProfileIndexService>();
            services.AddSingleton<IResourceProfileIndexService>(sp => sp.GetRequiredService<ResourceProfileIndexService>());

            // Resource data change event hub (singleton for event pub/sub)
            services.AddSingleton<ResourceDataChangeEventHub>();
            services.AddSingleton<IResourceDataChangeEvent>(sp => sp.GetRequiredService<ResourceDataChangeEventHub>());
            services.AddSingleton<IResourceDataChangeEventPublisher>(sp => sp.GetRequiredService<ResourceDataChangeEventHub>());

            // Resource search index service (singleton for in-memory caching)
            // Note: Index is built via BTask "SearchIndex" task, not IHostedService
            // Note: Depends on IResourceDataChangeEvent for event-driven index updates
            services.AddSingleton<ResourceSearchIndexService>();
            services.AddSingleton<IResourceSearchIndexService>(sp => sp.GetRequiredService<ResourceSearchIndexService>());

            // Temporary: Resource index notification service (notifies frontend about index updates)
            // TODO: Remove when no longer needed
            services.AddHostedService<ResourceIndexNotificationService>();

            // Resource cover cache invalidation service (invalidates cover cache when covers change)
            services.AddHostedService<ResourceCoverCacheInvalidationService>();

            #endregion

            services.AddScoped<MigrationHelper>();

            return services;
        }
    }
}