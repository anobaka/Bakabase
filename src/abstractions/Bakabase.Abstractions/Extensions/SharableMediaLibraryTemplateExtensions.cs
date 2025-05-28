using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Sharable;

namespace Bakabase.Abstractions.Extensions;

public static class SharableMediaLibraryTemplateExtensions
{
    public static void EnsureSharable(this Models.Domain.MediaLibraryTemplate template)
    {
        if (template.ResourceFilters == null || template.ResourceFilters.Count == 0)
        {
            throw new ArgumentException("Template must have at least one resource filter to be sharable.");
        }

        if (template.ResourceFilters.Any(r => r.ExtensionGroupIds?.Count != r.ExtensionGroups?.Count))
        {
            throw new ArgumentException(
                "All resource filters must have the same number of extension group ids and extension groups to be sharable.");
        }

        if (template.Properties?.Any(p => p.Property == null) == true)
        {
            throw new ArgumentException(
                "Template properties must have a valid property assigned to be sharable.");
        }

        if (template.PlayableFileLocator?.ExtensionGroupIds?.Count !=
            template.PlayableFileLocator?.ExtensionGroups?.Count)
        {
            throw new ArgumentException(
                "Playable file locator must have the same number of extension group ids and extension groups to be sharable.");
        }
    }

    public static SharableMediaLibraryTemplate ToSharable(this Models.Domain.MediaLibraryTemplate template)
    {
        template.EnsureSharable();
        return new SharableMediaLibraryTemplate
        {
            Name = template.Name,
            Author = template.Author,
            Description = template.Description,
            ResourceFilters = template.ResourceFilters!.Select(f => f.ToSharable()).ToList(),
            Properties = template.Properties?.Select(p => p.ToSharable()).ToList(),
            PlayableFileLocator = template.PlayableFileLocator?.ToSharable(),
            Enhancers = template.Enhancers?.Select(p => p.ToSharable()).ToList(),
            DisplayNameTemplate = template.DisplayNameTemplate,
            SamplePaths = template.SamplePaths,
            Child = template.Child?.ToSharable()
        };
    }

    public static SharableMediaLibraryTemplateProperty ToSharable(this MediaLibraryTemplateProperty property)
    {
        return new SharableMediaLibraryTemplateProperty
        {
            Property = property.Property!,
            ValueLocators = property.ValueLocators
        };
    }

    public static SharableMediaLibraryTemplatePlayableFileLocator ToSharable(
        this MediaLibraryTemplatePlayableFileLocator locator)
    {
        return new SharableMediaLibraryTemplatePlayableFileLocator
        {
            ExtensionGroups = locator.ExtensionGroups,
            Extensions = locator.Extensions
        };
    }

    public static SharableMediaLibraryTemplateEnhancerOptions ToSharable(
        this MediaLibraryTemplateEnhancerOptions options)
    {
        return new SharableMediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = options.EnhancerId,
            TargetOptions = options.TargetOptions?.Select(x => x.ToSharable()).ToList()
        };
    }

    public static SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions ToSharable(
        this MediaLibraryTemplateEnhancerTargetAllInOneOptions options)
    {
        return new SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions
        {
            Property = options.Property!,
            CoverSelectOrder = options.CoverSelectOrder,
            DynamicTarget = options.DynamicTarget,
            Target = options.Target
        };
    }

    public static SharablePathFilter ToSharable(this PathFilter filter)
    {
        return new SharablePathFilter
        {
            FsType = filter.FsType,
            ExtensionGroups = filter.ExtensionGroups,
            Extensions = filter.Extensions,
            Layer = filter.Layer,
            Positioner = filter.Positioner,
            Regex = filter.Regex
        };
    }

    public static Models.Domain.MediaLibraryTemplate ToDomainModel(this SharableMediaLibraryTemplate sharable,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap,
        Dictionary<int, ExtensionGroup>? extensionGroupConversionMap, PropertyMap? propertyMap)
    {
        return new Models.Domain.MediaLibraryTemplate
        {
            Name = sharable.Name,
            Author = sharable.Author,
            Description = sharable.Description,
            ResourceFilters = sharable.ResourceFilters.Select(r => r.ToDomainModel(extensionGroupConversionMap))
                .ToList(),
            Properties = sharable.Properties?.Select(x => x.ToDomainModel(customPropertyConversionMap, propertyMap!))
                .ToList(),
            PlayableFileLocator = sharable.PlayableFileLocator?.ToDomainModel(extensionGroupConversionMap),
            Enhancers = sharable.Enhancers?.Select(e => e.ToDomainModel(customPropertyConversionMap, propertyMap!))
                .ToList(),
            DisplayNameTemplate = sharable.DisplayNameTemplate,
            SamplePaths = sharable.SamplePaths,
            Child = sharable.Child?.ToDomainModel(customPropertyConversionMap, extensionGroupConversionMap, propertyMap)
        };
    }

    public static MediaLibraryTemplateProperty ToDomainModel(this SharableMediaLibraryTemplateProperty sharable,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap, PropertyMap propertyMap)
    {
        var p = sharable.Property;
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
            ValueLocators = sharable.ValueLocators
        };
    }

    public static MediaLibraryTemplatePlayableFileLocator ToDomainModel(
        this SharableMediaLibraryTemplatePlayableFileLocator sharable,
        Dictionary<int, ExtensionGroup>? extensionGroupIdConversionMap)
    {
        var groups = sharable.ExtensionGroups?.Select(x => extensionGroupIdConversionMap![x.Id]).ToList();
        return new MediaLibraryTemplatePlayableFileLocator
        {
            Extensions = sharable.Extensions,
            ExtensionGroupIds = groups?.Select(g => g.Id).ToHashSet(),
            ExtensionGroups = groups
        };
    }

    public static MediaLibraryTemplateEnhancerOptions ToDomainModel(
        this SharableMediaLibraryTemplateEnhancerOptions sharable,
        Dictionary<int, (PropertyPool Pool, int Id)>? customPropertyConversionMap, PropertyMap propertyMap)
    {
        var targetOptions = sharable.TargetOptions
            ?.Select(x => x.ToDomainModel(customPropertyConversionMap, propertyMap)).ToList();

        return new MediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = sharable.EnhancerId,
            TargetOptions = targetOptions
        };
    }

    public static MediaLibraryTemplateEnhancerTargetAllInOneOptions ToDomainModel(
        this SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions options,
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
            PropertyPool = pool,
            PropertyId = id,
            Property = propertyMap[pool][id],
            CoverSelectOrder = options.CoverSelectOrder,
            DynamicTarget = options.DynamicTarget,
            Target = options.Target
        };
    }

    public static PathFilter ToDomainModel(this SharablePathFilter sharable,
        Dictionary<int, ExtensionGroup>? extensionGroupIdConversionMap)
    {
        var groups = sharable.ExtensionGroups?.Select(x => extensionGroupIdConversionMap![x.Id]).ToList();
        return new PathFilter
        {
            ExtensionGroupIds = groups?.Select(g => g.Id).ToHashSet(),
            ExtensionGroups = groups,
            Extensions = sharable.Extensions,
            FsType = sharable.FsType,
            Positioner = sharable.Positioner,
            Layer = sharable.Layer,
            Regex = sharable.Regex
        };
    }

    public static SharableMediaLibraryTemplate[] Flat(this SharableMediaLibraryTemplate template)
    {
        var list = new List<SharableMediaLibraryTemplate> {template};
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