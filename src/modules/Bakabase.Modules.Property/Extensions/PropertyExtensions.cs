using System.Collections.Concurrent;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.Property.Extensions;

public static class PropertyExtensions
{
    private static readonly ConcurrentDictionary<StandardValueType, PropertyType[]>
        StandardValueTypeCustomPropertyTypesMap = new ConcurrentDictionary<StandardValueType, PropertyType[]>(
            PropertyInternals.PropertyAttributeMap.GroupBy(d => d.Value.BizValueType)
                .ToDictionary(d => d.Key, d => d.Select(c => c.Key).ToArray()));

    public static StandardValueType GetDbValueType(this PropertyType type) =>
        PropertyInternals.PropertyAttributeMap.GetValueOrDefault(type)?.DbValueType ?? default;

    public static StandardValueType GetBizValueType(this PropertyType type) =>
        PropertyInternals.PropertyAttributeMap.GetValueOrDefault(type)?.BizValueType ?? default;

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
            Options = domain.Options == null ? null : JsonConvert.SerializeObject(domain.Options)
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
        };
        if (dbModel.Options.IsNotEmpty())
        {
            var pd = PropertyInternals.DescriptorMap.GetValueOrDefault(dbModel.Type);
            if (pd?.OptionsType != null)
            {
                p.Options = JsonConvert.DeserializeObject(dbModel.Options, pd.OptionsType);
            }
        }

        return p;
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
            Value = dbModel.Value?.DeserializeAsStandardValue(PropertyInternals.DescriptorMap[type].DbValueType)
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

    public static bool IsReferenceValueType(this PropertyType type) =>
        PropertyInternals.PropertyAttributeMap[type].IsReferenceValueType;

    public static void SetAllowAddingNewDataDynamically(this Bakabase.Abstractions.Models.Domain.Property property,
        bool enable)
    {
        if (property.Options?.GetType() != PropertyInternals.DescriptorMap[property.Type].OptionsType)
        {
            property.Options = PropertyInternals.DescriptorMap[property.Type].InitializeOptions();
        }

        if (property.Options is IAllowAddingNewDataDynamically options)
        {
            options.AllowAddingNewDataDynamically = enable;
        }
    }

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
        property.Type, property.Name, property.Options);

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

    public static object? GetBizValue(this Bakabase.Abstractions.Models.Domain.Property property, object? dbValue) =>
        PropertyInternals.DescriptorMap[property.Type].GetBizValue(property, dbValue);

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
            TypeName = propertyLocalizer?.PropertyTypeName(property.Type) ?? property.Type.ToString()
        };
    }
}