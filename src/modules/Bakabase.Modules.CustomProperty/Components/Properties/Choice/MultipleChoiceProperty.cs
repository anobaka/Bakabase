using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Components.Properties.Tags;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice;

public record MultipleChoiceProperty : ChoiceProperty<List<string>>;

public record MultipleChoicePropertyValue : CustomPropertyValue<List<string>>;

public class MultipleChoicePropertyDescriptor(
    IStandardValueHelper standardValueHelper,
    IStandardValueHandlers standardValueHandlers)
    : AbstractCustomPropertyDescriptor<MultipleChoiceProperty,
        ChoicePropertyOptions<List<string>>, MultipleChoicePropertyValue, List<string>, List<string>>(
        standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.MultipleChoice;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.In,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull
    ];

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeyword(
        MultipleChoiceProperty property, string keyword)
    {
        var ids = property.Options?.Choices?.Where(c => c.Label.Contains(keyword)).Select(x => x.Value).ToList();
        return ids?.Any() == true ? (ids, SearchOperation.In) : null;
    }

    protected override (List<string>? DbValue, bool PropertyChanged) PrepareDbValueFromBizValueInternal(
        MultipleChoiceProperty property, List<string> bizValue)
    {
        var goodValues = bizValue.TrimAndRemoveEmpty();
        if (goodValues?.Any() == true)
        {
            var propertyChanged =
                (property.Options ??= new ChoicePropertyOptions<List<string>>() {AllowAddingNewDataDynamically = true})
                .AddChoices(true, goodValues.ToArray(),
                    null);
            var stringValues = goodValues.Select(v => property.Options.Choices?.Find(c => c.Label == v)?.Value)
                .OfType<string>().ToList();
            var nv = stringValues.Any() ? new ListStringValueBuilder(stringValues).Value : null;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override bool IsMatch(List<string>? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
            {
                var typedTarget = filterValue as List<string>;
                if (typedTarget?.Any() != true)
                {
                    return true;
                }

                return operation switch
                {
                    SearchOperation.Contains => typedTarget.All(target => value?.Contains(target) == true),
                    SearchOperation.NotContains => typedTarget.All(target => value?.Contains(target) != true),
                    _ => false
                };
            }
            case SearchOperation.In:
            {
                var typedTarget = filterValue as List<string>;
                if (typedTarget?.Any() != true)
                {
                    return true;
                }

                return typedTarget.Any(target => value?.Contains(target) == true);
            }
            case SearchOperation.IsNull:
                break;
            case SearchOperation.IsNotNull:
                break;
        }

        return true;
    }

    protected override List<string>? TypedConvertDbValueToBizValue(MultipleChoiceProperty property, List<string> value)
    {
        return value.Select(v => property.Options?.Choices?.FirstOrDefault(c => c.Value == v)?.Label ?? v).ToList();
    }

    // protected override async Task<object?> TypedConvertOptions(ChoicePropertyOptions<List<string>> current,
    //     CustomPropertyType newType)
    // {
    //     switch (newType)
    //     {
    //         case CustomPropertyType.SingleLineText:
    //         case CustomPropertyType.MultilineText:
    //             return null;
    //         case CustomPropertyType.SingleChoice:
    //         {
    //             return new ChoicePropertyOptions<string>
    //             {
    //                 AllowAddingNewDataDynamically = current.AllowAddingNewDataDynamically,
    //                 Choices = current.Choices?.Select(c => new ChoicePropertyOptions<string>.ChoiceOptions
    //                     {Color = c.Color, Label = c.Label, Value = c.Value}).ToList(),
    //                 DefaultValue = current.DefaultValue?.FirstOrDefault()
    //             };
    //         }
    //         case CustomPropertyType.MultipleChoice:
    //             return current;
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
    //                 DefaultValue = current.DefaultValue?.FirstOrDefault(),
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