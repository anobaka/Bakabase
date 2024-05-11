using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnhancers<TEnhancementService, TEnhancerService,
        TCategoryEnhancerOptionsService, TLocalizer>(this IServiceCollection services)
        where TEnhancerService : class, IEnhancerService
        where TEnhancementService : class, IEnhancementService
        where TCategoryEnhancerOptionsService : class, ICategoryEnhancerOptionsService
        where TLocalizer : class, IEnhancerLocalizer
    {
        services.TryAddScoped<IEnhancerService, TEnhancerService>();
        services.TryAddScoped<IEnhancementService, TEnhancementService>();
        services.TryAddScoped<ICategoryEnhancerOptionsService, TCategoryEnhancerOptionsService>();
        services.AddTransient<IEnhancerLocalizer, TLocalizer>(x => x.GetRequiredService<TLocalizer>());


        var currentAssemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        var enhancerTypes = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IEnhancer>.Type) &&
                s is {IsPublic: true, IsAbstract: false} &&
                s.GetCustomAttribute<EnhancerAttribute>() != null)
            .ToList();
        foreach (var et in enhancerTypes)
        {
            services.TryAddScoped(SpecificTypeUtils<IEnhancer>.Type, et);
        }

        services.AddSingleton(sp =>
        {
            var localizer = sp.GetRequiredService<IEnhancerLocalizer>();
            var enhancerDescriptors = SpecificEnumUtils<EnhancerId>.Values.Select(enhancerId =>
            {
                var attr = enhancerId.GetAttribute<EnhancerAttribute>();
                var targets = Enum.GetValues(attr.TargetEnumType).Cast<Enum>();
                var targetDescriptors = targets.Select(target =>
                {
                    var targetAttr = target.GetAttribute<EnhancerTargetAttribute>();
                    return new EnhancerTargetDescriptor(target,
                        localizer.Enhancer_TargetName(enhancerId, target),
                        targetAttr.ValueType,
                        localizer.Enhancer_TargetDescription(enhancerId, target),
                        targetAttr.Options?.Cast<int>().ToArray());
                }).ToArray();

                return new EnhancerDescriptor((int) enhancerId, localizer.Enhancer_Name(enhancerId),
                    localizer.Enhancer_Description(enhancerId), targetDescriptors, attr.CustomPropertyValueLayer);
            });
            return enhancerDescriptors;
        });

        return services;
    }
}