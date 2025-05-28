using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryTemplateExtensions
{

    public static PathFilterDbModel ToDbModel(this PathFilter filter)
    {
        return new PathFilterDbModel
        {
            ExtensionGroupIds = filter.ExtensionGroups?.Select(e => e.Id).ToHashSet(),
            Extensions = filter.Extensions,
            FsType = filter.FsType,
            Layer = filter.Layer,
            Positioner = filter.Positioner,
            Regex = filter.Regex
        };
    }

    public static MediaLibraryTemplatePropertyDbModel ToDbModel(this MediaLibraryTemplateProperty property)
    {
        return new MediaLibraryTemplatePropertyDbModel
        {
            Id = property.Id,
            Pool = property.Pool,
            ValueLocators = property.ValueLocators,
        };
    }

    public static MediaLibraryTemplatePlayableFileLocatorDbModel ToDbModel(
        this MediaLibraryTemplatePlayableFileLocator locator)
    {
        return new MediaLibraryTemplatePlayableFileLocatorDbModel
        {
            ExtensionGroupIds = locator.ExtensionGroups?.Select(g => g.Id).ToHashSet(),
            Extensions = locator.Extensions
        };
    }

    public static MediaLibraryTemplateEnhancerOptionsDbModel ToDbModel(this MediaLibraryTemplateEnhancerOptions options)
    {
        return new MediaLibraryTemplateEnhancerOptionsDbModel
        {
            EnhancerId = options.EnhancerId,
            TargetOptions = options.TargetOptions?.Select(t => t.ToDbModel()).ToList()
        };
    }

    public static MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel ToDbModel(
        this MediaLibraryTemplateEnhancerTargetAllInOneOptions target)
    {
        return new MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel
        {
            PropertyPool = target.PropertyPool,
            PropertyId = target.PropertyId,
            Target = target.Target,
            DynamicTarget = target.DynamicTarget,
            CoverSelectOrder = target.CoverSelectOrder
        };
    }

    public static MediaLibraryTemplateDbModel ToDbModel(this Models.Domain.MediaLibraryTemplate template)
    {
        return new MediaLibraryTemplateDbModel
        {
            Id = template.Id,
            Name = template.Name,
            Author = template.Author,
            Description = template.Description,
            ResourceFilters = JsonConvert.SerializeObject(template.ResourceFilters?.Select(x => x.ToDbModel())),
            Properties = JsonConvert.SerializeObject(template.Properties?.Select(p => p.ToDbModel())),
            PlayableFileLocator = JsonConvert.SerializeObject(template.PlayableFileLocator?.ToDbModel()),
            Enhancers = JsonConvert.SerializeObject(template.Enhancers?.Select(e => e.ToDbModel())),
            DisplayNameTemplate = template.DisplayNameTemplate,
            SamplePaths = JsonConvert.SerializeObject(template.SamplePaths),
            ChildTemplateId = template.Child?.Id
        };
    }

    public static PathFilter ToDomainModel(this PathFilterDbModel dbModel)
    {
        return new PathFilter
        {
            Extensions = dbModel.Extensions,
            FsType = dbModel.FsType,
            Layer = dbModel.Layer,
            Positioner = dbModel.Positioner,
            Regex = dbModel.Regex,
            ExtensionGroupIds = dbModel.ExtensionGroupIds
        };
    }

    public static MediaLibraryTemplateProperty ToDomainModel(this MediaLibraryTemplatePropertyDbModel dbModel)
    {
        return new MediaLibraryTemplateProperty
        {
            Id = dbModel.Id,
            Pool = dbModel.Pool,
            ValueLocators = dbModel.ValueLocators,
        };
    }

    public static MediaLibraryTemplatePlayableFileLocator ToDomainModel(
        this MediaLibraryTemplatePlayableFileLocatorDbModel dbModel)
    {
        return new MediaLibraryTemplatePlayableFileLocator
        {
            ExtensionGroupIds = dbModel.ExtensionGroupIds,
            Extensions = dbModel.Extensions
        };
    }

    public static MediaLibraryTemplateEnhancerOptions ToDomainModel(
        this MediaLibraryTemplateEnhancerOptionsDbModel dbModel)
    {
        return new MediaLibraryTemplateEnhancerOptions
        {
            EnhancerId = dbModel.EnhancerId,
            TargetOptions = dbModel.TargetOptions?.Select(t => t.ToDomainModel()).ToList()
        };
    }

    public static MediaLibraryTemplateEnhancerTargetAllInOneOptions ToDomainModel(
        this MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel dbModel)
    {
        return new MediaLibraryTemplateEnhancerTargetAllInOneOptions
        {
            PropertyPool = dbModel.PropertyPool,
            PropertyId = dbModel.PropertyId,
            Target = dbModel.Target,
            DynamicTarget = dbModel.DynamicTarget,
            CoverSelectOrder = dbModel.CoverSelectOrder
        };
    }

    public static Models.Domain.MediaLibraryTemplate ToDomainModel(this MediaLibraryTemplateDbModel dbModel)
    {
        var dbFilters = dbModel.ResourceFilters != null
            ? JsonConvert.DeserializeObject<List<PathFilterDbModel>>(dbModel.ResourceFilters)
            : null;

        var dbProperties = dbModel.Properties != null
            ? JsonConvert.DeserializeObject<List<MediaLibraryTemplatePropertyDbModel>>(dbModel.Properties)
            : null;
        var dbPlayableFileLocator = dbModel.PlayableFileLocator != null
            ? JsonConvert.DeserializeObject<MediaLibraryTemplatePlayableFileLocatorDbModel>(dbModel.PlayableFileLocator)
            : null;
        var dbEnhancers = dbModel.Enhancers != null
            ? JsonConvert.DeserializeObject<List<MediaLibraryTemplateEnhancerOptionsDbModel>>(dbModel.Enhancers)
            : null;

        return new Models.Domain.MediaLibraryTemplate
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Author = dbModel.Author,
            Description = dbModel.Description,
            DisplayNameTemplate = dbModel.DisplayNameTemplate,
            ResourceFilters = dbFilters?.Select(f => f.ToDomainModel()).ToList(),
            Properties = dbProperties?.Select(p => p.ToDomainModel()).ToList(),
            PlayableFileLocator = dbPlayableFileLocator?.ToDomainModel(),
            Enhancers = dbEnhancers?.Select(e => e.ToDomainModel()).ToList(),
            SamplePaths = dbModel.SamplePaths != null
                ? JsonConvert.DeserializeObject<List<string>>(dbModel.SamplePaths)
                : null
        };
    }
}