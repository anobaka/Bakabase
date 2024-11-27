using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.BulkModification.Models.Db;
using Bakabase.Modules.BulkModification.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.BulkModification.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBulkModification<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IBulkModificationDbContext
    {
        services.AddScoped<ResourceService<TDbContext, BulkModificationDiffDbModel, int>>();
        services.AddScoped<IBulkModificationService, BulkModificationService<TDbContext>>();

        services.AddTransient<IBulkModificationLocalizer, BulkModificationLocalizer>();
        return services;
    }
}