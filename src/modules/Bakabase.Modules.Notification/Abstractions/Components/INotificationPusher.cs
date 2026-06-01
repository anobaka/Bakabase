using Bakabase.Modules.Notification.Abstractions.Models.View;

namespace Bakabase.Modules.Notification.Abstractions.Components;

/// <summary>
/// Bridge between the Notification module and whatever push channel the host wires up
/// (currently SignalR via WebGuiHub). Keeps the module independent of the hosting layer.
/// </summary>
public interface INotificationPusher
{
    Task PushPersistedAsync(NotificationViewModel notification);
}
