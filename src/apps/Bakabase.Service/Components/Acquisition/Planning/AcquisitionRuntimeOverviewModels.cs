using System;
using System.Collections.Generic;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRuntimeOverviewInput(IReadOnlyList<int> TaskIds);

/// <summary>A bounded read-only projection of existing execution evidence, never a new availability check.</summary>
public sealed record AcquisitionRuntimeOverview
{
    public int TaskId { get; init; }
    public int PlanRevision { get; init; }
    public int? WorkflowRunId { get; init; }
    public string? WorkflowStatus { get; init; }
    public int? CurrentStepIndex { get; init; }
    public int? StepCount { get; init; }
    public string? CurrentNodeKind { get; init; }
    public WorkflowCapabilityRuntime? Capability { get; init; }
    public string? Stage { get; init; }
    public double? Progress { get; init; }
    public string? ProgressScope { get; init; }
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public long? EstimatedRemainingSeconds { get; init; }
    public bool BytesPartial { get; init; }
    public int DownloadCount { get; init; }
    public int ActiveDownloadCount { get; init; }
    public int CompletedDownloadCount { get; init; }
    public int FailedDownloadCount { get; init; }
    public string? DownloadActivity { get; init; }
    /// <summary>Persisted continuation intent for this run, or manual for an unclassified legacy run.</summary>
    public string? DownloadContinuation { get; init; }
    public bool DownloadsCompletedAwaitingWorkflow { get; init; }
    /// <summary>Offers diagnosis, not a claim that file validation or downstream storage admission passed.</summary>
    public bool CanDiagnoseDownloadContinuation { get; init; }
    public string? ProblemCode { get; init; }
    public string? ProblemReasonCode { get; init; }
    public IReadOnlyList<string> AttentionTags { get; init; } = [];
    public string? Problem { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<AcquisitionRuntimeSourceObservation> Sources { get; init; } = [];
    public bool SourcesTruncated { get; init; }
}

public sealed record AcquisitionRuntimeSourceObservation
{
    public string PartId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Url { get; init; } = "";
    public string? AccessCode { get; init; }
    public string ProviderId { get; init; } = "unknown";
    public bool Selected { get; init; }
    public string Status { get; init; } = "unknown";
    public string? ReasonCode { get; init; }
    public string? NormalizedReasonCode { get; init; }
    public string? Message { get; init; }
    public string? EvidenceKind { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool IsHistorical { get; init; }
    public string? DownloadTaskId { get; init; }
}
