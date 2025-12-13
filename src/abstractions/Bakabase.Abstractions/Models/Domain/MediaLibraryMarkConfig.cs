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
    ///
    /// Layer 语义（与 FilterByLayer 一致）：
    /// - 0 = 标记路径本身的目录名
    /// - 负数（如 -1, -2）= 向上：标记路径的父目录（-1=父，-2=祖父，以此类推）
    /// - 正数（如 1, 2）= 向下：标记路径的子目录（基于资源路径确定具体子目录）
    ///
    /// 当 LayerToMediaLibrary &gt; 0 时，由于可能存在多个子目录，需要根据资源路径来确定使用哪个值。
    /// 例如：标记路径 /data/media，LayerToMediaLibrary = 1，资源路径 /data/media/movies/a.mp4
    /// 则提取的媒体库名称为 "movies"
    /// </summary>
    public int? LayerToMediaLibrary { get; set; }

    /// <summary>
    /// Dynamic 模式：正则提取（可选）
    /// 对标记路径的目录名应用正则表达式，默认取第一个捕获组
    /// </summary>
    public string? RegexToMediaLibrary { get; set; }

    /// <summary>
    /// 应用范围
    /// MatchedOnly = 仅对匹配的路径生效
    /// MatchedAndSubdirectories = 对匹配的路径及其所有子目录生效
    /// </summary>
    public PathMarkApplyScope ApplyScope { get; set; } = PathMarkApplyScope.MatchedOnly;
}
