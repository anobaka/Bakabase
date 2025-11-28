namespace Bakabase.Abstractions.Models.View;

/// <summary>
/// 最近查看的内容视图模型
/// </summary>
public record ThirdPartyContentTrackerNearestViewModel
{
    /// <summary>
    /// 内容 ID
    /// </summary>
    public string ContentId { get; set; } = null!;

    /// <summary>
    /// 查看时间
    /// </summary>
    public DateTime ViewedAt { get; set; }
}
