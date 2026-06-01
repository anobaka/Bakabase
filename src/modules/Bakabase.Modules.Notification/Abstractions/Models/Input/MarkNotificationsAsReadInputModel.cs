namespace Bakabase.Modules.Notification.Abstractions.Models.Input;

public record MarkNotificationsAsReadInputModel
{
    /// <summary>When null or empty, all unread notifications are marked as read.</summary>
    public int[]? Ids { get; set; }
}
