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

    /// <summary>
    /// Toggle a row between Pending and Excluded — the confirm panel's checkbox. Any other
    /// status refuses: an applied or conflicted row is not the user's to include.
    /// </summary>
    Task<FileRenameEntry> SetExcluded(int id, bool excluded);

    /// <summary>
    /// Execute the run's Pending rows on disk, deepest paths first so a renamed parent
    /// directory cannot invalidate its children's recorded paths. Every row's status is saved
    /// as it happens — a crash leaves an exact, queryable record of which renames were done.
    /// One failing row becomes Failed and the rest continue.
    /// </summary>
    Task<List<FileRenameEntry>> ApplyRun(int runId);

    /// <summary>
    /// Replay the run's Applied rows in reverse (shallowest first — parents regain their old
    /// names before their children's recorded paths are used). Rows that cannot be reverted
    /// keep Applied with the reason in <see cref="FileRenameEntry.Error"/>.
    /// </summary>
    Task<List<FileRenameEntry>> UndoRun(int runId);

    /// <summary>Remove the plan rows of the given runs — definition deletion cleanup.</summary>
    Task DeleteByRunIds(IReadOnlyCollection<int> runIds);
}
