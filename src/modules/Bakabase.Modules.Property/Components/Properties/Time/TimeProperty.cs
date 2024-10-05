using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Time;

public class TimePropertyDescriptor : AbstractPropertyDescriptor<TimeSpan?, TimeSpan?>
{
    public override PropertyType Type => PropertyType.Time;

    protected override bool IsMatchInternal(TimeSpan? dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (TimeSpan) filterValue;
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

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.GreaterThan, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.LessThan, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.GreaterThanOrEquals, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.LessThanOrEquals, new PropertySearchOperationOptions(PropertyType.Time)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
    };
}