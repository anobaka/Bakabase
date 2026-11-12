using System;
using System.Collections.Generic;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRecoveryPreviewInput(IReadOnlyList<int> TaskIds, string? Scope = null,
    string? DiagnosisId = null);
public sealed record AcquisitionRecoveryApplyInput(string PreviewToken, IReadOnlyList<int> TaskIds, bool? UseEmergencySpaceReserve = null);
public sealed record AcquisitionRecoveryRow(int TaskId, string? Title, int? WorkflowRunId, bool CanContinue,
    string? Action, string ReasonCode, string? ManualAction = null, DateTimeOffset? EvidenceAt = null,
    string? CurrentStepKind = null, string? CurrentProblemCode = null,
    AcquisitionLocalFilesRecoveryPreview? LocalFiles = null, bool CanOverrideStorage = false);
public sealed record AcquisitionLocalFilesRecoveryPart(string PartId, int DownloadTaskId, int DownloadResultId,
    string Fingerprint, int FileCount, string SourceDirectory, string? OriginalWorkingDirectory,
    string WorkingDirectory, string? OriginalDeliveryDirectory, string DeliveryDirectory, string? ArtifactId = null);
public sealed record AcquisitionLocalFilesRecoveryPreview(IReadOnlyList<AcquisitionLocalFilesRecoveryPart> Parts);
public sealed record AcquisitionLocalFilesRecoveryOutcome(int TaskId, int PreviousWorkflowRunId, int? WorkflowRunId,
    int PlanRevision, string Status, string? ReasonCode = null);
public sealed record AcquisitionRecoveryPreview(string PreviewToken, DateTimeOffset ExpiresAt, int TotalCount,
    int ContinuableCount, int ManualCount, IReadOnlyList<AcquisitionRecoveryRow> Rows, string Scope = "workflow");
public sealed record AcquisitionRecoverySkip(int TaskId, string ReasonCode);
public sealed record AcquisitionDownloadRecoveryOutcome(int TaskId, IReadOnlyList<int> StartedDownloadTaskIds,
    IReadOnlyList<int> IndeterminateDownloadTaskIds, string? ReasonCode = null);
public sealed record AcquisitionRecoveryResult(IReadOnlyList<int> ContinuedTaskIds, IReadOnlyList<AcquisitionRecoverySkip> Skipped,
    IReadOnlyList<AcquisitionDownloadRecoveryOutcome>? Downloads = null,
    IReadOnlyList<AcquisitionLocalFilesRecoveryOutcome>? LocalFiles = null);
