using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRecoveryPreviewInput(IReadOnlyList<int> TaskIds, string? Scope = null,
    string? DiagnosisId = null);
public sealed record AcquisitionRecoveryApplyInput(string PreviewToken, IReadOnlyList<int> TaskIds, bool? UseEmergencySpaceReserve = null,
    bool? AnalyzeFilesFirst = null, int? MaxAdaptiveIterations = null, bool? PrepareOnly = null);
public sealed record AcquisitionRecoveryRow(int TaskId, string? Title, int? WorkflowRunId, bool CanContinue,
    string? Action, string ReasonCode, string? ManualAction = null, DateTimeOffset? EvidenceAt = null,
    string? CurrentStepKind = null, string? CurrentProblemCode = null,
    AcquisitionLocalFilesRecoveryPreview? LocalFiles = null, bool CanOverrideStorage = false);
public sealed record AcquisitionLocalFilesRecoveryPart(string PartId, int DownloadTaskId, int DownloadResultId,
    string Fingerprint, int FileCount, string SourceDirectory, string? OriginalWorkingDirectory,
    string WorkingDirectory, string? OriginalDeliveryDirectory, string DeliveryDirectory, string? ArtifactId = null);
public sealed record AcquisitionLocalFilesRecoveryPreview(IReadOnlyList<AcquisitionLocalFilesRecoveryPart> Parts)
{
    // Set only on the public preview projection. Receipts and evidence hashes keep their original representation.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsPrepareOnly { get; init; }
}
public sealed record AcquisitionLocalFilesRecoveryOutcome(int TaskId, int PreviousWorkflowRunId, int? WorkflowRunId,
    int PlanRevision, string Status, string? ReasonCode = null);
public sealed record AcquisitionRecoveryPreview(string PreviewToken, DateTimeOffset ExpiresAt, int TotalCount,
    int ContinuableCount, int ManualCount, IReadOnlyList<AcquisitionRecoveryRow> Rows, string Scope = "workflow");
public sealed record AcquisitionRecoverySkip(int TaskId, string ReasonCode);
public sealed record AcquisitionDownloadRecoveryOutcome(int TaskId, IReadOnlyList<int> StartedDownloadTaskIds,
    IReadOnlyList<int> IndeterminateDownloadTaskIds, string? ReasonCode = null,
    IReadOnlyList<int>? VerifiedDownloadTaskIds = null);
public sealed record AcquisitionRecoveryResult(IReadOnlyList<int> ContinuedTaskIds, IReadOnlyList<AcquisitionRecoverySkip> Skipped,
    IReadOnlyList<AcquisitionDownloadRecoveryOutcome>? Downloads = null,
    IReadOnlyList<AcquisitionLocalFilesRecoveryOutcome>? LocalFiles = null);
