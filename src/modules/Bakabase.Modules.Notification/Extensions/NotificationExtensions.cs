using Bakabase.Modules.Notification.Abstractions.Models.Db;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;

namespace Bakabase.Modules.Notification.Extensions;

public static class NotificationExtensions
{
    public static NotificationRecord ToDomainModel(this NotificationDbModel db) => new()
    {
        Id = db.Id,
        Source = db.Source,
        Title = db.Title,
        Body = db.Body,
        PayloadJson = db.PayloadJson,
        Severity = db.Severity,
        CreatedAt = db.CreatedAt,
        ReadAt = db.ReadAt,
    };
}
