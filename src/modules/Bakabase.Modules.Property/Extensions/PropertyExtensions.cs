using System.Collections.Concurrent;
using System.Text.Json;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Bakabase.Modules.Property.Extensions;

public static class PropertyExtensions
{
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly ConcurrentDictionary<StandardValueType, PropertyType[]>
        StandardValueTypeCustomPropertyTypesMap = new(
            Enum.GetValues<PropertyType>()
                .Select(t => (Type: t, Attr: PropertySystem.Property.TryGetAttribute(t)))
                .Where(x => x.Attr != null)
                .GroupBy(x => x.Attr!.BizValueType)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Type).ToArray()));

    /// <summary>
    /// Get the DB value type for a PropertyType.
    /// </summary>
    public static StandardValueType GetDbValueType(this PropertyType type) =>
        PropertySystem.Property.TryGetAttribute(type)?.DbValueType ?? default;

    /// <summary>
    /// Get the Biz value type for a PropertyType.
    /// </summary>
    public static StandardValueType GetBizValueType(this PropertyType type) =>
        PropertySystem.Property.TryGetAttribute(type)?.BizValueType ?? default;

    public static PropertyType[]? GetCompatibleCustomPropertyTypes(this StandardValueType bizValueType) =>
        StandardValueTypeCustomPropertyTypesMap.GetValueOrDefault(bizValueType);

    public static bool IntegratedWithAlias(this PropertyType type) =>
        type is PropertyType.SingleChoice or PropertyType.MultipleChoice or PropertyType.Multilevel
            or PropertyType.SingleLineText;

    public static CustomPropertyDbModel ToDbModel(this CustomProperty domain)
    {
        return new CustomPropertyDbModel
        {
            CreatedAt = domain.CreatedAt,
            Name = domain.Name,
            Id = domain.Id,
            Type = domain.Type,
            Options = domain.Options == null ? null : JsonConvert.SerializeObject(domain.Options),
            Order = domain.Order
        };
    }

    public static CustomProperty ToDomainModel(this CustomPropertyDbModel dbModel)
    {
        var p = new CustomProperty
        {
            Id = dbModel.Id,
            CreatedAt = dbModel.CreatedAt,
            Name = dbModel.Name,
            Type = dbModel.Type,
            ValueCount = null,
            Categories = null,
            Order = dbModel.Order
        };
        if (dbModel.Options.IsNotEmpty())
        {
            var pd = PropertySystem.Property.TryGetDescriptor(dbModel.Type);
            if (pd?.OptionsType != null)
            {
                p.Options = System.Text.Json.JsonSerializer.Deserialize(dbModel.Options, pd.OptionsType);
            }
        }

        return p;
    }

    /// <summary>
    /// Batch convert DbModels to DomainModels with optimized Options deserialization
    /// </summary>
    public static List<CustomProperty> ToDomainModelsBatch(this IEnumerable<CustomPropertyDbModel> dbModels)
    {
        var dbModelList = dbModels.ToList();
        var result = new List<CustomProperty>(dbModelList.Count);

        // Group by Type to batch deserialize options
        var groupedByType = dbModelList.GroupBy(d => d.Type).ToList();

        foreach (var typeGroup in groupedByType)
        {
            var propertyType = typeGroup.Key;
            var descriptor = PropertySystem.Property.TryGetDescriptor(propertyType);

            foreach (var dbModel in typeGroup)
            {
                var p = new CustomProperty
                {
                    Id = dbModel.Id,
                    CreatedAt = dbModel.CreatedAt,
                    Name = dbModel.Name,
                    Type = dbModel.Type,
                    ValueCount = null,
                    Categories = null,
                    Order = dbModel.Order
                };

                // Deserialize options if present (using System.Text.Json for better performance)
                if (dbModel.Options.IsNotEmpty() && descriptor?.OptionsType != null)
                {
                    p.Options = JsonSerializer.Deserialize(dbModel.Options, descriptor.OptionsType, CamelCaseJsonOptions);
                }

                result.Add(p);
            }
        }

        return result;
    }

    public static CustomPropertyValueDbModel ToDbModel(this CustomPropertyValue domain,
        StandardValueType valueType)
    {
        return new CustomPropertyValueDbModel
        {
            Id = domain.Id,
            PropertyId = domain.PropertyId,
            ResourceId = domain.ResourceId,
            Value = domain.Value?.SerializeAsStandardValue(valueType),
            Scope = domain.Scope
        };
    }

    public static CustomPropertyValue ToDomainModel(this CustomPropertyValueDbModel dbModel,
        PropertyType type)
    {
        return new CustomPropertyValue
        {
            Id = dbModel.Id,
            BizValue = null,
            Property = null,
            PropertyId = dbModel.PropertyId,
            ResourceId = dbModel.ResourceId,
            Scope = dbModel.Scope,
            Value = dbModel.Value?.DeserializeAsStandardValue(PropertySystem.Property.GetDescriptor(type).DbValueType)
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

    /// <summary>
    /// Check if a PropertyType is a reference value type (stores UUIDs).
    /// </summary>
    [Obsolete("Use PropertySystem.Property.IsReferenceValueType() instead.")]
    public static bool IsReferenceValueType(this PropertyType type) =>
        PropertySystem.Property.IsReferenceValueType(type);

    // public static void SetAllowAddingNewDataDynamically(this Bakabase.Abstractions.Models.Domain.Property property,
    //     bool enable)
    // {
    //     if (property.Options?.GetType() != PropertyInternals.DescriptorMap[property.Type].OptionsType)
    //     {
    //         property.Options = PropertyInternals.DescriptorMap[property.Type].InitializeOptions();
    //     }
    //
    //     if (property.Options is IAllowAddingNewDataDynamically options)
    //     {
    //         options.AllowAddingNewDataDynamically = enable;
    //     }
    // }

    public static CustomProperty ToCustomProperty(this Bakabase.Abstractions.Models.Domain.Property property)
    {
        if (property.Pool != PropertyPool.Custom)
        {
            throw new Exception(
                $"Can not convert a non-custom {nameof(Bakabase.Abstractions.Models.Domain.Property)} to {nameof(CustomProperty)}");
        }

        var cp = new CustomProperty
        {
            Id = property.Id,
            Name = property.Name!,
            Type = property.Type,
            Options = property.Options
        };

        return cp;
    }

    public static Bakabase.Abstractions.Models.Domain.Property ToProperty(this CustomProperty property) => new(
        PropertyPool.Custom, property.Id,
        property.Type, property.Name, property.Options, property.Order);

    public static CustomPropertyValue InitializeCustomPropertyValue(this PropertyType type, object? dbValue,
        int resourceId, int propertyId, int scope)
    {
        return new CustomPropertyValue
        {
            PropertyId = propertyId,
            ResourceId = resourceId,
            Scope = scope,
            Value = dbValue.IsStandardValueType(type.GetDbValueType()) ? dbValue : null
        };
    }

    /// <summary>
    /// Convert DbValue to BizValue for a property.
    /// </summary>
    [Obsolete("Use PropertySystem.Property.ToBizValue() instead.")]
    public static object? GetBizValue(this Bakabase.Abstractions.Models.Domain.Property property, object? dbValue) =>
        PropertySystem.Property.ToBizValue(property, dbValue);

    /// <summary>
    /// Convert DbValue to typed BizValue for a property.
    /// </summary>
    [Obsolete("Use PropertySystem.Property.ToBizValue<TBizValue>() instead.")]
    public static TBizValue? GetBizValue<TBizValue>(this Bakabase.Abstractions.Models.Domain.Property property,
        object? dbValue) => GetBizValue(property, dbValue) is TBizValue bv ? bv : default;

    public static PropertyViewModel ToViewModel(this Bakabase.Abstractions.Models.Domain.Property property,
        IPropertyLocalizer? propertyLocalizer = null)
    {
        return new PropertyViewModel
        {
            Id = property.Id,
            Name = property.Name,
            Options = property.Options,
            Pool = property.Pool,
            PoolName = propertyLocalizer?.PropertyPoolName(property.Pool) ?? property.Pool.ToString(),
            Type = property.Type,
            TypeName = propertyLocalizer?.PropertyTypeName(property.Type) ?? property.Type.ToString(),
            Order = property.Order
        };
    }
}