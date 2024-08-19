using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Boolean;

public record BooleanProperty() : Models.CustomProperty;
public record BooleanPropertyValue : CustomPropertyValue<bool?>
{
}

public class BooleanPropertyDescriptor(IStandardValueHelper standardValueHelper) : AbstractCustomPropertyDescriptor<BooleanProperty, BooleanPropertyValue,
    bool?, bool?>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Boolean;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals, SearchOperation.NotEquals, SearchOperation.IsNull, SearchOperation.IsNotNull
    ];

    protected override bool IsMatch(bool? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            {
                if (filterValue is not bool b)
                {
                    return true;
                }
                return operation == SearchOperation.Equals ? value == b : value != b;
            }
            case SearchOperation.IsNull:
                return false;
            case SearchOperation.IsNotNull:
                return true;
        }

        return true;
    }
}