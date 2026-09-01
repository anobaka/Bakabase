namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Lifecycle of one <see cref="Db.FileRenameEntry"/> row. The same row is the plan line, the
/// application progress marker and the undo record — the status is what moves (owner decision,
/// capability map §9·决定 3), so a crash mid-apply leaves a queryable record of exactly which
/// renames happened.
/// </summary>
public enum FileRenameStatus
{
    /// <summary>Planned by a preview run; eligible for apply.</summary>
    Pending = 1,

    /// <summary>Plan-time conflict (duplicate target, target exists, invalid name, path too long).
    /// Never applied; <see cref="Db.FileRenameEntry.Error"/> says why.</summary>
    Conflict = 2,

    /// <summary>Deselected by the user in the confirm panel; skipped by apply.</summary>
    Excluded = 3,

    /// <summary>Rename executed on disk.</summary>
    Applied = 4,

    /// <summary>Apply attempted and failed; <see cref="Db.FileRenameEntry.Error"/> says why.</summary>
    Failed = 5,

    /// <summary>Applied, then rolled back by undo.</summary>
    Undone = 6
}
