namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// 路径匹配模式
/// </summary>
public enum PathMatchMode
{
    /// <summary>
    /// 按层级匹配
    /// </summary>
    Layer = 1,

    /// <summary>
    /// 按正则表达式匹配
    /// </summary>
    Regex = 2
}
