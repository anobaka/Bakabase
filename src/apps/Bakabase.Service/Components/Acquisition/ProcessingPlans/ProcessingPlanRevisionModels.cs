using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.ProcessingPlans;

public sealed record ProcessingPlanEvidence(string? StepId, string Quote);
public sealed record ProcessingPlanRevisionInput(WorkflowSourceRef Source, string ExpectedSourceHash,
    int ExpectedRevision, FileProcessingPlan Plan, IReadOnlyList<ProcessingPlanEvidence> Evidence, string Reason);
public sealed record ProcessingPlanRevisionView(string Id, int Revision, string SourceHash,
    FileProcessingPlan? OriginalPlan, FileProcessingPlan Plan, IReadOnlyList<ProcessingPlanEvidence> Evidence,
    string Reason, [property: Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))] DateTimeOffset CreatedAt);
public sealed record SourceProcessingPlanView(WorkflowSourceRef Source, string SourceHash, int Revision,
    FileProcessingPlan? OriginalPlan, FileProcessingPlan? EffectivePlan, int EffectiveRevision,
    IReadOnlyList<ProcessingPlanRevisionView> History, bool HasStaleRevisions,
    bool SourceAvailable = true, string? SourceProblem = null);
public sealed record ApplyProcessingPlanRevisionItem(string PartId, WorkflowSourceRef Source, string RevisionId);
public sealed record ApplyProcessingPlanRevisionsInput(int ExpectedRevision, IReadOnlyList<ApplyProcessingPlanRevisionItem> Items);

/// <summary>Only source owners read their saved original instructions and body; never fetch remote evidence.</summary>
public interface IProcessingPlanSourceReader
{
    bool Supports(WorkflowSourceRef source);
    Task<ProcessingPlanSourceSnapshot> ReadAsync(WorkflowSourceRef source, CancellationToken ct);
}

public sealed record ProcessingPlanSourceSnapshot(WorkflowSourceRef Source, FileProcessingPlan? OriginalPlan,
    IReadOnlyList<string> EvidenceTexts, string SourceFingerprint, string? SourceLocator = null);
