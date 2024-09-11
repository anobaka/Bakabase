using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.ReservedProperty;

public static class ReservedPropertyExtensions
{
    public static IServiceCollection AddReservedProperty(this IServiceCollection services)
    {
        services
            .AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.ReservedPropertyValue,
                int>>();
        services.AddScoped<IReservedPropertyValueService, ReservedPropertyValueService>();
        return services;
    }
}