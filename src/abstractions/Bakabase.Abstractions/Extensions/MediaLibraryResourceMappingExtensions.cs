using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryResourceMappingExtensions
{
    public static MediaLibraryResourceMappingDbModel ToDbModel(this MediaLibraryResourceMapping model)
    {
        return new MediaLibraryResourceMappingDbModel
        {
            Id = model.Id,
            MediaLibraryId = model.MediaLibraryId,
            ResourceId = model.ResourceId,
            CreateDt = model.CreateDt
        };
    }

    public static MediaLibraryResourceMapping ToDomainModel(this MediaLibraryResourceMappingDbModel dbModel)
    {
        return new MediaLibraryResourceMapping
        {
            Id = dbModel.Id,
            MediaLibraryId = dbModel.MediaLibraryId,
            ResourceId = dbModel.ResourceId,
            CreateDt = dbModel.CreateDt
        };
    }
}
