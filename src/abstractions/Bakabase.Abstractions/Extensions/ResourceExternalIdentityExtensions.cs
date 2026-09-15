using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceExternalIdentityExtensions
{
    public static ResourceExternalIdentityDbModel ToDbModel(this ResourceExternalIdentity model) => new()
    {
        Id = model.Id,
        ResourceId = model.ResourceId,
        ThirdPartyId = model.ThirdPartyId,
        ExternalId = model.ExternalId,
        CreateDt = model.CreateDt,
        CoverUrls = StringListSerializer.Serialize(model.CoverUrls),
        LocalCoverPaths = StringListSerializer.Serialize(AppDataPaths.RelativizeAll(model.LocalCoverPaths)),
        CoverDownloadFailedAt = model.CoverDownloadFailedAt,
        MetadataJson = model.MetadataJson
    };

    public static ResourceExternalIdentity ToDomainModel(this ResourceExternalIdentityDbModel model) => new()
    {
        Id = model.Id,
        ResourceId = model.ResourceId,
        ThirdPartyId = model.ThirdPartyId,
        ExternalId = model.ExternalId,
        CreateDt = model.CreateDt,
        CoverUrls = StringListSerializer.Deserialize(model.CoverUrls),
        LocalCoverPaths = AppDataPaths.ResolveAll(StringListSerializer.Deserialize(model.LocalCoverPaths)),
        CoverDownloadFailedAt = model.CoverDownloadFailedAt,
        MetadataJson = model.MetadataJson
    };
}
