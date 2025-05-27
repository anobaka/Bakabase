using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bakabase.Modules.MediaLibraryTemplate.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using MediaLibraryTemplateEnhancerOptions = Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.MediaLibraryTemplateEnhancerOptions;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;

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

    public static MediaLibraryTemplateDbModel ToDbModel(this Models.Domain.MediaLibraryTemplate template)
    {
        return new MediaLibraryTemplateDbModel
        {
            Id = template.Id,
            Name = template.Name,
            ResourceFilters = JsonConvert.SerializeObject(template.ResourceFilters),
            Properties = JsonConvert.SerializeObject(template.Properties),
            PlayableFileLocator = JsonConvert.SerializeObject(template.PlayableFileLocator),
            Enhancers = JsonConvert.SerializeObject(template.Enhancers),
            DisplayNameTemplate = template.DisplayNameTemplate,
            SamplePaths = JsonConvert.SerializeObject(template.SamplePaths),
            ChildTemplateId = template.ChildrenTemplateId
        };
    }

    public static Models.Domain.MediaLibraryTemplate ToDomainModel(this MediaLibraryTemplateDbModel dbModel)
    {
        return new Models.Domain.MediaLibraryTemplate
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            DisplayNameTemplate = dbModel.DisplayNameTemplate,
            ResourceFilters = dbModel.ResourceFilters != null
                ? JsonConvert.DeserializeObject<List<PathFilter>>(dbModel.ResourceFilters)
                : null,
            Properties = dbModel.Properties != null
                ? JsonConvert.DeserializeObject<List<MediaLibraryTemplateProperty>>(dbModel.Properties)
                : null,
            PlayableFileLocator = dbModel.PlayableFileLocator != null
                ? JsonConvert.DeserializeObject<MediaLibraryTemplatePlayableFileLocator>(dbModel.PlayableFileLocator)
                : null,
            Enhancers = dbModel.Enhancers != null
                ? JsonConvert.DeserializeObject<List<MediaLibraryTemplateEnhancerOptions>>(dbModel.Enhancers)
                : null,
            SamplePaths = dbModel.SamplePaths != null
                ? JsonConvert.DeserializeObject<List<string>>(dbModel.SamplePaths)
                : null,
            ChildrenTemplateId = dbModel.ChildTemplateId
        };
    }
}