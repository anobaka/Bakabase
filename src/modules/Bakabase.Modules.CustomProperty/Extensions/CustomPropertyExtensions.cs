using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Extensions;

public static class CustomPropertyExtensions
{
    private static readonly ConcurrentDictionary<CustomPropertyType, CustomPropertyAttribute>
        CustomPropertyAttributeMap =
            new(SpecificEnumUtils<CustomPropertyType>.Values.ToDictionary(d => d,
                d => d.GetAttribute<CustomPropertyAttribute>()));

    private static readonly ConcurrentDictionary<StandardValueType, CustomPropertyType[]>
        StandardValueTypeCustomPropertyTypesMap = new ConcurrentDictionary<StandardValueType, CustomPropertyType[]>(
            CustomPropertyAttributeMap.GroupBy(d => d.Value.BizValueType)
                .ToDictionary(d => d.Key, d => d.Select(c => c.Key).ToArray()));

    public static StandardValueType GetDbValueType(this CustomPropertyType type) =>
        CustomPropertyAttributeMap[type].DbValueType;

    public static StandardValueType GetBizValueType(this CustomPropertyType type) =>
        CustomPropertyAttributeMap[type].BizValueType;

    public static CustomPropertyType[]? GetCompatibleCustomPropertyTypes(this StandardValueType bizValueType) =>
        StandardValueTypeCustomPropertyTypesMap.GetValueOrDefault(bizValueType);

    public static bool IntegratedWithAlias(this CustomPropertyType type) =>
        type is CustomPropertyType.SingleChoice or CustomPropertyType.MultipleChoice or CustomPropertyType.Multilevel
            or CustomPropertyType.SingleLineText;

    public static Bakabase.Abstractions.Models.Db.CustomProperty ToDbModel(
        this Bakabase.Abstractions.Models.Domain.CustomProperty domain)
    {
        return new Bakabase.Abstractions.Models.Db.CustomProperty
        {
            CreatedAt = domain.CreatedAt,
            Name = domain.Name,
            Id = domain.Id,
            Type = domain.Type,
            Options = domain.Options == null ? null : JsonConvert.SerializeObject(domain.Options)
        };
    }

    public static Bakabase.Abstractions.Models.Db.CustomPropertyValue ToDbModel(this CustomPropertyValue domain)
    {
        return new Bakabase.Abstractions.Models.Db.CustomPropertyValue
        {
            Id = domain.Id,
            PropertyId = domain.PropertyId,
            ResourceId = domain.ResourceId,
            Value = CustomPropertyValueHelper.SerializeValue(domain.Value),
            Scope = domain.Scope
        };
    }

    public static TProperty Cast<TProperty>(this Bakabase.Abstractions.Models.Domain.CustomProperty toProperty)
    {
        if (toProperty is not TProperty typedProperty)
        {
            throw new DevException($"Can not cast {nameof(toProperty)} to {toProperty.Type}");
        }

        return typedProperty;
    }

    public static string? SerializeAsCustomPropertyOptions(this object? options, bool throwOnError = false)
    {
        try
        {
            return options == null ? null : JsonConvert.SerializeObject(options);
        }
        catch (Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return null;
        }
    }

    public static T? DeserializeAsCustomPropertyOptions<T>(this string options, bool throwOnError = false)
        where T : class
    {
        try
        {
            return string.IsNullOrEmpty(options) ? null : JsonConvert.DeserializeObject<T>(options);
        }
        catch (Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return null;
        }
    }
}