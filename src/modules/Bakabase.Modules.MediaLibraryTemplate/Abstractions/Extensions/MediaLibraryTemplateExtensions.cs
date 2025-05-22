using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bakabase.Modules.MediaLibraryTemplate.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;

public static class MediaLibraryTemplateExtensions
{
    public static IServiceCollection
        AddMediaLibraryTemplate<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, MediaLibraryTemplateDbModel, int>>();
        services.AddScoped<IMediaLibraryTemplateService, MediaLibraryTemplateService<TDbContext>>();
        return services;
    }

    public static MediaLibraryTemplateDbModel ToDbModel(this Models.Domain.MediaLibraryTemplate template)
    {
        return new MediaLibraryTemplateDbModel(
            template.Id,
            template.Name,
            JsonConvert.SerializeObject(template.ResourceFilters),
            JsonConvert.SerializeObject(template.Properties),
            JsonConvert.SerializeObject(template.PlayableFileLocator),
            JsonConvert.SerializeObject(template.Enhancers),
            template.DisplayNameTemplate,
            JsonConvert.SerializeObject(template.SamplePaths)
        );
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
        };
    }

    public static string Encode(this Models.Domain.MediaLibraryTemplate template)
    {
        var copy =
            JsonConvert.DeserializeObject<Models.Domain.MediaLibraryTemplate>(JsonConvert.SerializeObject(template))!;
        copy.Id = 0;
        if (copy.Properties != null)
        {
            foreach (var p in copy.Properties.Where(p => p.Pool == PropertyPool.Custom))
            {
                p.Id = 0;
            }
        }

        if (copy.PlayableFileLocator != null)
        {
            copy.PlayableFileLocator.ExtensionGroupIds = null;
        }

        return JsonConvert.SerializeObject(copy);
    }
}