using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Collection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCollections<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, CollectionDbModel, int>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, CollectionResourceMappingDbModel, int>>();
        services.AddScoped<ICollectionResourceMappingService, CollectionResourceMappingService<TDbContext>>();

        return services;
    }
}
