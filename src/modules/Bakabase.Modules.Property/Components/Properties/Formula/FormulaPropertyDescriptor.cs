using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Formula;

public class FormulaPropertyDescriptor : AbstractPropertyDescriptor<string, string>
{
    public override PropertyType Type => PropertyType.Formula;

    protected override bool IsMatchInternal(string dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (string) filterValue;
        return operation switch
        {
            SearchOperation.Equals => dbValue.Equals(fv, StringComparison.OrdinalIgnoreCase),
            SearchOperation.NotEquals => !dbValue.Equals(fv, StringComparison.OrdinalIgnoreCase),
            SearchOperation.Contains => dbValue.Contains(fv, StringComparison.OrdinalIgnoreCase),
            SearchOperation.NotContains => !dbValue.Contains(fv, StringComparison.OrdinalIgnoreCase),
            SearchOperation.Matches => Regex.IsMatch(dbValue, fv),
            SearchOperation.NotMatches => !Regex.IsMatch(dbValue, fv),
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
        {SearchOperation.Matches, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotMatches, new PropertySearchOperationOptions(PropertyType.SingleLineText)}
    };
}