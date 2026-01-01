using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Attachment;

public class AttachmentPropertyDescriptor : AbstractPropertyDescriptor<List<string>, List<string>>
{
    public override PropertyType Type => PropertyType.Attachment;

    /// <summary>
    /// 为每个附件路径生成单独的索引条目
    /// </summary>
    protected override IEnumerable<PropertyIndexEntry> GenerateIndexEntriesInternal(
        Bakabase.Abstractions.Models.Domain.Property property,
        List<string> dbValue)
    {
        foreach (var path in dbValue)
        {
            if (!string.IsNullOrEmpty(path))
            {
                yield return new PropertyIndexEntry(path);
            }
        }
    }

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (filterValue as string)!;

        return operation switch
        {
            SearchOperation.Contains => dbValue.Any(x => x.Contains(fv, StringComparison.OrdinalIgnoreCase)),
            SearchOperation.NotContains => dbValue.All(x => !x.Contains(fv, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    /// <summary>
    /// 重写索引搜索：filter 值是 string，需要在所有索引键中搜索
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

        // Contains/NotContains 的 filter 值是 string
        if (filterDbValue is not string searchTerm || valueIndex == null)
        {
            return new HashSet<int>();
        }

        var normalizedSearch = Normalize(searchTerm);
        if (string.IsNullOrEmpty(normalizedSearch))
        {
            return new HashSet<int>();
        }

        // 遍历所有索引键，查找包含搜索词的路径
        var result = new HashSet<int>();
        foreach (var (key, resourceIds) in valueIndex)
        {
            if (key.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(resourceIds);
            }
        }

        return operation switch
        {
            SearchOperation.Contains => result,
            SearchOperation.NotContains => Negate(result, allResourceIds),
            _ => null
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
    };
}