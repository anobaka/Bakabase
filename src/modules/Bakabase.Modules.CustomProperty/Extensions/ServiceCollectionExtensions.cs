using System.Reflection;
using Bakabase.Modules.CustomProperty.Abstractions;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.CustomProperty.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection
        AddCustomProperty<TCustomPropertyService, TCustomPropertyValueService>(this IServiceCollection services)
        where TCustomPropertyValueService : class, ICustomPropertyValueService
        where TCustomPropertyService : class, ICustomPropertyService
    {
        services.AddScoped<ICustomPropertyService, TCustomPropertyService>();
        services.AddScoped<ICustomPropertyValueService, TCustomPropertyValueService>();

        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                        t.IsAssignableTo(SpecificTypeUtils<ICustomPropertyDescriptor>.Type))
            .ToList();
        foreach (var t in types)
        {
            services.AddSingleton(SpecificTypeUtils<ICustomPropertyDescriptor>.Type, t);
        }

        return services;
    }
}