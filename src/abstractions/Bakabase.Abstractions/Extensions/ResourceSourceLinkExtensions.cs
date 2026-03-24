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
            CreateDt = model.CreateDt
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
            CreateDt = dbModel.CreateDt
        };
    }
}
