using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Notification.Abstractions.Models.Input;

public record CreateTestNotificationInputModel
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public AppNotificationSeverity Severity { get; set; } = AppNotificationSeverity.Info;
}
