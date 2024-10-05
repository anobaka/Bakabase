using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection
        AddProperty<TCustomPropertyService, TCustomPropertyValueService, TCategoryCustomPropertyMappingService>(
            this IServiceCollection services)
        where TCustomPropertyValueService : class, ICustomPropertyValueService
        where TCustomPropertyService : class, ICustomPropertyService
        where TCategoryCustomPropertyMappingService : class, ICategoryCustomPropertyMappingService
    {
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<ICustomPropertyService, TCustomPropertyService>();
        services.AddScoped<ICustomPropertyValueService, TCustomPropertyValueService>();
        services.AddScoped<ICategoryCustomPropertyMappingService, TCategoryCustomPropertyMappingService>();
        services.AddTransient<IPropertyLocalizer, PropertyLocalizer>();

        services.AddTransient<BuiltinPropertyMap>();

        return services;
    }
}