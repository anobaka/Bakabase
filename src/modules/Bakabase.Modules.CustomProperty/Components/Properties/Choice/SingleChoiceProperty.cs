using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice;

public record SingleChoiceProperty : ChoiceProperty<string>;

public record SingleChoicePropertyValue : CustomPropertyValue<string>
{
    // protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    // {
    //     throw new NotImplementedException();
    // }
}

public class SingleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<SingleChoiceProperty,
    ChoicePropertyOptions<string>, SingleChoicePropertyValue, string, string>
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

    protected override (string? DbValue, bool PropertyChanged) TypedPrepareDbValueFromBizValue(SingleChoiceProperty property, string bizValue)
    {
        if (!string.IsNullOrEmpty(bizValue))
        {
            var propertyChanged = (property.Options ??= new ChoicePropertyOptions<string>()).AddChoices(true, bizValue);
            var stringValue = property.Options.Choices?.Find(x => x.Label == bizValue)?.Value;
            var nv = new StringValueBuilder(stringValue).Value;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override bool IsMatch(string? id, CustomPropertyValueSearchRequestModel model)
    {
        switch (model.Operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
                {

                    var searchId = model.DeserializeValue<string>();
                    // invalid filter
                    if (string.IsNullOrEmpty(searchId))
                    {
                        return true;
                    }

                    return model.Operation == SearchOperation.Equals
                        ? string.Equals(searchId, id)
                        : !string.Equals(searchId, id);
                }
            case SearchOperation.IsNull:
                return string.IsNullOrEmpty(id);
            case SearchOperation.IsNotNull:
                return !string.IsNullOrEmpty(id);
            case SearchOperation.In:
            case SearchOperation.NotIn:
                {
                    var searchIds = model.DeserializeValue<string[]>();
                    if (searchIds?.Any() == true)
                    {
                        return model.Operation == SearchOperation.In
                            ? searchIds.Contains(id)
                            : !searchIds.Contains(id);
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
}