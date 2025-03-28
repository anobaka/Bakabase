using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Enhancer;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Legacy;
using Bakabase.InsideWorld.Business.Components.Modules.Alias;
using Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Migrations.V190;
using Bakabase.Modules.Alias.Extensions;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.ThirdParty.Extensions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Service.Extensions
{
    public static class InsideWorldBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            services.AddScoped<DownloadTaskService>();
            services.AddScoped<PlaylistService>();
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

            services.RegisterAllRegisteredTypeAs<IEnhancer>();

            services.RegisterAllRegisteredTypeAs<IDownloader>();

            services.AddSingleton<ResourceTaskManager>();

            services.TryAddSingleton<IwFsWatcher>();

            // services.AddScoped<BmCategoryProcessor>();
            // services.AddScoped<BmMediaLibraryProcessor>();
            // services.AddScoped<BmTagProcessor>();

            #region Optimized after V190

            services.AddBakabaseComponents();

            services.AddScoped<V190Migrator>();

            services.AddLegacies();

            services.AddAlias<InsideWorldDbContext, AliasService>();
            services.AddProperty<CustomPropertyService, CustomPropertyValueService, CategoryCustomPropertyMappingService>();
            services
                .AddEnhancers<InsideWorldDbContext, EnhancementService, EnhancerService, CategoryEnhancerOptionsService,
                    EnhancementRecordService>();
            services.AddStandardValue<SpecialTextService>();
            services.AddReservedProperty();

            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, ResourceDbModel, int>>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, SpecialText, int>>();
            services.AddScoped<SpecialTextService>();
            services.AddScoped<ISpecialTextService>(sp => sp.GetRequiredService<SpecialTextService>());
            services.AddScoped<ResourceService<InsideWorldDbContext, MediaLibrary, int>>();
            services.AddScoped<IMediaLibraryService, MediaLibraryService>();
            services.AddScoped<ResourceService<InsideWorldDbContext, Category, int>>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, ResourceCacheDbModel, int>>();

            services.AddScoped<IThirdPartyService, ThirdPartyService>();

            // todo: this can be moved into abstraction layer.
            services.AddSingleton<Abstractions.Components.Cover.ICoverDiscoverer, CoverDiscoverer>();

            services.AddThirdParty();

            services.AddBulkModification<InsideWorldDbContext>();

            #endregion

            return services;
        }
    }
}