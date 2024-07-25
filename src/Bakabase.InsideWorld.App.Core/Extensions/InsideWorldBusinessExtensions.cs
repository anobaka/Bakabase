using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.BuiltinProperty;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Enhancer;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Legacy;
using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Business.Components.Modules.Alias;
using Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Services;
using Bakabase.InsideWorld.Business.Models.Dto;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.Alias.Extensions;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using InsideWorld.Migrations.V190;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using InternalOptions = Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.InsideWorld.App.Core.Extensions
{
    public static class InsideWorldBusinessExtensions
    {
        public static IServiceCollection AddInsideWorldBusinesses(this IServiceCollection services)
        {
            services.AddScoped<DownloadTaskService>();
            services.AddScoped<PlaylistService>();
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

            services.AddSingleton<BackgroundTaskHelper>();
            services.AddSingleton<ResourceTaskManager>();

            services.TryAddSingleton<IwFsWatcher>();

            services.TryAddSingleton<TempFileManager>();

            services.AddScoped<BmCategoryProcessor>();
            services.AddScoped<BmMediaLibraryProcessor>();
            // services.AddScoped<BmTagProcessor>();

            services.AddSingleton<InternalOptionsDto>(t =>
            {
                var options = new InternalOptionsDto();
                var customPropertyDescriptors = t.GetRequiredService<IEnumerable<ICustomPropertyDescriptor>>();
                options.Resource.CustomPropertyValueSearchOperationsMap =
                    customPropertyDescriptors.ToDictionary(d => (int) d.Type, d => d.SearchOperations);
                return options;
            });

            services.AddSingleton<IResourceSearchContextProcessor, DefaultResourceSearchContextProcessor>();

            #region Optimized after V190

            services.AddScoped<V190Migrator>();

            services.AddLegacies();

            services.AddAlias<InsideWorldDbContext, AliasService>();
            services.AddCustomProperty<CustomPropertyService, CustomPropertyValueService,
                CategoryCustomPropertyMappingService, InsideWorldLocalizer>();
            services
                .AddEnhancers<InsideWorldDbContext, EnhancementService, EnhancerService, CategoryEnhancerOptionsService,
                    InsideWorldLocalizer>();
            services.AddStandardValue<SpecialTextService, InsideWorldLocalizer>();
            services.AddBuiltinProperty();

            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int>>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, SpecialText, int>>();
            services.AddScoped<SpecialTextService>();
            services.AddScoped<ISpecialTextService>(sp => sp.GetRequiredService<SpecialTextService>());
            services.AddScoped<ResourceService<InsideWorldDbContext, MediaLibrary, int>>();
            services.AddScoped<IMediaLibraryService, MediaLibraryService>();
            services.AddScoped<ResourceService<InsideWorldDbContext, Category, int>>();
            services.AddScoped<ICategoryService, CategoryService>();

            services.AddScoped<IThirdPartyService, ThirdPartyService>();

            #endregion

            return services;
        }

        public static IServiceCollection AddBakabaseHttpClient<THttpClientHandler>(this IServiceCollection services,
            string name) where THttpClientHandler : HttpClientHandler
        {
            services.TryAddSingleton<THttpClientHandler>();
            services.AddHttpClient(name,
                    t => { t.DefaultRequestHeaders.Add("User-Agent", InternalOptions.DefaultHttpUserAgent); })
                // todo: let http client factory handle its lifetime automatically after changing queue mechanism of inside world handler 
                .SetHandlerLifetime(TimeSpan.FromDays(30))
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<THttpClientHandler>());

            return services;
        }
    }
}