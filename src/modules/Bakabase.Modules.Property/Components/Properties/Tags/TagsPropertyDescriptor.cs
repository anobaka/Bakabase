using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Tags;

public class TagsPropertyDescriptor : AbstractPropertyDescriptor<TagsPropertyOptions, List<string>, List<TagValue>>
{
    public override PropertyType Type => PropertyType.Tags;

    /// <summary>
    /// 为每个 tag 生成单独的索引条目
    /// </summary>
    protected override IEnumerable<PropertyIndexEntry> GenerateIndexEntriesInternal(
        Bakabase.Abstractions.Models.Domain.Property property,
        List<string> dbValue)
    {
        // 每个 tag ID 单独生成一个索引条目
        foreach (var tagId in dbValue)
        {
            if (!string.IsNullOrEmpty(tagId))
            {
                yield return new PropertyIndexEntry(tagId);
            }
        }
    }

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
        Bakabase.Abstractions.Models.Domain.Property property,
        string keyword)
    {
        var options = property.Options as TagsPropertyOptions;
        var values = options?.Tags?.Where(t => t.Group?.Contains(keyword) == true || t.Name.Contains(keyword))
            .Select(t => t.Value).ToHashSet();
        return values?.Any() == true ? (values, SearchOperation.In) : null;
    }

    protected override (List<string>? DbValue, bool PropertyChanged) PrepareDbValueInternal(
        Bakabase.Abstractions.Models.Domain.Property property, List<TagValue> bizValue)
    {
        bizValue.TrimAll();
        if (!bizValue.Any())
        {
            return (null, false);
        }

        var dbValue = new List<string>();
        var propertyChanged = false;
        var options =
            ((property.Options ??= new TagsPropertyOptions()) as
                TagsPropertyOptions)!;
        options.Tags ??= [];
        foreach (var tag in bizValue)
        {
            var definedTag = options.Tags.FirstOrDefault(x => x.Name == tag.Name && x.Group == tag.Group);
            if (definedTag == null)
            {
                definedTag = new TagsPropertyOptions.TagOptions(tag.Group, tag.Name)
                    {Value = TagsPropertyOptions.TagOptions.GenerateValue()};
                options.Tags.Add(definedTag);
                propertyChanged = true;
            }

            dbValue.Add(definedTag.Value);
        }

        return (dbValue, propertyChanged);
    }

    protected override List<TagValue>? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property,
        List<string> value)
    {
        var options = property.Options as TagsPropertyOptions;
        return value
            .Select(v => options?.Tags?.FirstOrDefault(x => x.Value == v)?.ToTagValue()).OfType<TagValue>()
            .ToList();
    }

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (List<string>) filterValue;
        return operation switch
        {
            SearchOperation.Contains => fv.All(dbValue.Contains),
            SearchOperation.NotContains => !fv.Any(dbValue.Contains),
            SearchOperation.In => dbValue.All(fv.Contains),
            _ => true
        };
    }

    /// <summary>
    /// 在索引上搜索标签值
    /// </summary>
    protected override HashSet<int>? SearchIndexInternal(
        SearchOperation operation,
        List<string> filterDbValue,
        IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
        IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
        IReadOnlyCollection<int> allResourceIds)
    {
        if (valueIndex == null || filterDbValue.Count == 0)
        {
            return operation == SearchOperation.NotContains
                ? new HashSet<int>(allResourceIds)
                : new HashSet<int>();
        }

        var matchingSets = filterDbValue
            .Select(v => valueIndex.GetValueOrDefault(Normalize(v)))
            .ToList();

        return operation switch
        {
            // Contains: 资源必须包含 filter 中所有标签 → 取交集
            SearchOperation.Contains => IntersectAll(matchingSets),

            // In: 资源的标签必须都在 filter 列表中 → 返回匹配任一标签的资源
            SearchOperation.In => UnionAll(matchingSets),

            // NotContains: 资源不能包含 filter 中任何标签 → 取反
            SearchOperation.NotContains => Negate(UnionAll(matchingSets), allResourceIds),

            _ => null
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.Tags)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.Tags)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
        {SearchOperation.In, new PropertySearchOperationOptions(PropertyType.Tags)},
    };
}