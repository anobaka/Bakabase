using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Boolean;

public class BooleanPropertyDescriptor : AbstractPropertyDescriptor<bool?, bool?>
{
    protected override bool IsMatchInternal(bool? dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (filterValue as bool?)!;
        return operation switch
        {
            SearchOperation.Equals => dbValue == fv,
            SearchOperation.NotEquals => dbValue != fv,
            _ => true
        };
    }

    public override PropertyType Type => PropertyType.Boolean;

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.Boolean)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.Boolean)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
    };
}