using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Notification.Abstractions.Models.Domain;

public record NotificationRecord
{
    public int Id { get; set; }
    public string Source { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public string? PayloadJson { get; set; }
    public AppNotificationSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public bool IsRead => ReadAt.HasValue;
}
