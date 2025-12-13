using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 搜索条件（用于 ResourceProfile）
/// </summary>
public class SearchCriteria
{
    /// <summary>
    /// 媒体库过滤
    /// </summary>
    public List<int>? MediaLibraryIds { get; set; }

    /// <summary>
    /// 属性过滤条件
    /// </summary>
    public List<PropertyFilter>? PropertyFilters { get; set; }

    /// <summary>
    /// 路径模式匹配
    /// </summary>
    public string? PathPattern { get; set; }

    /// <summary>
    /// 标签过滤
    /// </summary>
    public ResourceTag? TagFilter { get; set; }
}

/// <summary>
/// 属性过滤器
/// </summary>
public class PropertyFilter
{
    public PropertyPool Pool { get; set; }
    public int PropertyId { get; set; }
    public SearchOperation Operation { get; set; }
    public object? Value { get; set; }
}
