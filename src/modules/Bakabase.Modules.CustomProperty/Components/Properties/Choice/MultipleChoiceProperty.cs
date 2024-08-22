using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice;

public record MultipleChoiceProperty : ChoiceProperty<List<string>>;

public record MultipleChoicePropertyValue : CustomPropertyValue<List<string>>;

public class MultipleChoicePropertyDescriptor(IStandardValueHelper standardValueHelper)
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

    protected override (List<string>? DbValue, bool PropertyChanged) TypedPrepareDbValueFromBizValue(
        MultipleChoiceProperty property, List<string> bizValue)
    {
        if (bizValue.Any())
        {
            var propertyChanged =
                (property.Options ??= new ChoicePropertyOptions<List<string>>()).AddChoices(true, bizValue.ToArray());
            var stringValues = bizValue.Select(v => property.Options.Choices?.Find(c => c.Label == v)?.Value)
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
}