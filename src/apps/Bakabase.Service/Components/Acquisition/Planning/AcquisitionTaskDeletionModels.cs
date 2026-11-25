using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionTaskDeletePreviewInput(IReadOnlyList<int> TaskIds,
    bool? CleanupFiles = null, bool? AcceptDeliveredFiles = null, bool? SkipFileVerification = null, bool? RetryRetainedFiles = null,
    bool? DeleteDeliveredFiles = null);
public sealed record AcquisitionTaskDeleteInput(string PreviewId, bool? CleanupFiles = null,
    bool? AcceptDeliveredFiles = null, bool? SkipFileVerification = null, bool? RemoveTaskIfCleanupFails = null, bool? RetryRetainedFiles = null,
    bool? DeleteDeliveredFiles = null, IReadOnlyList<int>? TaskIds = null,
    IReadOnlyDictionary<int, IReadOnlyList<string>>? SelectedCleanupDirectories = null);
// Progress describes confirmed work only; it is not an authorization or a replacement for the final result.
public sealed record AcquisitionTaskDeleteProgress(string PreviewId, string State, int TotalTasks, int FinishedTasks,
    int? CurrentTaskId, string Phase, int CompletedFiles, long CompletedBytes,
    DateTimeOffset? StartedAt, DateTimeOffset UpdatedAt)
{
    public bool? CleanupFiles { get; init; }
    public bool? DeleteDeliveredFiles { get; init; }
    public bool SelectedDirectoriesOnly { get; init; }
    public bool ReceiptOnly { get; init; }
}
public sealed record AcquisitionTaskDeleteRow(int TaskId, string? Title, bool CanDelete, string? ReasonCode,
    AcquisitionTaskDeleteCleanup? Cleanup = null, bool CanRemoveTaskIfCleanupFails = false, bool WillStopExecution = false);
public sealed record AcquisitionTaskDeletePreview(string PreviewId, DateTimeOffset ExpiresAt, int TotalCount,
    int DeletableCount, int BlockedCount, IReadOnlyList<AcquisitionTaskDeleteRow> Rows,
    bool CleanupFiles = false, bool AcceptDeliveredFiles = false, int CleanupFileCount = 0, long CleanupBytes = 0,
    int IdentifiedFileCount = 0, long IdentifiedBytes = 0, bool CleanupSizeIncomplete = false,
    bool SkipFileVerification = false, bool RetryRetainedFiles = false, bool DeleteDeliveredFiles = false);
public sealed record AcquisitionTaskDeleteSkip(int TaskId, string ReasonCode);
public sealed record AcquisitionTaskDeleteResult(IReadOnlyList<int> DeletedTaskIds, IReadOnlyList<AcquisitionTaskDeleteSkip> Skipped,
    IReadOnlyList<AcquisitionTaskDeleteCleanupOutcome>? CleanupResults = null, int RemovedFiles = 0, long RemovedBytes = 0,
    IReadOnlyList<string>? RemovedDirectories = null, bool FileStatisticsIncomplete = false);
public sealed record AcquisitionTaskDeleteCleanupFile(string Path, long Bytes);
public sealed record AcquisitionTaskDeleteCleanupNode(int RunId, string NodeId, string Kind);
[Flags]
internal enum AcquisitionCleanupScopes { None = 0, Intermediate = 1, Delivery = 2, Both = Intermediate | Delivery }

public sealed record AcquisitionTaskDeleteCleanupGroup(string ArtifactId, string Kind, string Directory,
    bool Eligible, string? ReasonCode, IReadOnlyList<AcquisitionTaskDeleteCleanupNode> Nodes,
    IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, int FileCount, long Bytes, bool Shared = false,
    bool FilesTruncated = false, bool SizeUnknown = false)
{
    public bool RegisteredFilesOnly { get; init; }
    internal AcquisitionCleanupScopes Scopes { get; init; } = Kind switch
    {
        "download" or "processedFiles" or "intermediate" => AcquisitionCleanupScopes.Intermediate,
        "publishedFiles" => AcquisitionCleanupScopes.Delivery,
        _ => AcquisitionCleanupScopes.Both
    };
}
public sealed record AcquisitionTaskDeleteCleanup(int FileCount, long Bytes,
    IReadOnlyList<AcquisitionTaskDeleteCleanupGroup> Groups, string? ReasonCode = null,
    IReadOnlyList<AcquisitionTaskDeleteSupersededRun>? SupersededRuns = null,
    int IdentifiedFileCount = 0, long IdentifiedBytes = 0, bool SizeIncomplete = false);
public sealed record AcquisitionTaskDeleteSupersededRun(int RunId, int Revision, int ReplacedByRevision);
public sealed record AcquisitionTaskDeleteCleanupOutcome(int TaskId, bool Completed, int RemovedFiles, long RemovedBytes,
    IReadOnlyList<string> ReasonCodes, IReadOnlyList<int>? ClosedRunIds = null,
    IReadOnlyList<string>? RetainedDirectories = null, IReadOnlyList<string>? RemovedDirectories = null,
    bool FileStatisticsIncomplete = false)
{
    public IReadOnlyList<string> UnselectedDirectories { get; init; } = [];
    internal AcquisitionCleanupScopes ManualReviewScopes { get; init; } = AcquisitionCleanupScopes.Both;
    internal IReadOnlyList<(string Id, AcquisitionCleanupScopes Scopes)> DeferredReceipts { get; init; } = [];
    // A plan-wide failure has no per-stage proof.
    internal AcquisitionCleanupScopes UnconfirmedScopes { get; init; } = Completed ? AcquisitionCleanupScopes.None : AcquisitionCleanupScopes.Both;
}

public sealed record AcquisitionDeletedTaskRow(int TaskId, string? Title, DateTime DeletedAt,
    string IntermediateCleanup, string DeliveryCleanup, IReadOnlyList<string> LastReasonCodes)
{
    public IReadOnlyList<string> AwaitingFileReleaseScopes { get; init; } = [];
    public IReadOnlyList<int> WaitingRunIds { get; init; } = [];
}
public sealed record AcquisitionDeletedTaskPage(int PageIndex, int PageSize, int TotalCount,
    IReadOnlyList<AcquisitionDeletedTaskRow> Items);
