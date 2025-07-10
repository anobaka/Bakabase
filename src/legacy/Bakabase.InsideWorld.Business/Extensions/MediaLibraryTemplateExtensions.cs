using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using DotNext.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
                            .Select(v => v.LocateValues(relativePath, rpi))
                            .OfType<string[]>()
                            .SelectMany(v => v).Distinct()
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

    public static void InitFromMediaLibraryV1(this MediaLibraryTemplate template, MediaLibrary mediaLibrary, int pcIdx,
        Category category, IPlayableFileSelector? playableFileSelector, IOptions<EnhancerOptions> enhancerOptions)
    {
        if (!(mediaLibrary.PathConfigurations?.Count > pcIdx))
        {
            throw new Exception($"Can't find path configuration in media library by index {pcIdx}");
        }


        var pc = mediaLibrary.PathConfigurations[pcIdx];
        var rpf = pc.RpmValues?.FirstOrDefault(x => x.IsResourceProperty)?.ToPathFilter();
        template.ResourceFilters = rpf == null ? null : [rpf];
        template.Properties = category.CustomProperties?.Select(c =>
        {
            var p = c.ToProperty();
            return new MediaLibraryTemplateProperty
            {
                Property = p,
                Pool = p.Pool,
                Id = p.Id
            };
        }).ToList();
        if (pc.RpmValues != null)
        {
            foreach (var rv in pc.RpmValues)
            {
                if (rv.IsSecondaryProperty)
                {
                    var pool = rv.IsCustomProperty ? PropertyPool.Custom : PropertyPool.Reserved;
                    var id = rv.PropertyId;
                    var tp = template.Properties?.FirstOrDefault(x => x.Pool == pool && x.Id == id);
                    if (tp == null)
                    {
                        tp = new MediaLibraryTemplateProperty
                        {
                            Pool = pool,
                            Id = id
                        };
                        (template.Properties ??= []).Add(tp);
                    }

                    var filter = rv.ToPathFilter();
                    tp.ValueLocators ??= [];
                    tp.ValueLocators.Add(filter);
                }
            }
        }

        template.PlayableFileLocator = new MediaLibraryTemplatePlayableFileLocator
        {
            Extensions = playableFileSelector?.TryGetExtensions()
        };



        template.Enhancers = category.EnhancerOptions?.Select(eo =>
        {
            var ceo = eo as CategoryEnhancerFullOptions;
            if (ceo?.Active != true)
            {
                return null;
            }

            var isRegexEnhancer = eo.EnhancerId == (int) EnhancerId.Regex;

            return new MediaLibraryTemplateEnhancerOptions
            {
                EnhancerId = eo.EnhancerId,
                TargetOptions = ceo?.Options?.TargetOptions
                    ?.Where(to => to is {PropertyPool: not null, PropertyId: not null}).Select(to =>
                        new MediaLibraryTemplateEnhancerTargetAllInOneOptions
                        {
                            CoverSelectOrder = to.CoverSelectOrder,
                            Target = to.Target,
                            DynamicTarget = to.DynamicTarget,
                            PropertyId = to.PropertyId!.Value,
                            PropertyPool = to.PropertyPool!.Value,
                        }).ToList(),
                Expressions = isRegexEnhancer ? enhancerOptions.Value.RegexEnhancer?.Expressions : null
            };
        }).OfType<MediaLibraryTemplateEnhancerOptions>().ToList();
        template.DisplayNameTemplate = category.ResourceDisplayNameTemplate;
    }
}