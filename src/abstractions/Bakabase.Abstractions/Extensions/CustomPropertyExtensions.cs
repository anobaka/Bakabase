using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class CustomPropertyExtensions
{
    public static Models.Db.CustomProperty? ToDbModel<T>(this CustomProperty<T>? dto)
    {
        if (dto == null)
        {
            return null;
        }

        return new Models.Db.CustomProperty
        {
            CreatedAt = dto.CreatedAt,
            Name = dto.Name,
            Id = dto.Id,
            Type = dto.Type,
            Options = dto.Options == null ? null : JsonConvert.SerializeObject(dto.Options)
        };
    }
}