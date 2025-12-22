using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 属性标记配置 (Type = Property 时的配置，统一处理固定值和动态值)
/// </summary>
public class PropertyMarkConfig
{
    /// <summary>
    /// 匹配模式（决定哪些路径会应用此属性配置）
    /// </summary>
    public PathMatchMode MatchMode { get; set; }

    /// <summary>
    /// Layer 模式：作用的路径层级（相对于 PathMark.Path）
    /// 1 = 第一级子目录，2 = 第二级子目录，以此类推
    /// 0 = 匹配 PathMark.Path 本身
    /// 负数 = 父目录（-1 = 上一级，-2 = 上两级）
    /// </summary>
    public int? Layer { get; set; }

    /// <summary>
    /// Regex 模式：匹配路径的正则表达式（相对于 PathRule.Path）
    /// </summary>
    public string? Regex { get; set; }

    /// <summary>
    /// 属性池
    /// </summary>
    public PropertyPool Pool { get; set; }

    /// <summary>
    /// 属性 ID
    /// </summary>
    public int PropertyId { get; set; }

    /// <summary>
    /// 值类型
    /// </summary>
    public PropertyValueType ValueType { get; set; }

    /// <summary>
    /// Fixed 模式：直接的属性值
    /// Dynamic 模式：null
    /// </summary>
    public object? FixedValue { get; set; }

    /// <summary>
    /// Dynamic 模式：从哪一层获取目录名作为值
    /// </summary>
    public int? ValueLayer { get; set; }

    /// <summary>
    /// Dynamic 模式：正则提取（可选）
    /// </summary>
    public string? ValueRegex { get; set; }

    /// <summary>
    /// 应用范围
    /// MatchedOnly = 仅对匹配的路径生效
    /// MatchedAndSubdirectories = 对匹配的路径及其所有子目录生效
    /// </summary>
    public PathMarkApplyScope ApplyScope { get; set; } = PathMarkApplyScope.MatchedOnly;
}
