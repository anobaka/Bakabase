namespace Bakabase.Modules.Notification.Abstractions.Models.Input;

public record NotificationSearchInputModel
{
    /// <summary>Optional Source filter (exact match).</summary>
    public string? Source { get; set; }

    /// <summary>When true, only unread notifications are returned.</summary>
    public bool UnreadOnly { get; set; }

    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}
