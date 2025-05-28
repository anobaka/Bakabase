using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class MediaLibraryTemplateExtensions
{
    public static IServiceCollection
        AddMediaLibraryTemplate<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, MediaLibraryTemplateDbModel, int>>();
        services.AddScoped<IMediaLibraryTemplateService, MediaLibraryTemplateService<TDbContext>>();

        services.AddScoped<FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int>>();
        services.AddScoped<IMediaLibraryV2Service, MediaLibraryV2Service<TDbContext>>();

        return services;
    }

    public static EnhancerFullOptions ToEnhancerFullOptions(this MediaLibraryTemplateEnhancerOptions options)
    {
        return new EnhancerFullOptions
        {
            TargetOptions = options.TargetOptions?.Select(o => o.ToEnhancerTargetFullOptions()).ToList()
        };
    }

    public static EnhancerTargetFullOptions ToEnhancerTargetFullOptions(
        this MediaLibraryTemplateEnhancerTargetAllInOneOptions options)
    {
        return new EnhancerTargetFullOptions
        {
            PropertyPool = options.PropertyPool,
            PropertyId = options.PropertyId,
            Target = options.Target,
            DynamicTarget = options.DynamicTarget,
            CoverSelectOrder = options.CoverSelectOrder,
        };
    }

    public static List<TempSyncResource> DiscoverResources(this MediaLibraryTemplate template, string rootPath)
    {
        var resources = new List<TempSyncResource>();
        foreach (var path in template.ResourceFilters?.SelectMany(f => f.Filter(rootPath)).Distinct() ?? [])
        {
            var resource = new TempSyncResource(path);
            var fi = new FileInfo(path);
            resource.IsFile = !fi.Attributes.HasFlag(FileAttributes.Directory);
            resource.FileCreatedAt = fi.CreationTime;
            resource.FileModifiedAt = fi.LastWriteTime;

            var relativePath = path.Replace(Path.GetDirectoryName(rootPath)!, null).StandardizePath()!
                .Trim(InternalOptions.DirSeparator).StandardizePath()!;
            if (template.Properties != null)
            {
                foreach (var propertyDefinition in template.Properties)
                {
                    var property = propertyDefinition.Property;
                    object? bizValue = null;
                    if (propertyDefinition.ValueLocators != null)
                    {
                        var listStr = propertyDefinition.ValueLocators
                            .SelectMany(v => v.LocateValues(relativePath)).Distinct()
                            .ToList();
                        bizValue = StandardValueInternals.HandlerMap[StandardValueType.ListString]
                            .Convert(listStr, property!.Type.GetBizValueType());
                    }

                    if (bizValue == null)
                    {
                        var dbValue = (property!.Options as IDefaultValue)?.DefaultValue;
                        bizValue = PropertyInternals.DescriptorMap[property.Type].GetBizValue(property, dbValue);
                    }

                    if (bizValue != null)
                    {
                        (resource.PropertyValues ??= []).GetOrAdd(property!, () => bizValue);
                    }
                }
            }

            if (template.Child != null)
            {
                resource.Children = DiscoverResources(template.Child, resource.Path);
                if (resource.Children != null)
                {
                    foreach (var c in resource.Children)
                    {
                        c.Parent = resource;
                    }
                }
            }

            resources.Add(resource);
        }

        return resources;
    }
}