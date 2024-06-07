using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Time;

public record TimeProperty : Models.CustomProperty;

public record TimePropertyValue : CustomPropertyValue<TimeSpan>;

public class TimePropertyDescriptor : AbstractCustomPropertyDescriptor<TimeProperty, TimePropertyValue, TimeSpan, TimeSpan>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Time;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals,
        SearchOperation.NotEquals,
        SearchOperation.GreaterThan,
        SearchOperation.LessThan,
        SearchOperation.GreaterThanOrEquals,
        SearchOperation.LessThanOrEquals,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull
    ];

    protected override bool IsMatch(TimeSpan value, CustomPropertyValueSearchRequestModel model)
    {
        switch (model.Operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals:
                {
                    var typedTarget = model.DeserializeValue<TimeSpan>();
                    return model.Operation switch
                    {
                        SearchOperation.Equals => value == typedTarget,
                        SearchOperation.NotEquals => value != typedTarget,
                        SearchOperation.GreaterThan => value > typedTarget,
                        SearchOperation.LessThan => value < typedTarget,
                        SearchOperation.GreaterThanOrEquals => value >= typedTarget,
                        SearchOperation.LessThanOrEquals => value <= typedTarget,
                        _ => true
                    };
                }
            case SearchOperation.IsNull:
                return false;
            case SearchOperation.IsNotNull:
                return true;
        }

        return true;
    }
}