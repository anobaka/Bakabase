using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Text.Abstractions;

public abstract class TextPropertyDescriptor<TDbValue, TBizValue> : AbstractPropertyDescriptor<TDbValue, TBizValue>
{
    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(Bakabase.Abstractions.Models.Domain.Property property, string keyword)
    {
        return (keyword, SearchOperation.Contains);
    }

    protected override bool IsMatchInternal(TDbValue dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (string) filterValue;
        var matchSources = GetMatchSources(dbValue);
        return matchSources.Any(s => operation switch
            {
                SearchOperation.Equals => s.Equals(fv),
                SearchOperation.NotEquals => !s.Equals(fv),
                SearchOperation.Contains => s.Contains(fv),
                SearchOperation.NotContains => !s.Contains(fv),
                SearchOperation.StartsWith => s.StartsWith(fv),
                SearchOperation.NotStartsWith => !s.StartsWith(fv),
                SearchOperation.EndsWith => s.EndsWith(fv),
                SearchOperation.NotEndsWith => !s.EndsWith(fv),
                SearchOperation.Matches => Regex.IsMatch(s, fv),
                SearchOperation.NotMatches => !Regex.IsMatch(s, fv),
                _ => true
            }
        );
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.StartsWith, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotStartsWith, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.EndsWith, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotEndsWith, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
        {SearchOperation.Matches, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotMatches, new PropertySearchOperationOptions(PropertyType.SingleLineText)}
    };

    protected abstract string[] GetMatchSources(TDbValue value);
}