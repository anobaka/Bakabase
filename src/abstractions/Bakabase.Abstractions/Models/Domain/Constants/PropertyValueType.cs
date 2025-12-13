namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// 属性值类型
/// </summary>
public enum PropertyValueType
{
    /// <summary>
    /// 固定值
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// 动态值（使用目录名）
    /// </summary>
    Dynamic = 2
}
