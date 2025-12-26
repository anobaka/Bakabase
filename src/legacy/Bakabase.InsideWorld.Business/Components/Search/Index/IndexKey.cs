using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Search.Index;

/// <summary>
/// 索引键，用于追踪资源的索引条目
/// </summary>
public readonly record struct IndexKey(PropertyPool Pool, int PropertyId, string ValueKey)
{
    public override string ToString() => $"{Pool}:{PropertyId}:{ValueKey}";
}

/// <summary>
/// 索引操作类型
/// </summary>
public enum IndexOperationType
{
    /// <summary>
    /// 更新资源索引（先删除旧索引，再添加新索引）
    /// </summary>
    Update,

    /// <summary>
    /// 删除资源索引
    /// </summary>
    Remove
}

/// <summary>
/// 索引操作
/// </summary>
public readonly record struct IndexOperation(IndexOperationType Type, int ResourceId);
