using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceSourceLinkExtensions
{
    public static ResourceSourceLinkDbModel ToDbModel(this ResourceSourceLink model)
    {
        return new ResourceSourceLinkDbModel
        {
            Id = model.Id,
            ResourceId = model.ResourceId,
            Source = model.Source,
            SourceKey = model.SourceKey,
            CreateDt = model.CreateDt,
            CoverUrls = StringListSerializer.Serialize(model.CoverUrls),
            LocalCoverPaths = StringListSerializer.Serialize(AppDataPaths.RelativizeAll(model.LocalCoverPaths)),
            CoverDownloadFailedAt = model.CoverDownloadFailedAt,
            MetadataJson = model.MetadataJson,
            MetadataFetchedAt = model.MetadataFetchedAt,
        };
    }

    public static ResourceSourceLink ToDomainModel(this ResourceSourceLinkDbModel dbModel)
    {
        return new ResourceSourceLink
        {
            Id = dbModel.Id,
            ResourceId = dbModel.ResourceId,
            Source = dbModel.Source,
            SourceKey = dbModel.SourceKey,
            CreateDt = dbModel.CreateDt,
            CoverUrls = StringListSerializer.Deserialize(dbModel.CoverUrls),
            LocalCoverPaths = AppDataPaths.ResolveAll(StringListSerializer.Deserialize(dbModel.LocalCoverPaths)),
            CoverDownloadFailedAt = dbModel.CoverDownloadFailedAt,
            MetadataJson = dbModel.MetadataJson,
            MetadataFetchedAt = dbModel.MetadataFetchedAt,
        };
    }
}
