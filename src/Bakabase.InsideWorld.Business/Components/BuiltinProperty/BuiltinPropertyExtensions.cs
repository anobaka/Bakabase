using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.BuiltinProperty;

public static class BuiltinPropertyExtensions
{
    public static IServiceCollection AddBuiltinProperty(this IServiceCollection services)
    {
        services
            .AddScoped<FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.BuiltinPropertyValue,
                int>>();
        services.AddScoped<IBuiltinPropertyValueService, BuiltinPropertyValueService>();
        return services;
    }
}