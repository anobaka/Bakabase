using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.AspNetCore.Mvc.Filters;
using NPOI.Util.Collections;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;

public static class SharedMediaLibraryTemplateExtensions
{
    public static void EnsureSharable(this Models.Domain.MediaLibraryTemplate template)
    {
        if (template.ResourceFilters == null || template.ResourceFilters.Count == 0)
        {
            throw new ArgumentException("Template must have at least one resource filter to be sharable.");
        }
    }

    public static SharableMediaLibraryTemplate ToShared(this Models.Domain.MediaLibraryTemplate template,
        PropertyMap? propertyMap, Dictionary<int, ExtensionGroup>? extensionGroupMap)
    {
        template.EnsureSharable();
        return new SharableMediaLibraryTemplate
        {
            Name = template.Name,
            Author = template.Author,
            Description = template.Description,
            ResourceFilters = template.ResourceFilters!,
            Properties = template.Properties,
            PlayableFileLocator = template.PlayableFileLocator,
            Enhancers = template.Enhancers,
            DisplayNameTemplate = template.DisplayNameTemplate,
            SamplePaths = template.SamplePaths,
            Child = template.Children?.ToShared(propertyMap, extensionGroupMap)
        };
    }

    // public static SharedMediaLibraryTemplateProperty ToShared(this MediaLibraryTemplateProperty property,
    //     PropertyMap propertyMap)
    // {
    //     return new SharedMediaLibraryTemplateProperty
    //     {
    //         Property = propertyMap[property.Pool][property.Id],
    //         ValueLocators = property.ValueLocators
    //     };
    // }
    //
    // public static SharedMediaLibraryTemplatePlayableFileLocator ToShared(
    //     this MediaLibraryTemplatePlayableFileLocator locator, Dictionary<int, ExtensionGroup>? extensionGroupMap)
    // {
    //     return new SharedMediaLibraryTemplatePlayableFileLocator
    //     {
    //         ExtensionGroups = locator.ExtensionGroupIds?.Select(g => extensionGroupMap![g]).ToArray(),
    //         Extensions = locator.Extensions
    //     };
    // }
    //
    // public static SharedMediaLibraryTemplateEnhancerOptions ToShared(this MediaLibraryTemplateEnhancerOptions options,
    //     PropertyMap? propertyMap)
    // {
    //     // We do not clear custom property id because it will be used to distinct properties during importing
    //     return new SharedMediaLibraryTemplateEnhancerOptions
    //     {
    //         EnhancerId = options.EnhancerId,
    //         TargetOptions = options.TargetOptions?.Select(x => x.ToShared(propertyMap!)).ToList()
    //     };
    // }
    //
    // public static SharedMediaLibraryTemplateEnhancerTargetAllInOneOptions ToShared(
    //     this MediaLibraryTemplateEnhancerTargetAllInOneOptions options, PropertyMap propertyMap)
    // {
    //     return new SharedMediaLibraryTemplateEnhancerTargetAllInOneOptions
    //     {
    //         Property = propertyMap[options.PropertyPool][options.PropertyId],
    //         CoverSelectOrder = options.CoverSelectOrder,
    //         DynamicTarget = options.DynamicTarget,
    //         Target = options.Target
    //     };
    // }
    //
    // public static SharedPathFilter ToShared(this PathFilter filter)
    // {
    //     return new SharedPathFilter
    //     {
    //         FsType = filter.FsType,
    //         ExtensionGroups = filter.ExtensionGroups,
    //         Extensions = filter.Extensions,
    //         Layer = filter.Layer,
    //         Positioner = filter.Positioner,
    //         Regex = filter.Regex
    //     };
    // }

    public static Models.Domain.MediaLibraryTemplate ToDomainModel(this SharableMediaLibraryTemplate shared,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap,
        Dictionary<int, ExtensionGroup>? extensionGroupConversionMap, PropertyMap? propertyMap)
    {
        return new Models.Domain.MediaLibraryTemplate
        {
            Name = shared.Name,
            Author = shared.Author,
            Description = shared.Description,
            ResourceFilters = shared.ResourceFilters.Select(r => r.ToDomainModel(extensionGroupConversionMap)).ToList(),
            Properties = shared.Properties?.Select(x => x.ToDomainModel(customPropertyConversionMap, propertyMap!)).ToList(),
            PlayableFileLocator = shared.PlayableFileLocator?.ToDomainModel(extensionGroupConversionMap),
            Enhancers = shared.Enhancers?.Select(e => e.ToDomainModel(customPropertyConversionMap, propertyMap!)).ToList(),
            DisplayNameTemplate = shared.DisplayNameTemplate,
            SamplePaths = shared.SamplePaths,
            Children = shared.Child?.ToDomainModel(customPropertyConversionMap, extensionGroupConversionMap, propertyMap)
        };
    }

    public static MediaLibraryTemplateProperty ToDomainModel(this MediaLibraryTemplateProperty shared,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap, PropertyMap propertyMap)
    {
        var p = shared.Property;
        var pool = p.Pool;
        var id = p.Id;
        if (p.Pool == PropertyPool.Custom)
        {
            if (customPropertyConversionMap?.TryGetValue(p.Id, out var pi) == true)
            {
                pool = pi.Pool;
                id = pi.Id;
            }
            else
            {
                throw new Exception(
                    "A custom property must have a corresponding receiving property in order to be converted.");
            }
        }

        return new MediaLibraryTemplateProperty
        {
            Property = propertyMap[pool][id],
            ValueLocators = shared.ValueLocators
        };
    }

    public static MediaLibraryTemplatePlayableFileLocator ToDomainModel(
        this MediaLibraryTemplatePlayableFileLocator shared, Dictionary<int, ExtensionGroup>? extensionGroupIdConversionMap)
    {
        return shared with { ExtensionGroups = shared.ExtensionGroups?.Select(x => extensionGroupIdConversionMap![x.Id]).ToList() };
    }
    
    public static MediaLibraryTemplateEnhancerOptions ToDomainModel(
        this MediaLibraryTemplateEnhancerOptions shared,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap, PropertyMap propertyMap)
    {
        return shared with
        {
            TargetOptions = shared.TargetOptions
                ?.Select(x => x.ToDomainModel(customPropertyConversionMap, propertyMap)).ToList()
        };
    }
    
    public static MediaLibraryTemplateEnhancerTargetAllInOneOptions ToDomainModel(
        this MediaLibraryTemplateEnhancerTargetAllInOneOptions options,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap, PropertyMap propertyMap)
    {
        var p = options.Property;
        var pool = p.Pool;
        var id = p.Id;
        if (p.Pool == PropertyPool.Custom)
        {
            if (customPropertyConversionMap?.TryGetValue(p.Id, out var pi) == true)
            {
                pool = pi.Pool;
                id = pi.Id;
            }
            else
            {
                throw new Exception(
                    "A custom property must have a corresponding receiving property in order to be converted.");
            }
        }
    
        return new MediaLibraryTemplateEnhancerTargetAllInOneOptions
        {
            Property = propertyMap[pool][id],
            CoverSelectOrder = options.CoverSelectOrder,
            DynamicTarget = options.DynamicTarget,
            Target = options.Target
        };
    }

    public static PathFilter ToDomainModel(this PathFilter shared,
        Dictionary<int, ExtensionGroup>? extensionGroupIdConversionMap)
    {

        return shared with
        {
            ExtensionGroups = shared.ExtensionGroups?.Select(x => extensionGroupIdConversionMap![x.Id]).ToList()
        };
    }

    public static SharableMediaLibraryTemplate[] Flat(this SharableMediaLibraryTemplate template)
    {
        var list = new List<SharableMediaLibraryTemplate> { template };
        while (template.Child != null)
        {
            template = template.Child;
            list.Add(template);
        }

        return list.ToArray();
    }

    public static List<Bakabase.Abstractions.Models.Domain.Property> ExtractProperties(
        this SharableMediaLibraryTemplate[] templates)
    {
        return templates
            .SelectMany(t =>
                (t.Properties ?? []).Select(x => x.Property).Concat(
                    (t.Enhancers ?? []).SelectMany(e => e.TargetOptions?.Select(b => b.Property) ?? [])))
            .GroupBy(d => $"{d.Pool}-{d.Id}")
            .Select(x => x.First()).ToList();
    }

    public static List<ExtensionGroup> ExtractExtensionGroups(this SharableMediaLibraryTemplate[] templates)
    {
        return templates
            .SelectMany(t =>
                t.ResourceFilters.SelectMany(x => x.ExtensionGroups ?? [])
                    .Concat(t.PlayableFileLocator?.ExtensionGroups ?? Enumerable.Empty<ExtensionGroup>()))
            .GroupBy(d => d.Id)
            .Select(x => x.First())
            .ToList();
    }
}