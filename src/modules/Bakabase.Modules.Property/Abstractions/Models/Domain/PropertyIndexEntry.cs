namespace Bakabase.Modules.Property.Abstractions.Models.Domain;

/// <summary>
/// 索引条目，包含索引键和可选的可比较值（用于范围查询）
/// </summary>
public record struct PropertyIndexEntry(string Key, IComparable? RangeValue = null);