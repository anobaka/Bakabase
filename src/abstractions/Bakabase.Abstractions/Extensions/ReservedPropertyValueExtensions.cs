using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class ReservedPropertyValueExtensions
{
    public static ReservedPropertyValue ToDomainModel(this Models.Db.ReservedPropertyValue dbModel)
    {
        return new ReservedPropertyValue
        {
            Id = dbModel.Id,
            Introduction = dbModel.Introduction,
            Rating = dbModel.Rating,
            ResourceId = dbModel.ResourceId,
            Scope = dbModel.Scope,
        };
    }

    public static Models.Db.ReservedPropertyValue ToDbModel(this ReservedPropertyValue domainModel)
    {
        return new Models.Db.ReservedPropertyValue
        {
            Id = domainModel.Id,
            Introduction = domainModel.Introduction,
            Rating = domainModel.Rating,
            ResourceId = domainModel.ResourceId,
            Scope = domainModel.Scope,
        };
    }
}