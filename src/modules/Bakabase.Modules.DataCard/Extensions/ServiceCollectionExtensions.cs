using Bakabase.Modules.DataCard.Abstractions.Models.Db;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.DataCard.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataCard<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IDataCardTypeService, DataCardTypeService<TDbContext>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, DataCardTypeDbModel, int>>();

        services.AddScoped<IDataCardService, DataCardService<TDbContext>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, DataCardDbModel, int>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, DataCardPropertyValueDbModel, int>>();

        return services;
    }
}
