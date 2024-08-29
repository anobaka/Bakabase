using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Components.Properties.Tags;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice;

public record SingleChoiceProperty : ChoiceProperty<string>;

public record SingleChoicePropertyValue : CustomPropertyValue<string>
{
    // protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    // {
    //     throw new NotImplementedException();
    // }
}

public class SingleChoicePropertyDescriptor(
    IStandardValueHelper standardValueHelper,
    IStandardValueHandlers standardValueHandlers)
    : AbstractCustomPropertyDescriptor<SingleChoiceProperty,
        ChoicePropertyOptions<string>, SingleChoicePropertyValue, string, string>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.SingleChoice;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals,
        SearchOperation.NotEquals,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
        SearchOperation.In,
        SearchOperation.NotIn
    ];

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeyword(
        SingleChoiceProperty property, string keyword)
    {
        var ids = property.Options?.Choices?.Where(c => c.Label.Contains(keyword)).Select(x => x.Value).ToList();
        return ids?.Any() == true ? (ids, SearchOperation.In) : null;
    }

    protected override (string? DbValue, bool PropertyChanged) TypedPrepareDbValueFromBizValue(
        SingleChoiceProperty property, string bizValue)
    {
        if (!string.IsNullOrEmpty(bizValue))
        {
            var propertyChanged = (property.Options ??= new ChoicePropertyOptions<string>()).AddChoices(true, [bizValue], null);
            var stringValue = property.Options.Choices?.Find(x => x.Label == bizValue)?.Value;
            var nv = new StringValueBuilder(stringValue).Value;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override bool IsMatch(string? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            {
                var searchId = filterValue as string;
                // invalid filter
                if (string.IsNullOrEmpty(searchId))
                {
                    return true;
                }

                return operation == SearchOperation.Equals
                    ? string.Equals(searchId, value)
                    : !string.Equals(searchId, value);
            }
            case SearchOperation.IsNull:
                return string.IsNullOrEmpty(value);
            case SearchOperation.IsNotNull:
                return !string.IsNullOrEmpty(value);
            case SearchOperation.In:
            case SearchOperation.NotIn:
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                var searchIds = filterValue as List<string>;
                if (searchIds?.Any() == true)
                {
                    return operation == SearchOperation.In
                        ? searchIds.Contains(value)
                        : !searchIds.Contains(value);
                }

                // invalid filter
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    protected override string? TypedConvertDbValueToBizValue(SingleChoiceProperty property, string value)
    {
        return property.Options?.Choices?.FirstOrDefault(c => c.Value == value)?.Label;
    }

    // protected override async Task<object?> TypedConvertOptions(ChoicePropertyOptions<string> current,
    //     CustomPropertyType newType)
    // {
    //     switch (newType)
    //     {
    //         case CustomPropertyType.SingleLineText:
    //         case CustomPropertyType.MultilineText:
    //             return null;
    //         case CustomPropertyType.SingleChoice:
    //             return current;
    //         case CustomPropertyType.MultipleChoice:
    //         {
    //             return new ChoicePropertyOptions<List<string>>
    //             {
    //                 AllowAddingNewDataDynamically = current.AllowAddingNewDataDynamically,
    //                 Choices = current.Choices?.Select(c => new ChoicePropertyOptions<List<string>>.ChoiceOptions
    //                     {Color = c.Color, Label = c.Label, Value = c.Value}).ToList(),
    //                 DefaultValue = current.DefaultValue.IsNullOrEmpty() ? null : [current.DefaultValue]
    //             };
    //         }
    //         case CustomPropertyType.Number:
    //         case CustomPropertyType.Percentage:
    //         case CustomPropertyType.Rating:
    //         case CustomPropertyType.Boolean:
    //         case CustomPropertyType.Link:
    //         case CustomPropertyType.Attachment:
    //         case CustomPropertyType.Date:
    //         case CustomPropertyType.DateTime:
    //         case CustomPropertyType.Time:
    //         case CustomPropertyType.Formula:
    //             return null;
    //         case CustomPropertyType.Multilevel:
    //         {
    //             var data = current.Choices?.Select(c => c.Label).ToList();
    //             var multipleData = (await standardValueHandlers[BizValueType]
    //                 .Convert<List<List<string>>>(data, StandardValueType.ListListString)).NewValue?.Select((x, i) =>
    //                 new MultilevelDataOptions
    //                 {
    //                     Value = current.Choices![i].Value,
    //                     Color = current.Choices![i].Color,
    //                     Label = x[0]
    //                 }).ToList();
    //             return new MultilevelPropertyOptions()
    //             {
    //                 AllowAddingNewDataDynamically = current.AllowAddingNewDataDynamically,
    //                 DefaultValue = current.DefaultValue,
    //                 Data = multipleData
    //             };
    //         }
    //         case CustomPropertyType.Tags:
    //         {
    //             var data = current.Choices?.Select(c => c.Label).ToList();
    //             var tags = (await standardValueHandlers[BizValueType]
    //                 .Convert<List<TagValue>>(data, StandardValueType.ListTag)).NewValue;
    //             return new TagsPropertyOptions
    //             {
    //                 AllowAddingNewDataDynamically = current.AllowAddingNewDataDynamically,
    //                 Tags = tags?.Select((c, i) => new TagsPropertyOptions.TagOptions(c.Group, c.Name)
    //                     {Value = current.Choices![i].Value}).ToList()
    //             };
    //         }
    //         default:
    //             throw new ArgumentOutOfRangeException();
    //     }
    // }
}