using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Notification.Abstractions.Models.Input;

public record NotificationCreationInputModel
{
    public string Source { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public string? PayloadJson { get; set; }
    public AppNotificationSeverity Severity { get; set; } = AppNotificationSeverity.Info;
}
