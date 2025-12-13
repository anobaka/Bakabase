using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 资源标记配置 (Type = Resource 时的配置)
/// </summary>
public class ResourceMarkConfig
{
    /// <summary>
    /// 匹配模式
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
    /// 文件系统类型过滤（可选）
    /// </summary>
    public PathFilterFsType? FsTypeFilter { get; set; }

    /// <summary>
    /// 扩展名过滤（仅当 FsTypeFilter = File 时有效）
    /// </summary>
    public List<string>? Extensions { get; set; }

    /// <summary>
    /// 扩展名组ID列表（仅当 FsTypeFilter = File 时有效）
    /// 会与 Extensions 合并使用
    /// </summary>
    public List<int>? ExtensionGroupIds { get; set; }

    /// <summary>
    /// 应用范围
    /// MatchedOnly = 仅对匹配的路径生效
    /// MatchedAndSubdirectories = 对匹配的路径及其所有子目录生效
    /// </summary>
    public PathMarkApplyScope ApplyScope { get; set; } = PathMarkApplyScope.MatchedOnly;

    /// <summary>
    /// 是否在此路径建立资源边界
    /// 当设置为 true 时，上层的资源标记不会在此路径及其子路径上生效
    /// 用于处理目录结构不规则的情况，例如某些子目录需要不同的资源粒度
    /// </summary>
    public bool IsResourceBoundary { get; set; } = false;
}
