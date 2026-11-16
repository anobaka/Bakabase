using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionTaskDeletePreviewInput(IReadOnlyList<int> TaskIds,
    bool? CleanupFiles = null, bool? AcceptDeliveredFiles = null, bool? SkipFileVerification = null, bool? RetryRetainedFiles = null,
    bool? DeleteDeliveredFiles = null);
public sealed record AcquisitionTaskDeleteInput(string PreviewId, bool? CleanupFiles = null,
    bool? AcceptDeliveredFiles = null, bool? SkipFileVerification = null, bool? RemoveTaskIfCleanupFails = null, bool? RetryRetainedFiles = null,
    bool? DeleteDeliveredFiles = null);
// Progress describes confirmed work only; it is not an authorization or a replacement for the final result.
public sealed record AcquisitionTaskDeleteProgress(string PreviewId, string State, int TotalTasks, int FinishedTasks,
    int? CurrentTaskId, string Phase, int CompletedFiles, long CompletedBytes,
    DateTimeOffset? StartedAt, DateTimeOffset UpdatedAt);
public sealed record AcquisitionTaskDeleteRow(int TaskId, string? Title, bool CanDelete, string? ReasonCode,
    AcquisitionTaskDeleteCleanup? Cleanup = null, bool CanRemoveTaskIfCleanupFails = false, bool WillStopExecution = false);
public sealed record AcquisitionTaskDeletePreview(string PreviewId, DateTimeOffset ExpiresAt, int TotalCount,
    int DeletableCount, int BlockedCount, IReadOnlyList<AcquisitionTaskDeleteRow> Rows,
    bool CleanupFiles = false, bool AcceptDeliveredFiles = false, int CleanupFileCount = 0, long CleanupBytes = 0,
    int IdentifiedFileCount = 0, long IdentifiedBytes = 0, bool CleanupSizeIncomplete = false,
    bool SkipFileVerification = false, bool RetryRetainedFiles = false, bool DeleteDeliveredFiles = false);
public sealed record AcquisitionTaskDeleteSkip(int TaskId, string ReasonCode);
public sealed record AcquisitionTaskDeleteResult(IReadOnlyList<int> DeletedTaskIds, IReadOnlyList<AcquisitionTaskDeleteSkip> Skipped,
    IReadOnlyList<AcquisitionTaskDeleteCleanupOutcome>? CleanupResults = null, int RemovedFiles = 0, long RemovedBytes = 0);
public sealed record AcquisitionTaskDeleteCleanupFile(string Path, long Bytes);
public sealed record AcquisitionTaskDeleteCleanupNode(int RunId, string NodeId, string Kind);
public sealed record AcquisitionTaskDeleteCleanupGroup(string ArtifactId, string Kind, string Directory,
    bool Eligible, string? ReasonCode, IReadOnlyList<AcquisitionTaskDeleteCleanupNode> Nodes,
    IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, int FileCount, long Bytes, bool Shared = false,
    bool FilesTruncated = false, bool SizeUnknown = false);
public sealed record AcquisitionTaskDeleteCleanup(int FileCount, long Bytes,
    IReadOnlyList<AcquisitionTaskDeleteCleanupGroup> Groups, string? ReasonCode = null,
    IReadOnlyList<AcquisitionTaskDeleteSupersededRun>? SupersededRuns = null,
    int IdentifiedFileCount = 0, long IdentifiedBytes = 0, bool SizeIncomplete = false);
public sealed record AcquisitionTaskDeleteSupersededRun(int RunId, int Revision, int ReplacedByRevision);
public sealed record AcquisitionTaskDeleteCleanupOutcome(int TaskId, bool Completed, int RemovedFiles, long RemovedBytes,
    IReadOnlyList<string> ReasonCodes, IReadOnlyList<int>? ClosedRunIds = null,
    IReadOnlyList<string>? RetainedDirectories = null);
