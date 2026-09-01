using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Store for <see cref="FileRenameEntry"/> rows. Deliberately plain DbContext queries rather
/// than a full-memory cache: rows are written in bursts by runs, read per run, and pruned by
/// retention — nothing here benefits from holding every historical row in memory.
/// </summary>
public interface IFileRenameEntryService
{
    Task<List<FileRenameEntry>> GetByRunId(int runId);

    /// <summary>
    /// Append one entry to a run's plan, assigning the next <see cref="FileRenameEntry.Seq"/>.
    /// </summary>
    Task<FileRenameEntry> AddToPlan(int runId, string path, string from, string to,
        FileRenameStatus status, string? error = null);

    /// <summary>
    /// Whether the run's plan already contains a non-conflict row that would occupy
    /// <paramref name="targetFullPath"/> — the duplicate-target check saveName runs per item.
    /// Case-insensitive, matching the most restrictive filesystem the plan may execute on.
    /// </summary>
    Task<bool> IsTargetPlanned(int runId, string targetFullPath);
}
