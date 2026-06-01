using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;

namespace Bakabase.Modules.Notification.Abstractions.Models.View;

public record NotificationViewModel
{
    public int Id { get; set; }
    public string Source { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public string? PayloadJson { get; set; }
    public AppNotificationSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public static NotificationViewModel From(NotificationRecord n) => new()
    {
        Id = n.Id,
        Source = n.Source,
        Title = n.Title,
        Body = n.Body,
        PayloadJson = n.PayloadJson,
        Severity = n.Severity,
        CreatedAt = n.CreatedAt,
        ReadAt = n.ReadAt,
    };
}
