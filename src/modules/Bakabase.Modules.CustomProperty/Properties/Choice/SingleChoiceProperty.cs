using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Choice;

public record SingleChoiceProperty : ChoiceProperty<string>;

public record SingleChoicePropertyValue : TypedCustomPropertyValue<string>
{
    // protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    // {
    //     throw new NotImplementedException();
    // }
}

public class SingleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<SingleChoiceProperty,
    ChoicePropertyOptions<string>, SingleChoicePropertyValue, string>
{
    public override CustomPropertyType Type => CustomPropertyType.SingleChoice;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals,
        SearchOperation.NotEquals,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
        SearchOperation.In,
        SearchOperation.NotIn
    ];

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
}