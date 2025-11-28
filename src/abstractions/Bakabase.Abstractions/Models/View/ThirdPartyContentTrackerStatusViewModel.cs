namespace Bakabase.Abstractions.Models.View;

/// <summary>
/// 第三方内容状态视图模型
/// </summary>
public record ThirdPartyContentTrackerStatusViewModel
{
    /// <summary>
    /// 内容 ID
    /// </summary>
    public string ContentId { get; set; } = null!;

    /// <summary>
    /// 内容的更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 用户查看该内容的时间（null 表示未查看）
    /// </summary>
    public DateTime? ViewedAt { get; set; }

    /// <summary>
    /// 是否已查看
    /// </summary>
    public bool IsViewed => ViewedAt.HasValue;

    /// <summary>
    /// 是否有更新（查看后内容有更新）
    /// </summary>
    public bool HasUpdate => IsViewed && UpdatedAt.HasValue && ViewedAt.HasValue && UpdatedAt > ViewedAt;
}
