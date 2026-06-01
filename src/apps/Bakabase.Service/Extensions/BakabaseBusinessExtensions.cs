using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Extensions;
using Bakabase.InsideWorld.Business.Components.PlayList.Extensions;
using Bakabase.InsideWorld.Business.Components.PlayList.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Components.Search.Index;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bakabase.InsideWorld.Business.Components.ThirdParty;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.AI.Extensions;
using Bakabase.Modules.Alias.Extensions;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.HealthScore.Extensions;
using Bakabase.Modules.Notification.Abstractions.Components;
using Bakabase.Modules.Notification.Extensions;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Extensions;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Service.Components.Subscription.Providers.ExHentai;
using Bakabase.Service.Components.Subscription.Providers.Pixiv;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.Workflow.Activities.Actions;
using Bakabase.Service.Components.Workflow.Activities.Filters;
using Bakabase.Service.Components.Workflow.Activities.Transforms;
using Bakabase.Service.Components.Workflow.Triggers;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Presets.Extensions;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.Comparison.Extensions;
using Bakabase.Modules.DataCard.Extensions;
using Bakabase.InsideWorld.Business.Components.Resolvers;
using Bakabase.Modules.ThirdParty.Extensions;
using Bakabase.Modules.ThirdParty.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpecialText = Bakabase.Abstractions.Models.Db.SpecialText;

namespace Bakabase.Service.Extensions
{
    public static class BakabaseBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            services.AddScoped<PasswordService>();

            services.TryAddSingleton<IwFsWatcher>();
            services.AddSingleton<Bakabase.Service.Services.FileSystemEntryGroupingService>();

            #region Optimized after V190

            services.AddBakabaseComponents();
            
            services.AddAI<BakabaseDbContext>();
            services.AddLlmTools(typeof(Bakabase.Service.Components.AI.ResourceTools).Assembly);

            // Bridge AiOptions → AiModuleOptions for the AI module (avoids circular dependency)
            services.Configure<Bakabase.Modules.AI.Models.Domain.AiModuleOptions>(o =>
            {
                // Options will be populated via IOptionsMonitor change tracking below
            });
            services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Bakabase.Modules.AI.Models.Domain.AiModuleOptions>>(sp =>
            {
                var aiOpts = sp.GetRequiredService<Bootstrap.Components.Configuration.Abstractions.IBOptions<AiOptions>>();
                return new Microsoft.Extensions.Options.ConfigureOptions<Bakabase.Modules.AI.Models.Domain.AiModuleOptions>(o =>
                {
                    var src = aiOpts.Value;
                    o.DefaultProviderConfigId = src.DefaultProviderConfigId;
                    o.DefaultModelId = src.DefaultModelId;
                    o.Quota = src.Quota;
                    o.EnableCache = src.EnableCache;
                    o.DefaultCacheTtlDays = src.DefaultCacheTtlDays;
                    o.AuditLogRequestContent = src.AuditLogRequestContent;
                });
            });
            services.AddAlias<BakabaseDbContext>();
            services.AddProperty<BakabaseDbContext>();
            services.AddEnhancers<BakabaseDbContext>();
            services.AddStandardValue<SpecialTextService>();
            services.AddReservedProperty();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
            services.AddScoped<IResourceLegacySearchService, ResourceLegacySearchService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddSingleton<IPropertyValueScopeResolver, PropertyValueScopeResolver>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, SpecialText, int>>();
            services.AddScoped<SpecialTextService>();
            services.AddScoped<ISpecialTextService>(sp => sp.GetRequiredService<SpecialTextService>());
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, PlayHistoryDbModel, int>>();
            services.AddScoped<IPlayHistoryService, PlayHistoryService>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, PropertyValueScopePreferenceDbModel, int>>();
            services.AddScoped<IPropertyValueScopePreferenceService, PropertyValueScopePreferenceService>();
            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ThirdPartyContentTrackerDbModel, int>>();
            services.AddScoped<IThirdPartyContentTrackerService, ThirdPartyContentTrackerService>();

            // todo: this can be moved into abstraction layer.
            services.AddSingleton<Abstractions.Components.Cover.ICoverDiscoverer, CoverDiscoverer>();


            services.AddSingleton<IThirdPartyStatisticsNotificationService, ThirdPartyStatisticsNotificationService>();
            services
                .AddThirdParty<BilibiliOptions, BangumiOptions, DLsiteOptions, ExHentaiOptions, PixivOptions,
                    SoulPlusOptions, TmdbOptions>();
            // Replace the fallback resolver registered by AddThirdParty with one backed by
            // AvSourceOptions so user configuration in third-party-av-sources.json takes effect.
            services.Replace(ServiceDescriptor.Singleton<Bakabase.Modules.ThirdParty.ThirdParties.Av.IAvSourceOptionsProvider,
                Bakabase.InsideWorld.Business.Components.Configurations.AvSourceOptionsProvider>());

            services.AddDownloaders();
            services.AddResourceResolvers();
            services.AddCoverProviders();
            services.AddPlayableItemProviders();
            services.AddMetadataProviders();
            services.AddScoped<ICoverProviderService, Bakabase.InsideWorld.Business.Components.Providers.Cover.CoverProviderService>();
            services.AddScoped<IPlayableItemProviderService, Bakabase.InsideWorld.Business.Components.Providers.PlayableItem.PlayableItemProviderService>();

            services.AddBulkModification<BakabaseDbContext>();
            services.AddComparison<BakabaseDbContext>();
            services.AddDataCard<BakabaseDbContext>();
            services.AddHealthScore<BakabaseDbContext>();
            services.AddNotification<BakabaseDbContext>();
            services.AddSingleton<INotificationPusher, NotificationPusher>();
            services.AddSubscription<BakabaseDbContext>();
            services.AddSingleton<ISubscriptionProvider, ExHentaiSearchProvider>();
            services.AddSingleton<ISubscriptionProvider, ExHentaiGalleryProvider>();
            services.AddSingleton<ISubscriptionProvider, PixivFollowLatestProvider>();
            services.AddWorkflow<BakabaseDbContext>();
            services.AddSingleton<IWorkflowTrigger, SubscriptionUpdatedTrigger>();
            services.AddSingleton<IWorkflowTrigger, DownloaderCompletedTrigger>();
            // Item type descriptors — give the editor type info to render and the AI
            // transform shape info for prompts.
            services.AddSingleton<IWorkflowItemTypeDescriptor, SubscriptionAnyItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, PixivIllustItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, ExHentaiGalleryItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, SearchQueryItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, DownloaderCompletedItemTypeDescriptor>();
            // Activities.
            services.AddSingleton<IWorkflowActivity, SubscriptionItemTitleContainsActivity>();
            services.AddSingleton<IWorkflowActivity, AiTransformActivity>();
            services.AddSingleton<IWorkflowActivity, ExHentaiQueryToGalleryActivity>();
            services.AddSingleton<IWorkflowActivity, ExHentaiEnqueueDownloadActivity>();
            services.AddSingleton<IWorkflowActivity, CreateNotificationActivity>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int>>();
            services.AddScoped<IExtensionGroupService, ExtensionGroupService>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, SteamAppDbModel, int>>();
            services.AddScoped<ISteamAppService, SteamAppService>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int>>();
            services.AddScoped<DLsiteArchiveExtractor>();
            services.AddScoped<IDLsiteWorkService, DLsiteWorkService>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int>>();
            services.AddScoped<IExHentaiGalleryService, ExHentaiGalleryService>();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, SourceMetadataMappingDbModel, int>>();
            services.AddScoped<ISourceMetadataSyncService, SourceMetadataSyncService<BakabaseDbContext>>();

            services.AddSingleton<ISystemPlayer, SelfPlayer>();
            services.AddSingleton<Bakabase.Abstractions.Components.ISystemPlayer>(sp =>
                sp.GetRequiredService<ISystemPlayer>());

            services.AddPostParser<BakabaseDbContext>();
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
            
            return services;
        }
    }
}