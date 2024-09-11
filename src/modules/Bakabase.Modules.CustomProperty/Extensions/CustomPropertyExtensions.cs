﻿using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Components.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Components.Properties.Formula;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Components.Properties.Number;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.Tags;
using Bakabase.Modules.CustomProperty.Components.Properties.Text;
using Bakabase.Modules.CustomProperty.Components.Properties.Time;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
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

    public static Bakabase.Abstractions.Models.Db.CustomPropertyValue ToDbModel(this CustomPropertyValue domain,
        StandardValueType valueType)
    {
        return new Bakabase.Abstractions.Models.Db.CustomPropertyValue
        {
            Id = domain.Id,
            PropertyId = domain.PropertyId,
            ResourceId = domain.ResourceId,
            Value = domain.Value?.SerializeAsStandardValue(valueType),
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

    public static bool IsReferenceValueType(this CustomPropertyType type)
    {
        return type switch
        {
            CustomPropertyType.SingleLineText or CustomPropertyType.MultilineText or CustomPropertyType.Number
                or CustomPropertyType.Percentage or CustomPropertyType.Rating or CustomPropertyType.Boolean
                or CustomPropertyType.Link or CustomPropertyType.Attachment or CustomPropertyType.Date
                or CustomPropertyType.DateTime or CustomPropertyType.Time or CustomPropertyType.Formula => false,
            CustomPropertyType.Multilevel or CustomPropertyType.Tags or CustomPropertyType.SingleChoice
                or CustomPropertyType.MultipleChoice => true,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public static void SetAllowAddingNewDataDynamically(this Abstractions.Models.CustomProperty property, bool enable)
    {
        switch (property.EnumType)
        {
            case CustomPropertyType.SingleLineText:
                break;
            case CustomPropertyType.MultilineText:
                break;
            case CustomPropertyType.SingleChoice:
            {
                if (property.Options is not ChoicePropertyOptions<string>)
                {
                    property.Options = new ChoicePropertyOptions<string>();
                }

                break;
            }
            case CustomPropertyType.MultipleChoice:
            {
                if (property.Options is not ChoicePropertyOptions<List<string>>)
                {
                    property.Options = new ChoicePropertyOptions<List<string>>();
                }

                break;
            }
            case CustomPropertyType.Number:
                break;
            case CustomPropertyType.Percentage:
                break;
            case CustomPropertyType.Rating:
                break;
            case CustomPropertyType.Boolean:
                break;
            case CustomPropertyType.Link:
                break;
            case CustomPropertyType.Attachment:
                break;
            case CustomPropertyType.Date:
                break;
            case CustomPropertyType.DateTime:
                break;
            case CustomPropertyType.Time:
                break;
            case CustomPropertyType.Formula:
                break;
            case CustomPropertyType.Multilevel:
            {
                if (property.Options is not MultilevelPropertyOptions)
                {
                    property.Options = new MultilevelPropertyOptions();
                }

                break;
            }
            case CustomPropertyType.Tags:
            {
                if (property.Options is not TagsPropertyOptions)
                {
                    property.Options = new TagsPropertyOptions();
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (property.Options is IAllowAddingNewDataDynamically options)
        {
            options.AllowAddingNewDataDynamically = enable;
        }
    }

    public static CustomPropertyType GetEnumCustomPropertyType(this Property property) =>
        (CustomPropertyType) property.CustomPropertyType;

    public static Abstractions.Models.CustomProperty ToCustomProperty(this Property property)
    {
        if (property.Type != ResourcePropertyType.Custom)
        {
            throw new Exception(
                $"Can not convert a non-custom {nameof(Property)} to {nameof(Abstractions.Models.CustomProperty)}");
        }

        // todo: make it bind to custom property descriptor
        Abstractions.Models.CustomProperty cp = property.GetEnumCustomPropertyType() switch
        {
            CustomPropertyType.SingleLineText => new SingleLineTextProperty(),
            CustomPropertyType.MultilineText => new MultilineTextProperty(),
            CustomPropertyType.SingleChoice => new SingleChoiceProperty(),
            CustomPropertyType.MultipleChoice => new MultipleChoiceProperty(),
            CustomPropertyType.Number => new NumberProperty(),
            CustomPropertyType.Percentage => new PercentageProperty(),
            CustomPropertyType.Rating => new RatingProperty(),
            CustomPropertyType.Boolean => new BooleanProperty(),
            CustomPropertyType.Link => new LinkProperty(),
            CustomPropertyType.Attachment => new AttachmentProperty(),
            CustomPropertyType.Date => new DateTimeProperty(),
            CustomPropertyType.DateTime => new DateTimeProperty(),
            CustomPropertyType.Time => new TimeProperty(),
            CustomPropertyType.Formula => new FormulaProperty(),
            CustomPropertyType.Multilevel => new MultilevelProperty(),
            CustomPropertyType.Tags => new TagsProperty(),
            _ => throw new ArgumentOutOfRangeException()
        };

        cp.Id = property.Id;
        cp.Name = property.CustomPropertyName!;
        cp.Type = property.CustomPropertyType;
        cp.DbValueType = property.DbValueType;
        cp.BizValueType = property.BizValueType;
        cp.Options = property.Options;

        return cp;
    }
}