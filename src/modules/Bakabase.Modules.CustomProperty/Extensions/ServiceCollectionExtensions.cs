using System.Reflection;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.CustomProperty.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection
        AddCustomProperty<TCustomPropertyService, TCustomPropertyValueService, TCategoryCustomPropertyMappingService,
            TCustomPropertyLocalizer>(
            this IServiceCollection services)
        where TCustomPropertyValueService : class, ICustomPropertyValueService
        where TCustomPropertyService : class, ICustomPropertyService
        where TCustomPropertyLocalizer : ICustomPropertyLocalizer
        where TCategoryCustomPropertyMappingService : class, ICategoryCustomPropertyMappingService
    {
        services.AddScoped<ICustomPropertyService, TCustomPropertyService>();
        services.AddScoped<ICustomPropertyValueService, TCustomPropertyValueService>();
        services.AddScoped<ICategoryCustomPropertyMappingService, TCategoryCustomPropertyMappingService>();

        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                        t.IsAssignableTo(SpecificTypeUtils<ICustomPropertyDescriptor>.Type))
            .ToList();
        foreach (var t in types)
        {
            services.AddSingleton(SpecificTypeUtils<ICustomPropertyDescriptor>.Type, t);
        }

        services.AddSingleton<ICustomPropertyDescriptors, CustomPropertyDescriptors>();

        services.AddTransient<ICustomPropertyLocalizer>(sp => sp.GetRequiredService<TCustomPropertyLocalizer>());

        return services;
    }
}