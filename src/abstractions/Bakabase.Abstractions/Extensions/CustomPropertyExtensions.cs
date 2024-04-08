using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class CustomPropertyExtensions
{
    public static Models.Db.CustomProperty? ToDbModel<T>(this CustomProperty<T>? domain)
    {
        if (domain == null)
        {
            return null;
        }

        return new Models.Db.CustomProperty
        {
            CreatedAt = domain.CreatedAt,
            Name = domain.Name,
            Id = domain.Id,
            Type = domain.Type,
            Options = domain.Options == null ? null : JsonConvert.SerializeObject(domain.Options)
        };
    }

    public static Models.Db.CustomPropertyValue? ToDbModel(this CustomPropertyValue? domain)
    {
        if (domain == null)
        {
            return null;
        }

        return new Models.Db.CustomPropertyValue
        {
            Id = domain.Id,
            PropertyId = domain.PropertyId,
            ResourceId = domain.ResourceId,
            Value = domain.Value == null ? null : JsonConvert.SerializeObject(domain.Value)
        };
    }
}