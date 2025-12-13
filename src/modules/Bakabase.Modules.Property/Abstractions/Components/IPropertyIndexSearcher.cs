using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// 属性索引搜索器接口
/// 由属性描述符实现，用于在预构建的索引上执行搜索操作
/// </summary>
public interface IPropertyIndexSearcher
{
    /// <summary>
    /// 在索引上执行搜索操作
    /// </summary>
    /// <param name="operation">搜索操作类型</param>
    /// <param name="filterDbValue">过滤器的数据库值（类型由属性和操作决定）</param>
    /// <param name="valueIndex">值索引：标准化字符串 -> 资源ID集合</param>
    /// <param name="rangeIndex">范围索引：可比较值 -> 资源ID集合（已按值排序）</param>
    /// <param name="allResourceIds">所有已索引的资源ID（用于否定操作）</param>
    /// <returns>匹配的资源ID集合，null 表示"匹配所有"或不支持该操作</returns>
    HashSet<int>? SearchIndex(
        SearchOperation operation,
        object? filterDbValue,
        IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
        IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
        IReadOnlyCollection<int> allResourceIds);
}
