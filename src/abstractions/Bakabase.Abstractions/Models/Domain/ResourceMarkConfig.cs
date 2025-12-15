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
}
