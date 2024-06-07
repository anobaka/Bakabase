using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Boolean;

public record BooleanProperty() : Models.CustomProperty;
public record BooleanPropertyValue : CustomPropertyValue<bool>
{
}

public class BooleanPropertyDescriptor : AbstractCustomPropertyDescriptor<BooleanProperty, BooleanPropertyValue,
    bool, bool>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Boolean;

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