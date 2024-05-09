using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Choice;

public record MultipleChoiceProperty : ChoiceProperty<List<string>>;

public record MultipleChoicePropertyValue : CustomPropertyValue<List<string>>;

public class MultipleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<MultipleChoiceProperty,
    ChoicePropertyOptions<List<string>>, MultipleChoicePropertyValue, List<string>>
{
    public override CustomPropertyType EnumType => CustomPropertyType.MultipleChoice;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull
    ];

    protected override bool IsMatch(List<string>? value, CustomPropertyValueSearchRequestModel model)
    {
        switch (model.Operation)
        {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
            {
                var typedTarget = model.DeserializeValue<List<string>>();
                if (typedTarget?.Any() != true)
                {
                    return true;
                }

                return model.Operation switch
                {
                    SearchOperation.Contains => typedTarget.All(target => value?.Contains(target) == true),
                    SearchOperation.NotContains => typedTarget.All(target => value?.Contains(target) != true),
                    _ => true
                };
            }
            case SearchOperation.IsNull:
                break;
            case SearchOperation.IsNotNull:
                break;
        }

        return true;
    }

    protected override object? BuildValueForDisplay(MultipleChoiceProperty property, List<string> value)
    {
        return value.Select(v => property.Options?.Choices?.FirstOrDefault(c => c.Value == v)?.Label ?? v).ToList();
    }
}