using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.DateTime;

public class DateTimePropertyDescriptor : AbstractPropertyDescriptor<System.DateTime?, System.DateTime?>
{
    public override PropertyType Type => PropertyType.DateTime;

    protected override bool IsMatchInternal(System.DateTime? dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (System.DateTime) filterValue;
        return operation switch
        {
            SearchOperation.Equals => dbValue == fv,
            SearchOperation.NotEquals => dbValue != fv,
            SearchOperation.GreaterThan => dbValue > fv,
            SearchOperation.LessThan => dbValue < fv,
            SearchOperation.GreaterThanOrEquals => dbValue >= fv,
            SearchOperation.LessThanOrEquals => dbValue <= fv,
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {
            SearchOperation.Equals,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {
            SearchOperation.NotEquals,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {
            SearchOperation.GreaterThan,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {
            SearchOperation.LessThan,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {
            SearchOperation.GreaterThanOrEquals,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {
            SearchOperation.LessThanOrEquals,
            new PropertySearchOperationOptions(PropertyType.DateTime)
        },
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
    };
}