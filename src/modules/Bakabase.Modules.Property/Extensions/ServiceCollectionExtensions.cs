using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProperty<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<ICustomPropertyService, CustomPropertyService<TDbContext>>();
        services.AddScoped<ICustomPropertyValueService, CustomPropertyValueService<TDbContext>>();
        services.AddScoped<ICategoryCustomPropertyMappingService, CategoryCustomPropertyMappingService<TDbContext>>();
        services.AddTransient<IPropertyLocalizer, PropertyLocalizer>();

        services.AddTransient<BuiltinPropertyMap>();

        return services;
    }
}