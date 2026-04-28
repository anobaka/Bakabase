using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using DotNext.Collections.Generic;
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

        // New services for media library refactoring
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, PathMarkDbModel, int>>();
        services.AddScoped<IPathMarkService, PathMarkService<TDbContext>>();

        // PathMark effect tracking services
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, ResourceMarkEffectDbModel, int>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, PropertyMarkEffectDbModel, int>>();
        services.AddScoped<IPathMarkEffectService, PathMarkEffectService<TDbContext>>();

        services.AddScoped<PathMarkSyncService>();
        services.AddScoped<ResourceSyncService>();
        services.AddSingleton<PathSyncManager>();
        services.AddHostedService(sp => sp.GetRequiredService<PathSyncManager>());
        services.AddSingleton<IPathMarkSyncService>(sp => sp.GetRequiredService<PathSyncManager>());

        services.AddScoped<FullMemoryCacheResourceService<TDbContext, MediaLibraryResourceMappingDbModel, int>>();
        services.AddSingleton<MediaLibraryResourceMappingIndexService>();
        services.AddScoped<IMediaLibraryResourceMappingService, MediaLibraryResourceMappingService<TDbContext>>();

        services.AddScoped<FullMemoryCacheResourceService<TDbContext, ResourceSourceLinkDbModel, int>>();
        services.AddScoped<IResourceSourceLinkService, ResourceSourceLinkService<TDbContext>>();

        services.AddScoped<FullMemoryCacheResourceService<TDbContext, ResourceProfileDbModel, int>>();
        services.AddScoped<IResourceProfileService, ResourceProfileService<TDbContext>>();

        return services;
    }

    public static EnhancerFullOptions ToEnhancerFullOptions(this EnhancerFullOptions options)
    {
        return new EnhancerFullOptions
        {
            TargetOptions = options.TargetOptions?.Select(o => o.ToEnhancerTargetFullOptions()).ToList()
        };
    }

    public static EnhancerTargetFullOptions ToEnhancerTargetFullOptions(
        this EnhancerTargetFullOptions options)
    {
        return new EnhancerTargetFullOptions
        {
            PropertyPool = options.PropertyPool,
            PropertyId = options.PropertyId,
            Target = options.Target,
            DynamicTarget = options.DynamicTarget,
            CoverSelectOrder = options.CoverSelectOrder,
            CustomPrompt = options.CustomPrompt,
        };
    }

    public static async Task<List<TempSyncResource>> DiscoverResources(this MediaLibraryTemplate template,
        string rootPath, Func<int, Task>? onProgressChange, Func<string, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct, int maxThreads = 1)
    {
        if (template.ResourceFilters == null || template.ResourceFilters.Count == 0)
        {
            return [];
        }

        var resources = new List<TempSyncResource>();

        await using var progressor = new BProgressor(onProgressChange);

        const float progressForFiltering = 70;
        const float progressForInitializing = 100 - progressForFiltering;

        var filterProgressor = progressor.CreateNewScope(0, progressForFiltering);
        var rpiMap =
            await template.ResourceFilters.Filter(rootPath, null, p => filterProgressor.Set(p), null, pt, ct,
                maxThreads);
        await filterProgressor.DisposeAsync();
        
        var initializingProgressor = progressor.CreateNewScope(progressForFiltering, progressForInitializing);
        var progressPerPath = rpiMap.Count == 0 ? 0 : 100f / rpiMap.Count;
        foreach (var (path, rpi) in rpiMap)
        {
            var resource = new TempSyncResource(path);
            var fi = new FileInfo(path);
            resource.IsFile = !fi.Attributes.HasFlag(FileAttributes.Directory);
            resource.FileCreatedAt = fi.CreationTime.TruncateToMilliseconds();
            resource.FileModifiedAt = fi.LastWriteTime.TruncateToMilliseconds();

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
                            .Select(v => v.ExtractValues(relativePath, rpi))
                            .OfType<string[]>()
                            .SelectMany(v => v).Distinct()
                            .ToList();
                        bizValue = StandardValueSystem.GetHandler(StandardValueType.ListString)
                            .Convert(listStr, property!.Type.GetBizValueType());
                    }

                    if (bizValue == null)
                    {
                        var dbValue = (property!.Options as IDefaultValue)?.DefaultValue;
                        bizValue = PropertySystem.Property.GetDescriptor(property.Type).GetBizValue(property, dbValue);
                    }

                    if (bizValue != null)
                    {
                        (resource.PropertyValues ??= []).GetOrAdd(property!, _ => bizValue);
                    }
                }
            }

            if (template.Child != null && !rpi.IsFile)
            {
                resource.Children =
                    await DiscoverResources(template.Child, resource.Path, null, null, pt, ct);
                if (resource.Children != null)
                {
                    foreach (var c in resource.Children)
                    {
                        c.Parent = resource;
                    }
                }
            }

            resources.Add(resource);
            await initializingProgressor.Add(progressPerPath);
        }

        return resources;
    }
}