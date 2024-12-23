using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Property.Extensions;

public static class ReservedPropertyValueExtensions
{
    public static ReservedPropertyValue ToDomainModel(
        this Bakabase.Abstractions.Models.Db.ReservedPropertyValue dbModel)
    {
        return new ReservedPropertyValue
        {
            Id = dbModel.Id,
            Introduction = dbModel.Introduction,
            Rating = dbModel.Rating,
            ResourceId = dbModel.ResourceId,
            Scope = dbModel.Scope,
            CoverPaths = dbModel.CoverPaths?.DeserializeBizValueAsStandardValue<List<string>?>(PropertyType.Attachment)
        };
    }

    public static Bakabase.Abstractions.Models.Db.ReservedPropertyValue ToDbModel(
        this ReservedPropertyValue domainModel)
    {
        return new Bakabase.Abstractions.Models.Db.ReservedPropertyValue
        {
            Id = domainModel.Id,
            Introduction = domainModel.Introduction,
            Rating = domainModel.Rating,
            ResourceId = domainModel.ResourceId,
            Scope = domainModel.Scope,
            CoverPaths = domainModel.CoverPaths?.SerializeDbValueAsStandardValue(PropertyType.Attachment)
        };
    }
}