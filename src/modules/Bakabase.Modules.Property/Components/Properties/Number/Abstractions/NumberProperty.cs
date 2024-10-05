using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Number.Abstractions;

public abstract class NumberPropertyDescriptor<TOptions> : AbstractPropertyDescriptor<TOptions, decimal?, decimal?>
    where TOptions : class, new()
{
    protected override bool IsMatchInternal(decimal? dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (decimal) filterValue;
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
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.GreaterThan, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.LessThan, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.GreaterThanOrEquals, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.LessThanOrEquals, new PropertySearchOperationOptions(PropertyType.Number)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
    };
}