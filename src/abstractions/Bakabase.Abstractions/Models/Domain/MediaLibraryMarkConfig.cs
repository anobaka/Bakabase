using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 媒体库标记配置 (Type = MediaLibrary 时的配置)
/// </summary>
public class MediaLibraryMarkConfig
{
    /// <summary>
    /// 匹配模式（决定哪些路径会应用此媒体库配置）
    /// </summary>
    public PathMatchMode MatchMode { get; set; }

    /// <summary>
    /// Layer 模式：作用的路径层级（相对于 PathRule.Path）
    /// 1 = 第一级子目录，2 = 第二级子目录，以此类推
    /// -1 = 所有层级
    /// </summary>
    public int? Layer { get; set; }

    /// <summary>
    /// Regex 模式：匹配路径的正则表达式（相对于 PathRule.Path）
    /// </summary>
    public string? Regex { get; set; }

    /// <summary>
    /// 值类型（Fixed = 固定媒体库，Dynamic = 从路径动态提取媒体库名称）
    /// </summary>
    public PropertyValueType ValueType { get; set; }

    /// <summary>
    /// Fixed 模式：直接的媒体库 ID (MediaLibraryV2.Id)
    /// Dynamic 模式：null
    /// </summary>
    public int? MediaLibraryId { get; set; }

    /// <summary>
    /// Dynamic 模式：从哪一层获取目录名作为媒体库名称
    /// </summary>
    public int? LayerToMediaLibrary { get; set; }

    /// <summary>
    /// Dynamic 模式：正则提取（可选），默认取第一个 group，或最后一级目录匹配项
    /// </summary>
    public string? RegexToMediaLibrary { get; set; }

    /// <summary>
    /// 应用范围
    /// MatchedOnly = 仅对匹配的路径生效
    /// MatchedAndSubdirectories = 对匹配的路径及其所有子目录生效
    /// </summary>
    public PathMarkApplyScope ApplyScope { get; set; } = PathMarkApplyScope.MatchedOnly;
}
