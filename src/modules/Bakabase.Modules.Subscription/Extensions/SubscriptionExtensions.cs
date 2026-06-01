using Bakabase.Modules.Subscription.Abstractions.Models.Db;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;

namespace Bakabase.Modules.Subscription.Extensions;

public static class SubscriptionExtensions
{
    public static SubscriptionRecord ToDomainModel(this SubscriptionDbModel db) => new()
    {
        Id = db.Id,
        Kind = db.Kind,
        DisplayName = db.DisplayName,
        TargetJson = db.TargetJson,
        Enabled = db.Enabled,
        CreatedAt = db.CreatedAt,
        LastCheckedAt = db.LastCheckedAt,
        LastChangeAt = db.LastChangeAt,
        LastError = db.LastError,
        IntervalMinutes = db.IntervalMinutes,
    };
}
