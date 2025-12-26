using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Search.Index;

/// <summary>
/// 资源搜索倒排索引存储结构
/// </summary>
public class ResourceSearchIndex
{
    /// <summary>
    /// 值索引：用于等值查询和包含查询
    /// PropertyPool → PropertyId → NormalizedValue → HashSet&lt;ResourceId&gt;
    /// </summary>
    /// <example>
    /// Custom → 5(Tag属性) → "动作" → {1, 5, 23, 100}
    /// Custom → 5(Tag属性) → "喜剧" → {2, 5, 50}
    /// </example>
    internal readonly ConcurrentDictionary<PropertyPool,
        ConcurrentDictionary<int,
            ConcurrentDictionary<string, HashSet<int>>>> ValueIndex = new();

    /// <summary>
    /// 范围索引：用于日期、数值的范围查询
    /// PropertyPool → PropertyId → SortedList&lt;ComparableValue, HashSet&lt;ResourceId&gt;&gt;
    /// </summary>
    internal readonly ConcurrentDictionary<PropertyPool,
        ConcurrentDictionary<int, SortedList<IComparable, HashSet<int>>>> RangeIndex = new();

    /// <summary>
    /// 资源索引键映射：用于删除/更新时快速清理旧索引
    /// ResourceId → Set&lt;IndexKey&gt;
    /// </summary>
    internal readonly ConcurrentDictionary<int, HashSet<IndexKey>> ResourceIndexKeys = new();

    /// <summary>
    /// 所有资源ID集合（用于 IsNull/IsNotNull 查询）
    /// </summary>
    internal HashSet<int> AllResourceIds = new();

    /// <summary>
    /// 用于保护 AllResourceIds 的锁
    /// </summary>
    internal readonly object AllResourceIdsLock = new();

    /// <summary>
    /// 索引版本号
    /// </summary>
    public long Version { get; internal set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; internal set; }

    /// <summary>
    /// 清空所有索引
    /// </summary>
    public void Clear()
    {
        ValueIndex.Clear();
        RangeIndex.Clear();
        ResourceIndexKeys.Clear();
        lock (AllResourceIdsLock)
        {
            AllResourceIds.Clear();
        }
    }

    /// <summary>
    /// 获取值索引的总条目数
    /// </summary>
    public int GetValueIndexEntryCount()
    {
        return ValueIndex.Sum(p =>
            p.Value.Sum(i => i.Value.Count));
    }

    /// <summary>
    /// 获取范围索引的总条目数
    /// </summary>
    public int GetRangeIndexEntryCount()
    {
        return RangeIndex.Sum(p =>
            p.Value.Sum(i => i.Value.Count));
    }
}
