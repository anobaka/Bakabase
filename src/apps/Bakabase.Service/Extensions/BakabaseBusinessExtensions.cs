using Bakabase.Abstractions.Components.Events;
using Bakabase.Service.Components.Workflow.Resources;
using Bakabase.Service.Components.IdentityLookups;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Extensions;
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
using Bakabase.Modules.Player.Extensions;
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
using Bakabase.Modules.Text.Components;
using Bakabase.Modules.Text.Extensions;
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

namespace Bakabase.Service.Extensions
{
    public static class BakabaseBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            services.AddScoped<PasswordService>();

            services.TryAddSingleton<IwFsWatcher>();
            services.AddSingleton<Bakabase.Service.Services.FileSystemEntryGroupingService>();
            services.AddSingleton<Bakabase.Service.Services.ProxyTestService>();

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
            services.AddText<BakabaseDbContext>();
            services.AddStandardValue<TextOps>();
            services.AddReservedProperty();

            services.AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
            services.AddScoped<IResourceLegacySearchService, ResourceLegacySearchService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddSingleton<Bakabase.Abstractions.Components.ResourceMove.ResourceMoveGuard>();
            services
                .AddScoped<IResourceMoveService,
                    Bakabase.InsideWorld.Business.Components.ResourceMove.ResourceMoveService>();
            services.AddSingleton<IPropertyValueScopeResolver, PropertyValueScopeResolver>();
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
            // The platforms the user already holds things on. Each wraps the service that
            // already knows how to talk to it rather than reimplementing any of it, and each is
            // keyed so asking for one does not build the other two.
            services.AddKeyedScoped<Bakabase.Abstractions.Components.Platform.IPlatformConnector,
                Components.Acquisition.Connectors.DLsiteConnector>(Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.DLsite);
            services.AddKeyedScoped<Bakabase.Abstractions.Components.Platform.IPlatformConnector,
                Components.Acquisition.Connectors.SteamConnector>(Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Steam);
            services.AddKeyedScoped<Bakabase.Abstractions.Components.Platform.IPlatformConnector,
                Components.Acquisition.Connectors.ExHentaiConnector>(Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.ExHentai);
            services.AddScoped<Bakabase.Abstractions.Components.Platform.IPlatformConnectorRegistry,
                Components.Acquisition.Connectors.PlatformConnectorRegistry>();

            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.SoulPlus.SoulPlusSearchProvider>();
            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.DLsite.DLsiteCircleProvider>();
            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.Bangumi.BangumiSubjectRelationsProvider>();
            // What the user already holds, as sources. They take no configuration: the account
            // lives in each platform's own settings.
            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.Platform.DLsitePurchasesProvider>();
            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.Platform.SteamOwnedGamesProvider>();
            services.AddSingleton<ISubscriptionProvider,
                Components.Subscription.Providers.Platform.ExHentaiFavoritesProvider>();
            services.AddWorkflow<BakabaseDbContext>();
            services.AddAcquisition<BakabaseDbContext>();
            services.AddCollections<BakabaseDbContext>();
            // The app-layer subclass supplies the two things the module deliberately does not know:
            // how to run a resource search, and what is currently being acquired.
            services.AddScoped<ICollectionService, Components.Collections.BakabaseCollectionService>();
            services.AddScoped<Bakabase.Abstractions.Services.ICollectionNameProvider,
                Components.Collections.CollectionNameProvider>();
            // Acquisition steps. Each registers its workflow activity alongside itself, so a step
            // added here becomes something a recipe can name.
            services.AddAcquisitionStep<Bakabase.Modules.Acquisition.Components.Steps.SelectLinkStep>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.ResolveSharedContentStep>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.FetchHttpStep>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.WaitForInboxStep>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.FetchFromPlatformStep>();
            services.AddScoped<Components.Acquisition.SharedListImportService>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.UnpackStep>();
            services.AddAcquisitionStep<Bakabase.Modules.Acquisition.Components.Steps.PickLocalDirectoryStep>();
            services.AddAcquisitionStep<Bakabase.Modules.Acquisition.Components.Steps.PlaceStep>();
            services.AddAcquisitionStep<Components.Acquisition.Steps.MaterializeStep>();
            services.AddHttpClient(nameof(Components.Acquisition.Steps.FetchHttpStep));
            services.AddScoped<Components.Acquisition.AcquisitionInboxService>();
            services.AddScoped<Components.Acquisition.AcquisitionSetupService>();
            services.AddHostedService<Components.Acquisition.AcquisitionInboxWatcher>();

            // "I am missing this" — creating resources for things the user does not have yet.
            services.AddScoped<IPlaceholderResourceService, PlaceholderResourceService>();
            services.AddScoped<IExternalIdentityLookup, DLsiteIdentityLookup>();
            services.AddScoped<IExternalIdentityLookup, SteamIdentityLookup>();
            services.AddScoped<IExternalIdentityLookup, BangumiIdentityLookup>();
            services.AddScoped<IExternalIdentityLookup, ExHentaiIdentityLookup>();
            services.AddScoped<ISharedUrlTitleResolver, SharedUrlTitleResolver>();

            // "…or is this the one I already have?" — the pairs a person still has to decide.
            services
                .AddScoped<FullMemoryCacheResourceService<BakabaseDbContext, ResourceMatchSuggestionDbModel, int>>();
            services.AddScoped<IResourceMatchSuggestionService, ResourceMatchSuggestionService>();

            services.AddSingleton<IWorkflowTrigger, SubscriptionUpdatedTrigger>();
            services.AddSingleton<IWorkflowTrigger, DownloaderCompletedTrigger>();
            services.AddSingleton<IWorkflowTrigger, ResourceMaterializedTrigger>();
            // Item type descriptors — give the editor type info to render and the AI
            // transform shape info for prompts.
            services.AddSingleton<IWorkflowItemTypeDescriptor, SubscriptionAnyItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, PixivIllustItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, ExHentaiGalleryItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, SearchQueryItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, DownloaderCompletedItemTypeDescriptor>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, ResourceItemTypeDescriptor>();
            // Activities.
            services.AddSingleton<IWorkflowActivity, SubscriptionItemTitleContainsActivity>();
            services.AddSingleton<IWorkflowActivity, AiTransformActivity>();
            services.AddSingleton<IWorkflowActivity, ExHentaiQueryToGalleryActivity>();
            services.AddSingleton<IWorkflowActivity, ExHentaiEnqueueDownloadActivity>();
            services.AddSingleton<IWorkflowActivity, DownloaderEnqueueActivity>();
            services.AddSingleton<IWorkflowActivity, CreateNotificationActivity>();
            services.AddSingleton<IWorkflowActivity, ResourceSetPropertyValueActivity>();
            services.AddSingleton<IWorkflowActivity, EnhancerEnhanceActivity>();
            services.AddSingleton<IWorkflowActivity, PathMarkEnqueueSyncActivity>();
            // File-cleaning vertical (fs domain).
            services.AddSingleton<IWorkflowTrigger, FsManualScanTrigger>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, Components.Workflow.Fs.FsEntryItemTypeDescriptor>();
            services.AddSingleton<IWorkflowActivity, FsFileNameOpActivity>();
            services.AddSingleton<IWorkflowActivity, FsSaveNameActivity>();
            services.AddScoped<IFileRenameEntryService, FileRenameEntryService>();
            // Text family (E3): contract-accepting transforms over any ITextWorkpiece item.
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.Text.TextRemoveWrappedActivity>();
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.Text.TextRemoveTextsActivity>();
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.Text.TextTrimActivity>();
            // Variables + expansion (E4 + E2): capture/template over the bag, fs descent.
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.Text.TextCaptureActivity>();
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.Text.TextTemplateActivity>();
            services.AddSingleton<IWorkflowActivity, Components.Workflow.Activities.Transforms.FsExpandChildrenActivity>();
            // Automation (E6): scheduled scan + directory watch.
            services.AddSingleton<IWorkflowTrigger, FsScheduledScanTrigger>();
            services.AddSingleton<IWorkflowTrigger, FsWatchTrigger>();
            services.AddHostedService<Components.Workflow.WorkflowFsWatchService>();

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

            services.AddPlayerModule();
            services.AddScoped<Bakabase.Modules.Player.Abstractions.Components.IBatchPlayPlaylistSource,
                Bakabase.Service.Services.PlaylistBatchPlaySource>();
            // Temp playlists must live under AppData (appdata-paths rule), but the
            // module cannot reference Infrastructures — bridge it here. AppService
            // is resolved lazily so test hosts without it still work.
            services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<
                Bakabase.Modules.Player.Abstractions.Models.Domain.PlayerModuleOptions>>(sp =>
                new Microsoft.Extensions.Options.ConfigureOptions<
                    Bakabase.Modules.Player.Abstractions.Models.Domain.PlayerModuleOptions>(o =>
                {
                    var appService = sp.GetService<Bakabase.Infrastructures.Components.App.AppService>();
                    if (appService != null)
                    {
                        o.TempPlaylistDirectory = appService.RequestAppDataDirectory("temp", "playlists");
                    }
                }));

            services.AddBakaTracing();

            // ResourceProfile index service (singleton for in-memory caching)
            services.AddSingleton<ResourceProfileIndexService>();
            services.AddSingleton<IResourceProfileIndexService>(sp => sp.GetRequiredService<ResourceProfileIndexService>());

            // Resource data change event hub (singleton for event pub/sub)
            services.AddSingleton<ResourceDataChangeEventHub>();
            services.AddSingleton<IResourceDataChangeEvent>(sp => sp.GetRequiredService<ResourceDataChangeEventHub>());
            // A collection's rule can only give a different answer because a resource changed, so
            // that is exactly when the cached answer stops being usable.
            services.AddSingleton<Components.Collections.CollectionRuleCache>();
            services.AddSingleton<Components.Collections.CollectionRuleCacheInvalidator>();
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

            // Bridges resource-data-change events to the web UI over SignalR so the
            // resource list can reload affected cards (e.g. after a cache refresh).
            services.AddHostedService<ResourceChangePushService>();

            #endregion
            
            return services;
        }
    }
}