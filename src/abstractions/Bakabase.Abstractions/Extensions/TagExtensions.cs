using Bakabase.Abstractions.Components.Tag;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class TagExtensions
{
    public static Tag ToDomainModel(this Models.Db.Tag dbModel)
    {
        return new Tag
        {
            Id = dbModel.Id,
            Color = dbModel.Color,
            Group = dbModel.Group,
            Name = dbModel.Name,
            Order = dbModel.Order
        };
    }

    public static Models.Db.Tag ToDbModel(this Tag domainModel)
    {
        return new Models.Db.Tag
        {
            Id = domainModel.Id,
            Color = domainModel.Color,
            Group = domainModel.Group,
            Name = domainModel.Name,
            Order = domainModel.Order
        };
    }

    public static string BuildKeyString(this ITagBizKey key) =>
        string.IsNullOrEmpty(key.Group) ? key.Name : $"{key.Group}:{key.Name}";
}