using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.View;

public class AppNotificationMessageViewModel
{
    /// <summary>
    /// Unique identifier for this notification
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Notification title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Notification message content
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Severity level of the notification
    /// </summary>
    public AppNotificationSeverity Severity { get; set; } = AppNotificationSeverity.Info;

    /// <summary>
    /// Behavior of the notification (auto-dismiss or persistent)
    /// </summary>
    public AppNotificationBehavior Behavior { get; set; } = AppNotificationBehavior.AutoDismiss;

    /// <summary>
    /// Auto-dismiss duration in milliseconds (only applies when Behavior is AutoDismiss)
    /// Default is 5000ms (5 seconds)
    /// </summary>
    public int? DurationMs { get; set; } = 5000;

    /// <summary>
    /// Timestamp when notification was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional metadata for the notification
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
