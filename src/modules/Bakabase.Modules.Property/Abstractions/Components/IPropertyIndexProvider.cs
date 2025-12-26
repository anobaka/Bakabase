using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// 属性索引提供器接口
/// 由每种属性类型实现，生成该属性值对应的索引键
/// </summary>
public interface IPropertyIndexProvider
{
    /// <summary>
    /// 从数据库值生成索引条目
    /// </summary>
    /// <param name="property">属性定义</param>
    /// <param name="dbValue">数据库中存储的值</param>
    /// <returns>索引条目列表（一个值可能生成多个索引条目）</returns>
    IEnumerable<PropertyIndexEntry> GenerateIndexEntries(
        Bakabase.Abstractions.Models.Domain.Property property,
        object? dbValue);
}
