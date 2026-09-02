using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// One planned (and, once applied, executed) rename, produced by the workflow
/// <c>action.fs.saveName</c> activity. Plan, application progress and undo record are the same
/// row across its <see cref="Status"/> lifecycle — see <see cref="FileRenameStatus"/>.
/// Rows are grouped by the workflow run that planned them.
/// </summary>
public record FileRenameEntry
{
    [Key] public int Id { get; set; }

    /// <summary>The <c>WorkflowRun</c> whose saveName step planned this rename.</summary>
    public int RunId { get; set; }

    /// <summary>Order within the run's plan; undo replays Applied rows in reverse.</summary>
    public int Seq { get; set; }

    /// <summary>Full path of the entry when the plan was recorded.</summary>
    [Required, MaxLength(1024)]
    public string Path { get; set; } = null!;

    /// <summary>Name at plan time (with extension for files).</summary>
    [Required, MaxLength(256)]
    public string From { get; set; } = null!;

    /// <summary>Sanitized target name. May be empty when sanitizing emptied it — such rows are
    /// always <see cref="FileRenameStatus.Conflict"/>.</summary>
    [MaxLength(256)]
    public string To { get; set; } = "";

    public FileRenameStatus Status { get; set; }

    [MaxLength(512)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AppliedAt { get; set; }
}
