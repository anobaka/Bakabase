namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// 规则队列项状态
/// </summary>
public enum RuleQueueStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
