using System.Reflection;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnhancers<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<IEnhancerService, EnhancerService>();
        services.TryAddScoped<IEnhancementService, EnhancementService<TDbContext>>();
        services.TryAddScoped<ResourceService<TDbContext, CategoryEnhancerOptions, int>>();
        services.TryAddScoped<ICategoryEnhancerOptionsService, AbstractCategoryEnhancerOptionsService<TDbContext>>();
        services.TryAddScoped<ResourceService<TDbContext, EnhancementRecord, int>>();
        services.TryAddScoped<IEnhancementRecordService, EnhancementRecordService<TDbContext>>();
        services.AddTransient<IEnhancerLocalizer, EnhancerLocalizer>();


        var currentAssemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        var enhancerTypes = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IEnhancer>.Type) &&
                s is {IsPublic: true, IsAbstract: false})
            .ToList();
        foreach (var et in enhancerTypes)
        {
            services.AddScoped(SpecificTypeUtils<IEnhancer>.Type, et);
        }

        services.AddSingleton<IEnhancerDescriptors>(sp =>
        {
            var localizer = sp.GetRequiredService<IEnhancerLocalizer>();
            var enhancerDescriptors = SpecificEnumUtils<EnhancerId>.Values.Select(enhancerId =>
            {
                var attr = enhancerId.GetAttribute<EnhancerAttribute>()!;
                var targets = Enum.GetValues(attr.TargetEnumType).Cast<Enum>();
                var targetDescriptors = targets.Select(target =>
                {
                    var targetAttr = target.GetAttribute<EnhancerTargetAttribute>()!;
                    var converter = targetAttr.Converter == null
                        ? null
                        : Activator.CreateInstance(targetAttr.Converter) as IEnhancementConverter;
                    return new EnhancerTargetDescriptor(target,
                        enhancerId,
                        localizer,
                        targetAttr.ValueType,
                        targetAttr.PropertyType,
                        targetAttr.IsDynamic,
                        targetAttr.Options?.Cast<int>().ToArray(),
                        converter,
                        targetAttr.ReservedPropertyCandidate != default
                            ? targetAttr.ReservedPropertyCandidate
                            : null
                    );
                }).ToArray();

                return (IEnhancerDescriptor) new EnhancerDescriptor(enhancerId, localizer, targetDescriptors,
                    attr.PropertyValueScope, attr.Tags);
            }).ToArray();
            return new EnhancerDescriptors(enhancerDescriptors, localizer);
        });

        return services;
    }
}