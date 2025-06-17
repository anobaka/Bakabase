using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Alias.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAlias<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IAliasService, AliasService<TDbContext>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.Alias, int>>();

        return services;
    }
}