namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// Where an acquisition task stands. It mirrors the workflow run that carries it — the task row is
/// an index over runs, not a second source of truth — so that "what am I getting, and how is it
/// going" is one cheap query instead of a join through the workflow tables.
/// </summary>
public enum AcquisitionStatus
{
    /// <summary>Created, queued, not started.</summary>
    Pending = 1,

    Running = 2,

    /// <summary>Stopped at a step that needs something from outside.</summary>
    Waiting = 3,

    Completed = 4,

    Failed = 5,

    Cancelled = 6
}
