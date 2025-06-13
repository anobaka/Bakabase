using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryV2Extensions
{
    public static MediaLibraryV2DbModel ToDbModel(this MediaLibraryV2 model)
    {
        return new MediaLibraryV2DbModel
        {
            Id = model.Id,
            Name = model.Name,
            Path = model.Path,
            TemplateId = model.TemplateId,
            ResourceCount = model.ResourceCount,
        };
    }

    public static MediaLibraryV2 ToDomainModel(this MediaLibraryV2DbModel dbModel)
    {
        return new MediaLibraryV2
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Path = dbModel.Path,
            TemplateId = dbModel.TemplateId,
            ResourceCount = dbModel.ResourceCount
        };
    }
}
