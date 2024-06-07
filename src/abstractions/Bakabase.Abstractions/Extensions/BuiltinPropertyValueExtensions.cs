using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class BuiltinPropertyValueExtensions
{
    public static BuiltinPropertyValue ToDomainModel(this Models.Db.BuiltinPropertyValue dbModel)
    {
        return new BuiltinPropertyValue
        {
            Id = dbModel.Id,
            Introduction = dbModel.Introduction,
            Rating = dbModel.Rating,
            ResourceId = dbModel.ResourceId,
            Scope = dbModel.Scope,
        };
    }

    public static Models.Db.BuiltinPropertyValue ToDbModel(this BuiltinPropertyValue domainModel)
    {
        return new Models.Db.BuiltinPropertyValue
        {
            Id = domainModel.Id,
            Introduction = domainModel.Introduction,
            Rating = domainModel.Rating,
            ResourceId = domainModel.ResourceId,
            Scope = domainModel.Scope,
        };
    }
}