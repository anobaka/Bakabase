namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// 规则队列操作类型
/// </summary>
public enum RuleQueueAction
{
    /// <summary>
    /// 应用规则到路径
    /// </summary>
    Apply = 1,

    /// <summary>
    /// 重新评估路径（规则变更时）
    /// </summary>
    Reevaluate = 2,

    /// <summary>
    /// 文件系统变更触发
    /// </summary>
    FileSystemChange = 3
}
