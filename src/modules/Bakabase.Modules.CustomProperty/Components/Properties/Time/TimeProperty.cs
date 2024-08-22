using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Time;

public record TimeProperty : Abstractions.Models.CustomProperty;

public record TimePropertyValue : CustomPropertyValue<TimeSpan?>;

public class TimePropertyDescriptor(IStandardValueHelper standardValueHelper)
    : AbstractCustomPropertyDescriptor<TimeProperty, TimePropertyValue, TimeSpan?, TimeSpan?>(standardValueHelper)
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

    protected override bool IsMatch(TimeSpan? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals:
            {
                if (filterValue is not System.TimeSpan typedTarget)
                {
                    return true;
                }

                return operation switch
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