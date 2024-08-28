using System.Reflection;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnhancers<TDbContext, TEnhancementService, TEnhancerService,
        TCategoryEnhancerOptionsService, TEnhancementRecordService>(this IServiceCollection services)
        where TEnhancerService : class, IEnhancerService
        where TEnhancementService : class, IEnhancementService
        where TCategoryEnhancerOptionsService : class, ICategoryEnhancerOptionsService
        where TDbContext : DbContext
        where TEnhancementRecordService : class, IEnhancementRecordService
    {
        services.TryAddScoped<IEnhancerService, TEnhancerService>();
        services.TryAddScoped<IEnhancementService, TEnhancementService>();
        services.TryAddScoped<ResourceService<TDbContext, CategoryEnhancerOptions, int>>();
        services.TryAddScoped<ICategoryEnhancerOptionsService, TCategoryEnhancerOptionsService>();
        services.TryAddScoped<ResourceService<TDbContext, EnhancementRecord, int>>();
        services.TryAddScoped<IEnhancementRecordService, TEnhancementRecordService>();
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
                var attr = enhancerId.GetAttribute<EnhancerAttribute>();
                var targets = Enum.GetValues(attr.TargetEnumType).Cast<Enum>();
                var targetDescriptors = targets.Select(target =>
                {
                    var targetAttr = target.GetAttribute<EnhancerTargetAttribute>();
                    var converter = targetAttr.Converter == null
                        ? null
                        : Activator.CreateInstance(targetAttr.Converter) as IEnhancementConverter;
                    return new EnhancerTargetDescriptor(target,
                        enhancerId,
                        localizer,
                        targetAttr.ValueType,
                        targetAttr.CustomPropertyType,
                        targetAttr.IsDynamic,
                        targetAttr.Options?.Cast<int>().ToArray(),
                        converter
                    );
                }).ToArray();

                return (IEnhancerDescriptor) new EnhancerDescriptor(enhancerId, localizer, targetDescriptors,
                    attr.PropertyValueScope);
            }).ToArray();
            return new EnhancerDescriptors(enhancerDescriptors, localizer);
        });

        return services;
    }
}