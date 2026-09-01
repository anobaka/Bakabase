namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum ResourceMoveRecordStatus
{
    Pending = 1,
    Moving = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,

    /// <summary>
    /// The process died while this record was <see cref="Moving"/>. Files may be split across the
    /// source and destination; never auto-resumed — the user retries explicitly.
    /// </summary>
    Interrupted = 6
}
