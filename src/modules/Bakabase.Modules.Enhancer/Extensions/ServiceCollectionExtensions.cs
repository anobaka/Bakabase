using System.Reflection;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnhancers<TEnhancementService, TEnhancerService,
        TCategoryEnhancerOptionsService>(this IServiceCollection services)
        where TEnhancerService : class, IEnhancerService
        where TEnhancementService : class, IEnhancementService
        where TCategoryEnhancerOptionsService : class, ICategoryEnhancerOptionsService
    {
        services.TryAddScoped<IEnhancerService, TEnhancerService>();
        services.TryAddScoped<IEnhancementService, TEnhancementService>();
        services.TryAddScoped<ICategoryEnhancerOptionsService, TCategoryEnhancerOptionsService>();

        var enhancerTypes = Assembly.GetExecutingAssembly().GetTypes().Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IEnhancer>.Type) &&
                s.GetCustomAttribute<EnhancerAttribute>() != null)
            .ToList();
        foreach (var et in enhancerTypes)
        {
            services.TryAddScoped(SpecificTypeUtils<IEnhancer>.Type, et);
        }

        return services;
    }
}