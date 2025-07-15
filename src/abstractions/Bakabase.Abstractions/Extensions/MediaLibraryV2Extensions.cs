using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryV2Extensions
{
    private const char PathListSeparator = '|';

    public static MediaLibraryV2DbModel ToDbModel(this MediaLibraryV2 model)
    {
        return new MediaLibraryV2DbModel
        {
            Id = model.Id,
            Name = model.Name,
            Paths = string.Join(PathListSeparator, model.Paths),
            TemplateId = model.TemplateId,
            ResourceCount = model.ResourceCount,
            Color = model.Color,
        };
    }

    public static MediaLibraryV2 ToDomainModel(this MediaLibraryV2DbModel dbModel)
    {
        return new MediaLibraryV2
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Paths = string.IsNullOrEmpty(dbModel.Paths)
                ? []
                : dbModel.Paths.Split(PathListSeparator, StringSplitOptions.RemoveEmptyEntries).Distinct()
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(),
            TemplateId = dbModel.TemplateId,
            ResourceCount = dbModel.ResourceCount,
            Color = dbModel.Color,
        };
    }

    public static void SetPaths(this MediaLibraryV2DbModel model, IEnumerable<string> paths)
    {
        model.Paths = string.Join(PathListSeparator, paths.Select(p => p.StandardizePath()!).Distinct());
    }
}