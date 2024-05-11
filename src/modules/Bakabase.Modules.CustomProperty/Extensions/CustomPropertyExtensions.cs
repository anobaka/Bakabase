using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Formula;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Time;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.CustomProperty.Extensions;

public static class CustomPropertyExtensions
{
    public static Dictionary<CustomPropertyType, ICustomPropertyDescriptor> Descriptors = new()
    {
        {CustomPropertyType.SingleLineText, new SingleLineTextPropertyDescriptor()},
        {CustomPropertyType.MultilineText, new MultilineTextPropertyDescriptor()},
        {CustomPropertyType.SingleChoice, new SingleChoicePropertyDescriptor()},
        {CustomPropertyType.MultipleChoice, new MultipleChoicePropertyDescriptor()},
        {CustomPropertyType.Number, new NumberPropertyDescriptor()},
        {CustomPropertyType.Percentage, new PercentagePropertyDescriptor()},
        {CustomPropertyType.Rating, new RatingPropertyDescriptor()},
        {CustomPropertyType.Boolean, new BooleanPropertyDescriptor()},
        {CustomPropertyType.Link, new LinkPropertyDescriptor()},
        {CustomPropertyType.Attachment, new AttachmentPropertyDescriptor()},
        {CustomPropertyType.Date, new DatePropertyDescriptor()},
        {CustomPropertyType.DateTime, new DateTimePropertyDescriptor()},
        {CustomPropertyType.Time, new TimePropertyDescriptor()},
        {CustomPropertyType.Formula, new FormulaPropertyDescriptor()},
        {CustomPropertyType.Multilevel, new MultilevelPropertyDescriptor()},
    };

    private static readonly ConcurrentDictionary<CustomPropertyType, StandardValueType> StandardValueTypeMapCache =
        new ConcurrentDictionary<CustomPropertyType, StandardValueType>(
            SpecificEnumUtils<CustomPropertyType>.Values.ToDictionary(d => d,
                d => d.GetAttribute<StandardValueAttribute>().ValueType));

    private static readonly ConcurrentDictionary<StandardValueType, CustomPropertyType[]>
        StandardValueTypeCustomPropertyTypesMapCache =
            new ConcurrentDictionary<StandardValueType, CustomPropertyType[]>(StandardValueTypeMapCache
                .GroupBy(d => d.Value).ToDictionary(c => c.Key, c => c.Select(d => d.Key).ToArray()));

    public static Models.CustomProperty? ToDomainModel(
        this Bakabase.Abstractions.Models.Db.CustomProperty? entity)
    {
        return entity == null ? null : Descriptors[(CustomPropertyType)entity.Type].BuildDomainProperty(entity);
    }

    public static StandardValueType ToStandardValueType(this CustomPropertyType type) =>
        StandardValueTypeMapCache[type];

    public static CustomPropertyType[] GetCompatibleCustomPropertyTypes(this StandardValueType type) =>
        StandardValueTypeCustomPropertyTypesMapCache[type];

    public static bool IntegratedWithAlias(this CustomPropertyType type) =>
        type is CustomPropertyType.SingleChoice or CustomPropertyType.MultipleChoice or CustomPropertyType.Multilevel
            or CustomPropertyType.SingleLineText;
}