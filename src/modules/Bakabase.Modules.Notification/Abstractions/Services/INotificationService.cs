using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Notification.Abstractions.Services;

public interface INotificationService
{
    /// <summary>Persists the notification and pushes it to all connected clients.</summary>
    Task<NotificationRecord> CreateAsync(NotificationCreationInputModel input);

    Task<SearchResponse<NotificationRecord>> SearchAsync(NotificationSearchInputModel input);

    /// <summary>Total unread count, used by the frontend badge.</summary>
    Task<int> GetUnreadCountAsync();

    /// <summary>Marks the given notifications as read; when ids is null/empty all unread are marked.</summary>
    Task MarkAsReadAsync(int[]? ids);

    /// <summary>Deletes the specified notifications.</summary>
    Task DeleteAsync(int[] ids);

    /// <summary>Deletes all read notifications (unread are preserved).</summary>
    Task ClearReadAsync();
}
