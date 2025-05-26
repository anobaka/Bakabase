using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain;
using Microsoft.AspNetCore.Mvc.Filters;
using NPOI.Util.Collections;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;

public static class SharedMediaLibraryTemplateExtensions
{
    public static SharedMediaLibraryTemplate ToShared(this Models.Domain.MediaLibraryTemplate template)
    {
        return new SharedMediaLibraryTemplate
        {
            Name = template.Name,
            Author = template.Author,
            Description = template.Description,
            ResourceFilters = template.ResourceFilters?.Select(x => x.ToShared()).ToList(),
            Properties = template.Properties?.Select(x => x.ToShared()).ToList(),
            PlayableFileLocator = template.PlayableFileLocator?.ToShared(),
            Enhancers = template.Enhancers?.Select(x => x.ToShared()).ToList(),
            DisplayNameTemplate = template.DisplayNameTemplate,
            SamplePaths = template.SamplePaths,
            Child = template.Children?.ToShared()
        };
    }

    public static SharedMediaLibraryTemplateProperty ToShared(this MediaLibraryTemplateProperty property)
    {
        var p = property.Property;
        // We do not clear custom property id because it will be used to distinct properties during importing
        return new SharedMediaLibraryTemplateProperty
        {
            Property = p,
            ValueLocators = property.ValueLocators
        };
    }

    public static SharedMediaLibraryTemplatePlayableFileLocator ToShared(
        this MediaLibraryTemplatePlayableFileLocator locator)
    {
        return new SharedMediaLibraryTemplatePlayableFileLocator
        {
            ExtensionGroups = locator.ExtensionGroups,
            Extensions = locator.Extensions
        };
    }

    public static SharedMediaLibraryTemplateEnhancerOptions ToShared(this MediaLibraryTemplateEnhancerOptions options)
    {
        var o = options.Options;
        // We do not clear custom property id because it will be used to distinct properties during importing
        return new SharedMediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = options.EnhancerId,
            Options = o
        };
    }

    public static SharedPathFilter ToShared(this PathFilter filter)
    {
        return new SharedPathFilter
        {
            FsType = filter.FsType,
            ExtensionGroups = filter.ExtensionGroups,
            Extensions = filter.Extensions,
            Layer = filter.Layer,
            Positioner = filter.Positioner,
            Regex = filter.Regex
        };
    }

    public static Models.Domain.MediaLibraryTemplate ToDomainModel(this SharedMediaLibraryTemplate shared,
        Dictionary<int, int>? customPropertyIdConversionMap, Dictionary<int, int>? extensionGroupIdConversionMap)
    {
        return new Models.Domain.MediaLibraryTemplate
        {
            Name = shared.Name,
            Author = shared.Author,
            Description = shared.Description,
            ResourceFilters = shared.ResourceFilters?.Select(x => x.ToDomainModel(extensionGroupIdConversionMap))
                .ToList(),
            Properties = shared.Properties?.Select(x => x.ToDomainModel(customPropertyIdConversionMap)).ToList(),
            PlayableFileLocator = shared.PlayableFileLocator?.ToDomainModel(extensionGroupIdConversionMap),
            Enhancers = shared.Enhancers?.Select(x => x.ToDomainModel(customPropertyIdConversionMap)).ToList(),
            DisplayNameTemplate = shared.DisplayNameTemplate,
            SamplePaths = shared.SamplePaths,
            Children = shared.Child?.ToDomainModel(customPropertyIdConversionMap, extensionGroupIdConversionMap)
        };
    }

    public static MediaLibraryTemplateProperty ToDomainModel(this SharedMediaLibraryTemplateProperty shared,
        Dictionary<int, int>? customPropertyIdConversionMap)
    {
        var pool = shared.Property.Pool;
        var id = customPropertyIdConversionMap?.GetValueOrDefault(shared.Property.Id) ?? shared.Property.Id;
        return new MediaLibraryTemplateProperty
        {
            Pool = pool,
            Id = id,
            Property = shared.Property,
            ValueLocators = shared.ValueLocators
        };
    }

    public static MediaLibraryTemplatePlayableFileLocator ToDomainModel(
        this SharedMediaLibraryTemplatePlayableFileLocator shared, Dictionary<int, int>? extensionGroupIdConversionMap)
    {
        var groups = shared.ExtensionGroups
            ?.Select(g => g with {Id = extensionGroupIdConversionMap?.GetValueOrDefault(g.Id) ?? g.Id}).ToArray();
        return new MediaLibraryTemplatePlayableFileLocator
        {
            ExtensionGroups = groups,
            Extensions = shared.Extensions,
            ExtensionGroupIds = groups?.Select(d => d.Id).ToHashSet()
        };
    }

    public static MediaLibraryTemplateEnhancerOptions ToDomainModel(
        this SharedMediaLibraryTemplateEnhancerOptions shared, Dictionary<int, int>? customPropertyIdConversionMap)
    {
        return new MediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = shared.EnhancerId,
            Options = shared.Options
        };
    }

    public static PathFilter ToDomainModel(this SharedPathFilter shared,
        Dictionary<int, int>? extensionGroupIdConversionMap)
    {
        var groups = shared.ExtensionGroups
            ?.Select(g => g with {Id = extensionGroupIdConversionMap?.GetValueOrDefault(g.Id) ?? g.Id}).ToArray();
        return new PathFilter
        {
            FsType = shared.FsType,
            Extensions = shared.Extensions,
            Layer = shared.Layer,
            Positioner = shared.Positioner,
            Regex = shared.Regex,
            ExtensionGroups = groups,
            ExtensionGroupIds = groups?.Select(d => d.Id).ToHashSet()
        };
    }

    public static SharedMediaLibraryTemplate[] Flat(this SharedMediaLibraryTemplate template)
    {
        var list = new List<SharedMediaLibraryTemplate> {template};
        while (template.Child != null)
        {
            template = template.Child;
            list.Add(template);
        }

        return list.ToArray();
    }

    public static List<Bakabase.Abstractions.Models.Domain.Property> ExtractProperties(
        this SharedMediaLibraryTemplate[] templates)
    {
        return templates
            .SelectMany(t =>
                (t.Properties ?? []).Select(x => x.Property).Concat(
                    (t.Enhancers ?? []).SelectMany(e => e.Options?.TargetOptions?.Select(b => b.Property) ?? [])))
            .OfType<Bakabase.Abstractions.Models.Domain.Property>().GroupBy(d => $"{d.Pool}-{d.Id}")
            .Select(x => x.First()).ToList();
    }

    public static List<ExtensionGroup> ExtractExtensionGroups(this SharedMediaLibraryTemplate[] templates)
    {
        return templates
            .SelectMany(t =>
                t.ResourceFilters.SelectMany(x => x.ExtensionGroups ?? [])
                    .Concat(t.PlayableFileLocator?.ExtensionGroups ?? []))
            .GroupBy(d => d.Id)
            .Select(x => x.First())
            .ToList();
    }
}