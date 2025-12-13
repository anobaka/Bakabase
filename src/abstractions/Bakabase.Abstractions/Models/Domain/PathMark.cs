using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 路径标记（单一职责）
/// </summary>
public class PathMark
{
    /// <summary>
    /// 标记类型
    /// </summary>
    public PathMarkType Type { get; set; }

    /// <summary>
    /// 优先级（数字越大优先级越高，用于解决同类型 Mark 冲突）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 配置内容 JSON（根据 Type 解析为对应的 Config 类）
    /// </summary>
    public string ConfigJson { get; set; } = null!;
}
