using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A producer proves same-content membership; the execution service knows no producer-specific rules.</summary>
public sealed record AcquisitionSourceRevisionCandidate(string Id, string PartId, AcquisitionPlanSource Source,
    IReadOnlyList<WorkflowSourceRef> SourceRefs, string Evidence, string EvidenceVersion);
public interface IAcquisitionSourceRevisionCandidates
{
    Task<IReadOnlyList<AcquisitionSourceRevisionCandidate>> ReadAsync(AcquisitionPlan plan,
        IReadOnlyList<WorkflowSourceRef> sourceRefs, CancellationToken ct);
}
public sealed record AcquisitionSourceRevisionOptions(int TaskId, int ExpectedRevision, int? ExpectedRunId,
    bool CanRevise, string? ReasonCode, AcquisitionPlanSource? CurrentSource,
    IReadOnlyList<AcquisitionSourceRevisionCandidate> Candidates);
public sealed record AcquisitionSourceRevisionPreviewInput(int ExpectedRevision, int ExpectedRunId,
    string PartId, string CandidateId);
public sealed record AcquisitionSourceRevisionPreview(string PreviewToken, DateTimeOffset ExpiresAt,
    int TaskId, int ExpectedRevision, int ExpectedRunId, string PartId, bool CanApply, string? ReasonCode,
    AcquisitionPlanSource CurrentSource, AcquisitionPlanSource ProposedSource,
    Bakabase.Modules.Workflow.Artifacts.FileProcessingPlan? CurrentProcessing,
    Bakabase.Modules.Workflow.Artifacts.FileProcessingPlan? ProposedProcessing,
    bool ProcessingChanged, string Evidence, DownloadIntentPreflight Preflight);
public sealed record AcquisitionSourceRevisionApplyInput(string PreviewToken);
public sealed record AcquisitionSourceRevisionResult(int TaskId, int PlanRevision, int PreviousRunId,
    bool Applied);
