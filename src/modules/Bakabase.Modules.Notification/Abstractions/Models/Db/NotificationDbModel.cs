using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Notification.Abstractions.Models.Db;

public record NotificationDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>
    /// Free-form source identifier, e.g. "subscription:42", "btask:Enhancement".
    /// Lets future producers reuse the same notification surface without changing this schema.
    /// </summary>
    public string Source { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? Body { get; set; }

    /// <summary>JSON blob with deep-link / item summaries / arbitrary producer-defined data.</summary>
    public string? PayloadJson { get; set; }

    public AppNotificationSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
