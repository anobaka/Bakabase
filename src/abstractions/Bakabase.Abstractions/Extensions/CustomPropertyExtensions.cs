using System.Collections.Concurrent;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class CustomPropertyExtensions
{
    public static Models.Db.CustomProperty? ToDbModel(this CustomProperty? domain)
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
            Value = Models.Db.CustomPropertyValue.SerializeValue(domain.Value),
            Scope = domain.Scope
        };
    }

    public static TProperty Cast<TProperty>(this CustomProperty toProperty)
    {
        if (toProperty is not TProperty typedProperty)
        {
            throw new DevException($"Can not cast {nameof(toProperty)} to {toProperty.Type}");
        }

        return typedProperty;
    }
}