using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.DateTime;
public record DateTimeProperty : Models.CustomProperty;
public record DateTimePropertyValue : CustomPropertyValue<System.DateTime>;

public class
    DateTimePropertyDescriptor : AbstractCustomPropertyDescriptor<DateTimeProperty, DateTimePropertyValue,
    System.DateTime>
{
    public override CustomPropertyType EnumType => CustomPropertyType.DateTime;

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

    protected override bool IsMatch(System.DateTime value, CustomPropertyValueSearchRequestModel model)
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
                var typedTarget = model.DeserializeValue<System.DateTime>();
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