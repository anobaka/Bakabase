using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

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
            SyncVersion = model.SyncVersion,
            Players = JsonConvert.SerializeObject(model.Players?.Select(p => p.ToDbModel()))
        };
    }

    public static MediaLibraryV2 ToDomainModel(this MediaLibraryV2DbModel dbModel)
    {
        var domain = new MediaLibraryV2
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
            SyncVersion = dbModel.SyncVersion,
        };
        if (!string.IsNullOrEmpty(dbModel.Players))
        {
            try
            {
                var dbPlayers = JsonConvert.DeserializeObject<List<MediaLibraryPlayerDbModel>>(dbModel.Players);
                domain.Players = dbPlayers?.Select(p => p.ToDomainModel()).ToList();
            }
            catch (Exception e)
            {
                // ignored
            }
        }

        return domain;
    }

    public static void SetPaths(this MediaLibraryV2DbModel model, IEnumerable<string> paths)
    {
        model.Paths = string.Join(PathListSeparator, paths.Select(p => p.StandardizePath()!).Distinct());
    }
}