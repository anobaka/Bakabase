using System;
using System.Linq;
using System.Net.Http;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.BmVolumeProcessor;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Network;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Configs.Fixed;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.App.Core.Extensions
{
    public static class InsideWorldBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            services.AddScoped<AliasService>();
            services.AddScoped<AliasGroupService>();
            services.AddScoped<OriginalResourceMappingService>();
            services.AddScoped<PublisherResourceMappingService>();
            services.AddScoped<ResourceTagMappingService>();
            services.AddScoped<PublisherTagMappingService>();
            services.AddScoped<FavoritesResourceMappingService>();
            services.AddScoped<ResourceService>();
            services.AddScoped<PublisherService>();
            services.AddScoped<VolumeService>();
            services.AddScoped<PasswordService>();
            services.AddScoped<SeriesService>();
            services.AddScoped<OriginalService>();
            services.AddScoped<ResourceCategoryService>();
            services.AddScoped<TagService>();
            services.AddScoped<TagGroupService>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int>>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, TagGroup, int>>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Publisher, int>>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Series, int>>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Original, int>>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Tag, int>>();
            services.AddScoped<SpecialTextService>();
            services.AddScoped<MediaLibraryService>();
            services.AddScoped<CustomResourcePropertyService>();
            services.AddScoped<EnhancementRecordService>();
            services.AddScoped<FavoritesService>();
            services.AddScoped<DownloadTaskService>();
            services.AddScoped<PlaylistService>();
            services.AddScoped<ThirdPartyService>();
            services.AddScoped<BulkModificationService>();
            services.AddScoped<BulkModificationDiffService>();
            services.AddScoped<BulkModificationTempDataService>();
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

            services.TryAddScoped<InsideWorldEnhancer>();
            services.TryAddScoped<DLsiteEnhancer>();
            //services.TryAddSingleton<DMMEnhancer>();
            services.TryAddScoped<JavLibraryEnhancer>();
            services.TryAddScoped<BangumiEnhancer>();
            services.TryAddScoped<ExHentaiEnhancer>();
            services.TryAddScoped<NfoEnhancer>();
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

            services.RegisterAllRegisteredTypeAs<IEnhancer>();

            services.RegisterAllRegisteredTypeAs<IDownloader>();

            services.AddSingleton<ToolService>();

            services.AddSingleton<BackgroundTaskHelper>();
            services.AddSingleton<ResourceTaskManager>();

            services.TryAddSingleton<IwFsWatcher>();

            services.TryAddSingleton<TempFileManager>();

            services.AddScoped<BmVolumeProcessor>();
            services.AddScoped<BmCategoryProcessor>();
            services.AddScoped<BmCustomPropertiesProcessor>();
            services.AddScoped<BmIntroductionProcessor>();
            services.AddScoped<BmLanguageProcessor>();
            services.AddScoped<BmMediaLibraryProcessor>();
            services.AddScoped<BmNameProcessor>();
            services.AddScoped<BmOriginalProcessor>();
            services.AddScoped<BmPublishersProcessor>();
            services.AddScoped<BmRateProcessor>();
            services.AddScoped<BmReleaseDtProcessor>();
            services.AddScoped<BmSeriesProcessor>();
            services.AddScoped<BmTagProcessor>();


            services.AddScoped<CustomPropertyService>();
            services.AddScoped<CustomPropertyValueService>();
            services.AddScoped<CategoryCustomPropertyMappingService>();

            services.AddSingleton<ReservedOptions>();

            return services;
        }

        public static IServiceCollection AddInsideWorldHttpClient<THttpClientHandler>(this IServiceCollection services,
            string name) where THttpClientHandler : HttpClientHandler
        {
            services.TryAddSingleton<THttpClientHandler>();
            services.AddHttpClient(name,
                    t => { t.DefaultRequestHeaders.Add("User-Agent", BusinessConstants.DefaultHttpUserAgent); })
                // todo: let http client factory handle its lifetime automatically after changing queue mechanism of inside world handler 
                .SetHandlerLifetime(TimeSpan.FromDays(30))
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<THttpClientHandler>());

            return services;
        }
    }
}