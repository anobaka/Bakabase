using Bakabase.Abstractions.Exceptions;
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

        if (template.ResourceFilters.Any(r =>
                (r.ExtensionGroupIds ?? []).Except((r.ExtensionGroups ?? []).Select(eg => eg.Id)).Any()))
        {
            throw new ArgumentException(
                "All resource filters must have the same number of extension group ids and extension groups to be sharable.");
        }

        if (template.Properties?.Any(p => p.Property == null) == true)
        {
            throw new ArgumentException(
                "Template properties must have a valid property assigned to be sharable.");
        }

        if (template.PlayableFileLocator?.ExtensionGroupIds
                ?.Except((template.PlayableFileLocator?.ExtensionGroups ?? []).Select(x => x.Id)).Any() == true)
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
            Extensions = locator.Extensions,
            MaxFileCount = locator.MaxFileCount
        };
    }

    public static SharableMediaLibraryTemplateEnhancerOptions ToSharable(
        this EnhancerFullOptions options)
    {
        return new SharableMediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = options.EnhancerId,
            TargetOptions = options.TargetOptions?.Select(x => x.ToSharable()).ToList(),
            Requirements = options.Requirements,
            KeywordProperty = options.KeywordProperty
        };
    }

    public static SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions ToSharable(
        this EnhancerTargetFullOptions options)
    {
        if (options.Property == null)
        {
            throw new DevException("Cannot share an enhancer that binds to an unknown property");
        }
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

    public static List<List<Property>> ExtractUniqueCustomProperties(
        this SharableMediaLibraryTemplate[] flattenSharable)
    {
        var ps = flattenSharable.SelectMany(sharable =>
            (sharable.Properties?.Select(p => p.Property).ToList() ?? []).Concat(
                sharable.Enhancers?.SelectMany(e => e.TargetOptions?.Select(a => a.Property) ?? []) ?? [])).ToList();
        return ps.Where(p => p.Pool == PropertyPool.Custom).GroupBy(d => d.Id).SelectMany(p =>
            p.Key == 0 ? p.GroupBy(x => $"{x.Type}-{x.Name}").Select(x => x.ToList()) : [p.ToList()]).ToList();
    }

    public static List<List<ExtensionGroup>> ExtractUniqueExtensionGroups(
        this SharableMediaLibraryTemplate[] flattenSharable)
    {
        var egs = flattenSharable.SelectMany(sharable => (sharable.PlayableFileLocator?.ExtensionGroups ?? []).Concat(
            sharable.ResourceFilters.SelectMany(x => x.ExtensionGroups ?? []))).ToList();
        return egs.GroupBy(g => g.Id)
            .SelectMany(g =>
                g.Key == 0
                    ? g.GroupBy(x => $"{x.Name}-{string.Join(',', x.Extensions?.OrderBy(a => a).ToArray() ?? [])}")
                        .Select(x => x.ToList())
                    : [g.ToList()])
            .ToList();
    }

    public static MediaLibraryTemplate ToDomainModel(this SharableMediaLibraryTemplate sharable,
        Dictionary<int, ExtensionGroup>? extensionGroupMap, PropertyMap? propertyMap)
    {
        return new MediaLibraryTemplate
        {
            Name = sharable.Name,
            Author = sharable.Author,
            Description = sharable.Description,
            ResourceFilters = sharable.ResourceFilters.Select(r => r.ToDomainModel(extensionGroupMap))
                .ToList(),
            Properties = sharable.Properties?.Select(x => x.ToDomainModel(propertyMap!))
                .ToList(),
            PlayableFileLocator = sharable.PlayableFileLocator?.ToDomainModel(extensionGroupMap),
            Enhancers = sharable.Enhancers?.Select(e => e.ToDomainModel(propertyMap!))
                .ToList(),
            DisplayNameTemplate = sharable.DisplayNameTemplate,
            SamplePaths = sharable.SamplePaths,
            Child = sharable.Child?.ToDomainModel(extensionGroupMap, propertyMap)
        };
    }

    public static MediaLibraryTemplateProperty ToDomainModel(this SharableMediaLibraryTemplateProperty sharable,
        PropertyMap propertyMap)
    {
        var p = sharable.Property;
        return new MediaLibraryTemplateProperty
        {
            Property = propertyMap[p.Pool][p.Id],
            ValueLocators = sharable.ValueLocators
        };
    }

    public static MediaLibraryTemplatePlayableFileLocator ToDomainModel(
        this SharableMediaLibraryTemplatePlayableFileLocator sharable,
        Dictionary<int, ExtensionGroup>? extensionGroupMap)
    {
        var groups = sharable.ExtensionGroups?.Select(x => extensionGroupMap![x.Id]).ToList();
        return new MediaLibraryTemplatePlayableFileLocator
        {
            Extensions = sharable.Extensions,
            ExtensionGroupIds = groups?.Select(g => g.Id).ToHashSet(),
            ExtensionGroups = groups,
            MaxFileCount = sharable.MaxFileCount
        };
    }

    public static EnhancerFullOptions ToDomainModel(
        this SharableMediaLibraryTemplateEnhancerOptions sharable, PropertyMap propertyMap)
    {
        var targetOptions = sharable.TargetOptions
            ?.Select(x => x.ToDomainModel(propertyMap)).ToList();

        return new EnhancerFullOptions
        {
            EnhancerId = sharable.EnhancerId,
            TargetOptions = targetOptions,
            Requirements = sharable.Requirements,
            KeywordProperty = sharable.KeywordProperty
        };
    }

    public static EnhancerTargetFullOptions ToDomainModel(
        this SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions options, PropertyMap propertyMap)
    {
        var p = options.Property;

        return new EnhancerTargetFullOptions
        {
            PropertyPool = p.Pool,
            PropertyId = p.Id,
            Property = propertyMap[p.Pool][p.Id],
            CoverSelectOrder = options.CoverSelectOrder,
            DynamicTarget = options.DynamicTarget,
            Target = options.Target
        };
    }

    public static PathFilter ToDomainModel(this SharablePathFilter sharable,
        Dictionary<int, ExtensionGroup>? extensionGroupMap)
    {
        var groups = sharable.ExtensionGroups?.Select(x => extensionGroupMap![x.Id]).ToList();
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
}