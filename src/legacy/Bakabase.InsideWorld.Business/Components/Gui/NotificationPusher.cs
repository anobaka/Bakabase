using System.Threading.Tasks;
using Bakabase.Modules.Notification.Abstractions.Components;
using Bakabase.Modules.Notification.Abstractions.Models.View;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.InsideWorld.Business.Components.Gui;

public class NotificationPusher : INotificationPusher
{
    private readonly IHubContext<WebGuiHub, IWebGuiClient> _hub;

    public NotificationPusher(IHubContext<WebGuiHub, IWebGuiClient> hub)
    {
        _hub = hub;
    }

    public Task PushPersistedAsync(NotificationViewModel notification) =>
        _hub.Clients.All.OnPersistentNotification(notification);
}
