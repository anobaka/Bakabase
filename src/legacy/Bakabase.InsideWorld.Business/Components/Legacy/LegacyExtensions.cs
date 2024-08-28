using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Legacy;

public static class LegacyExtensions
{
    public static IServiceCollection AddLegacies(this IServiceCollection services)
    {
        services.AddScoped<LegacyAliasService>();
        services.AddScoped<LegacyOriginalResourceMappingService>();
        services.AddScoped<LegacyPublisherResourceMappingService>();
        services.AddScoped<LegacyResourceTagMappingService>();
        services.AddScoped<LegacyPublisherTagMappingService>();
        services.AddScoped<LegacyFavoritesResourceMappingService>();
        services.AddScoped<LegacyPublisherService>();
        services.AddScoped<LegacyVolumeService>();
        services.AddScoped<LegacyResourceService>();
        services.AddScoped<LegacySeriesService>();
        services.AddScoped<LegacyOriginalService>();
        services.AddScoped<LegacyTagService>();
        services.AddScoped<LegacyTagGroupService>();
        services.AddScoped<LegacyResourcePropertyService>();
        services.AddScoped<LegacyFavoritesService>();

        services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, TagGroup, int>>();
        services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Publisher, int>>();
        services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Series, int>>();
        services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Original, int>>();
        services.AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Tag, int>>();

        return services;
    }
}