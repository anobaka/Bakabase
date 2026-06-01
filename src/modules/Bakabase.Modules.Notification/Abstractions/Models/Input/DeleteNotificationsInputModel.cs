namespace Bakabase.Modules.Notification.Abstractions.Models.Input;

public record DeleteNotificationsInputModel
{
    public int[] Ids { get; set; } = [];
}
