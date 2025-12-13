using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 路径规则处理队列项数据库模型
/// </summary>
public record PathRuleQueueItemDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 待处理的路径
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// 处理类型
    /// </summary>
    public RuleQueueAction Action { get; set; }

    /// <summary>
    /// 相关规则 ID
    /// </summary>
    public int? RuleId { get; set; }

    public DateTime CreateDt { get; set; }
    public RuleQueueStatus Status { get; set; }
    public string? Error { get; set; }
}
