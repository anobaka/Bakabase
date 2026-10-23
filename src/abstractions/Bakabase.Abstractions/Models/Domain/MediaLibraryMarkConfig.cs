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
    /// Applicability depth relative to PathMark.Path: 0 = mark, positive = descendants,
    /// negative = ancestor depth. Only existing resources at or below the mark are considered.
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

    /// <summary>Dynamic value origin. Missing fields retain mark-relative extraction.</summary>
    public PathMarkValueBasePath ValueBasePath { get; set; } = PathMarkValueBasePath.MarkPath;

    /// <summary>Dynamic extraction mode. Null preserves the historical MatchMode behavior.</summary>
    public PathMatchMode? ValueMatchMode { get; set; }

    /// <summary>
    /// Fixed 模式：直接的媒体库 ID (MediaLibraryV2.Id)
    /// Dynamic 模式：null
    /// </summary>
    public int? MediaLibraryId { get; set; }

    /// <summary>
    /// Fixed libraries. When present, takes precedence over the legacy single MediaLibraryId.
    /// </summary>
    public List<int>? MediaLibraryIds { get; set; }

    /// <summary>
    /// Dynamic layer relative to ValueBasePath: 0 selects that path's name (including a file extension),
    /// negative values select ancestors (-1 = parent). With MarkPath, positive values select the
    /// resource's branch at that depth below the mark. ResourcePath does not support positive layers.
    /// </summary>
    public int? LayerToMediaLibrary { get; set; }

    /// <summary>
    /// Dynamic regex applied to the selected base path's file or directory name.
    /// Uses the first capture group, or the entire match when no capture exists.
    /// </summary>
    public string? RegexToMediaLibrary { get; set; }

    /// <summary>
    /// 应用范围
    /// MatchedOnly = 仅对匹配的路径生效
    /// MatchedAndSubdirectories = 对匹配的路径及其所有子目录生效
    /// </summary>
    public PathMarkApplyScope ApplyScope { get; set; } = PathMarkApplyScope.MatchedOnly;
}
