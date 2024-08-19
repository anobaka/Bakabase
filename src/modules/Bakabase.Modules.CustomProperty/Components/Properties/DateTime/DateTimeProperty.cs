using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.DateTime;
public record DateTimeProperty : Models.CustomProperty;
public record DateTimePropertyValue : CustomPropertyValue<System.DateTime?>;

public class
    DateTimePropertyDescriptor(IStandardValueHelper standardValueHelper)
    : AbstractCustomPropertyDescriptor<DateTimeProperty, DateTimePropertyValue,
        System.DateTime?, System.DateTime?>(standardValueHelper)
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

    protected override bool IsMatch(System.DateTime? value, SearchOperation operation, object? filterValue)
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
                if (filterValue is not System.DateTime dt)
                {
                    return true;
                }

                return operation switch
                {
                    SearchOperation.Equals => value == dt,
                    SearchOperation.NotEquals => value != dt,
                    SearchOperation.GreaterThan => value > dt,
                    SearchOperation.LessThan => value < dt,
                    SearchOperation.GreaterThanOrEquals => value >= dt,
                    SearchOperation.LessThanOrEquals => value <= dt,
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