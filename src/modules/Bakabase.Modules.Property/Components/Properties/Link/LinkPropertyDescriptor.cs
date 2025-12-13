using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Components.Properties.Text.Abstractions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Link;

public class LinkPropertyDescriptor : TextPropertyDescriptor<LinkValue, LinkValue>
{
    public override PropertyType Type => PropertyType.Link;

    protected override string[] GetMatchSources(LinkValue? value) =>
        new[] {value?.Text, value?.Url}.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;

    /// <summary>
    /// 为 Text 和 Url 分别生成索引条目
    /// </summary>
    protected override IEnumerable<PropertyIndexEntry> GenerateIndexEntriesInternal(
        Bakabase.Abstractions.Models.Domain.Property property,
        LinkValue dbValue)
    {
        if (!string.IsNullOrEmpty(dbValue.Text))
        {
            yield return new PropertyIndexEntry(dbValue.Text);
        }
        if (!string.IsNullOrEmpty(dbValue.Url))
        {
            yield return new PropertyIndexEntry(dbValue.Url);
        }
    }

    /// <summary>
    /// 重写索引搜索：filter 值是 string，需要在 Text 和 Url 的索引中搜索
    /// </summary>
    public override HashSet<int>? SearchIndex(
        SearchOperation operation,
        object? filterDbValue,
        IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
        IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
        IReadOnlyCollection<int> allResourceIds)
    {
        // 处理 IsNull/IsNotNull
        if (operation == SearchOperation.IsNull)
        {
            return SearchIsNull(valueIndex, rangeIndex, allResourceIds);
        }
        if (operation == SearchOperation.IsNotNull)
        {
            return SearchIsNotNull(valueIndex, rangeIndex);
        }

        // filter 值是 string（SingleLineText 类型）
        if (filterDbValue is not string searchTerm || valueIndex == null)
        {
            return new HashSet<int>();
        }

        var normalizedSearch = Normalize(searchTerm);
        if (string.IsNullOrEmpty(normalizedSearch))
        {
            return new HashSet<int>();
        }

        return operation switch
        {
            SearchOperation.Equals => GetExactMatch(normalizedSearch, valueIndex),
            SearchOperation.NotEquals => Negate(GetExactMatch(normalizedSearch, valueIndex), allResourceIds),
            SearchOperation.Contains => SearchContains(normalizedSearch, valueIndex),
            SearchOperation.NotContains => Negate(SearchContains(normalizedSearch, valueIndex), allResourceIds),
            SearchOperation.StartsWith => SearchStartsWith(normalizedSearch, valueIndex),
            SearchOperation.NotStartsWith => Negate(SearchStartsWith(normalizedSearch, valueIndex), allResourceIds),
            SearchOperation.EndsWith => SearchEndsWith(normalizedSearch, valueIndex),
            SearchOperation.NotEndsWith => Negate(SearchEndsWith(normalizedSearch, valueIndex), allResourceIds),
            SearchOperation.Matches => SearchMatches(searchTerm, valueIndex), // 正则不需要规范化
            SearchOperation.NotMatches => Negate(SearchMatches(searchTerm, valueIndex), allResourceIds),
            _ => null
        };
    }
}