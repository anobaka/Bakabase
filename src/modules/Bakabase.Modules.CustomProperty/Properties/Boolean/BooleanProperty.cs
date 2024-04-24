using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Properties.Boolean;

public record BooleanProperty(): Abstractions.Models.Domain.CustomProperty;
public record BooleanPropertyValue : CustomPropertyValue<bool>
{
}

public class BooleanPropertyDescriptor : AbstractCustomPropertyDescriptor<BooleanProperty, BooleanPropertyValue,
    bool>
{
    public override StandardValueType ValueType => StandardValueType.Boolean;
    public override CustomPropertyType Type => CustomPropertyType.Boolean;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals, SearchOperation.NotEquals, SearchOperation.IsNull, SearchOperation.IsNotNull
    ];

    protected override bool IsMatch(bool value, CustomPropertyValueSearchRequestModel model)
    {
        switch (model.Operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            {
                var typedTarget = model.DeserializeValue<bool>();
                return model.Operation == SearchOperation.Equals ? value == typedTarget : value != typedTarget;
            }
            case SearchOperation.IsNull:
                return false;
            case SearchOperation.IsNotNull:
                return true;
        }

        return true;
    }
}