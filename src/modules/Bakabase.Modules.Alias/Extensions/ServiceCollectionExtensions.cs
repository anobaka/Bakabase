using Bakabase.Modules.Alias.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Alias.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAlias<TDbContext, TAliasService>(this IServiceCollection services)
        where TAliasService : class, IAliasService where TDbContext : DbContext
    {
        services.AddScoped<IAliasService, TAliasService>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.Alias, int>>();

        return services;
    }
}