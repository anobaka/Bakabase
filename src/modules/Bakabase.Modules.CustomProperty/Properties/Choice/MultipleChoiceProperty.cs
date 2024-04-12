using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Choice;

public record MultipleChoiceProperty : ChoiceProperty<string[]>;

public record MultipleChoicePropertyValue : TypedCustomPropertyValue<string[]>
{
    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public class MultipleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<MultipleChoiceProperty,
    ChoicePropertyOptions<string[]>, MultipleChoicePropertyValue, string[]>
{
    public override CustomPropertyType Type => CustomPropertyType.MultipleChoice;

    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}