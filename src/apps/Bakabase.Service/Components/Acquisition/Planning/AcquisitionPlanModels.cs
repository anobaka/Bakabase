using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionPlanBuilderDescriptor(string Id, string NameKey, string DescriptionKey,
    IReadOnlyList<string> EntryPoints, string InputKind);
public sealed record AcquisitionPlanCandidate(string Id, string Title, string Url, string? CoverUrl = null);
public sealed record AcquisitionPlanIssue(string Code, string Message, bool Blocking = true);
public sealed record AcquisitionPlanStepView(string Id, string Kind, string LabelKey,
    string? Operation = null, string? Input = null, string? Selector = null, string? TargetName = null,
    string? TargetDirectory = null, string? Extension = null, IReadOnlyList<string>? Inputs = null,
    string? FindText = null, string? ReplaceText = null, string? SourceDirectory = null)
{
    public string? Stage { get; init; } = AcquisitionStageHistory.StageFor(Kind);
}
public sealed record AcquisitionPlanPreparationAttempt(int Attempt, string Query, DateTime StartedAt, DateTime? CompletedAt, string Status, int CandidateCount, string? Code = null);
public sealed record AcquisitionPlanTaskDetail(AcquisitionTask Task, AcquisitionPlan? Plan,
    IReadOnlyList<AcquisitionPlanCandidate> Candidates, string? Query, IReadOnlyList<AcquisitionPlanIssue> Issues,
    IReadOnlyList<AcquisitionPlanStepView> Steps, IReadOnlyList<WorkflowSourceRef> SourceRefs)
{
    public int Id => Task.Id;
    public IReadOnlyList<AcquisitionStageAttempt> StageAttempts { get; init; } = [];
    public AcquisitionStageDirectories? StageDirectories { get; init; }
    public AcquisitionDeliverySettings? DeliverySettings { get; init; }
    public bool CanEditDeliverySettings { get; init; }
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    public bool CanEditStoragePolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanKeepPreparedFiles { get; init; }
    public IReadOnlyList<AcquisitionPlanRevision> PlanHistory { get; init; } = [];
    public IReadOnlyList<AcquisitionPlanPreparationAttempt> PreparationAttempts { get; init; } = [];
    public IReadOnlyList<AcquisitionPlanArtifactCandidate> ReplanArtifacts { get; init; } = [];
    public IReadOnlyList<AcquisitionDraftSource> DraftSources { get; init; } = [];
    public IReadOnlyList<AcquisitionLegacyLeadSnapshot> LegacyLeads { get; init; } = [];
    public string? LegacyTitle { get; init; }
    public AcquisitionDraftListRow? ImportedListRow { get; init; }
}
public sealed record AcquisitionPlanArtifactCandidate(string PartId, string ArtifactId, string Kind,
    string RootDirectory, string Fingerprint, int FileCount, bool? CanRepublish = null, bool? CanRetryDelivery = null,
    bool? CanReadCurrentDirectory = null);
public sealed record AcquisitionPlanReplanInput(int ExpectedRevision, int ExpectedRunId, string Reason,
    IReadOnlyList<AcquisitionPlanProcessingReplacement> Items, bool UseCurrentStorageDefaults = false,
    ArtifactDeliveryPolicy? DeliveryContentLayout = null,
    IReadOnlyList<AcquisitionPlanCompositeSource>? CompositeInputs = null,
    bool? EnableAdaptiveAssessment = null, int? MaxAdaptiveIterations = null,
    string? AdaptiveContext = null, string? DeliveryRootDirectory = null, bool? RetryDeliveryOnly = null);
public sealed record AcquisitionPlanCompositeSource(string PartId, string ArtifactId);
public sealed record AcquisitionPlanProcessingReplacement(string PartId, FileProcessingPlan Plan,
    IReadOnlyList<string> ArtifactEvidenceIds, string? ContinueFromArtifactId = null,
    string? ExpectedDeliveryProjectionFingerprint = null, bool? UseAdaptiveAssessment = null,
    string? InputMode = null, string? InputStage = null);
public sealed record AcquisitionPlanCreationInput(string Title, string BuilderId, string? OutputDirectory = null,
    AcquisitionDeliverySettings? DeliverySettings = null, ArtifactStoragePolicy? StoragePolicy = null);
public sealed record AcquisitionPlanSearchInput(string Query);
public sealed record AcquisitionPlanSelectionInput(string CandidateId, int ExpectedRevision);
public sealed record AcquisitionPlanRunInput(int ExpectedRevision, bool? UseEmergencySpaceReserve = null);
public sealed record AcquisitionKeepPreparedFilesInput(int ExpectedRevision, string? Reason = null);
public sealed record AcquisitionPlanUpdateInput(int ExpectedRevision, string? OutputDirectory = null,
    AcquisitionDeliverySettings? DeliverySettings = null, ArtifactStoragePolicy? StoragePolicy = null,
    IReadOnlyList<AcquisitionStoragePolicyOverrideInput>? StoragePolicyOverrides = null);
public sealed record AcquisitionStoragePolicyOverrideInput(string PartId, ArtifactStoragePolicyOverride? Policy, string? StepId = null);
internal sealed record AcquisitionPlanBuilderState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcquisitionStageDirectoryChangeJournal? DirectoryChange { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessingStageCommitId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessingStageInputId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcquisitionLocalRecoveryQueueEntry? LocalRecoveryQueue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcquisitionLocalRecoveryQueueEntry>? LocalRecoveryQueueHistory { get; init; }
    public AcquisitionDownloadContinuation? DownloadContinuation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcquisitionLocalFilesRecoveryReceipt? LocalFilesRecovery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcquisitionDeliveryRetirement>? PendingDeliveryRetirements { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AcquisitionDeliveryRetirement>? DeliveryRetirementResults { get; init; }
    public IReadOnlyList<AcquisitionCreationOrigin> CreationOrigins { get; init; } = [];
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    public AcquisitionDeliverySettings? DeliverySettings { get; init; }
    public AcquisitionDeliveryLayout? DeliveryLayout { get; init; }
    public string? Query { get; init; }
    public List<AcquisitionPlanPreparationAttempt> Attempts { get; init; } = [];
    public List<AcquisitionPlanCandidate> Candidates { get; init; } = [];
    public List<WorkflowSourceRef> SourceRefs { get; init; } = [];
    public List<AcquisitionPlanIssue> Issues { get; init; } = [];
    public List<AcquisitionDraftSource> DraftSources { get; init; } = [];
    public List<AcquisitionLegacyLeadSnapshot> LegacyLeads { get; init; } = [];
    public int? LegacyResourceId { get; init; }
    public string? ImportKey { get; init; }
    public string? LegacyTitle { get; init; }
    public AcquisitionDraftListRow? ImportedListRow { get; init; }
}

public sealed record AcquisitionDraftSource
{
    public string Id { get; init; } = "";
    public string? Title { get; init; }
    public string Url { get; init; } = "";
    public string Kind { get; init; } = "auto";
    public string? ProviderId { get; init; }
    public string? MethodId { get; init; }
    public string? AccountId { get; init; }
    public string? AccessCode { get; init; }
    public string? ArchivePassword { get; init; }
    public FileProcessingPlan? Processing { get; init; }
}
public sealed record AcquisitionLegacyLeadSnapshot(string Id, AcquisitionLeadKind Kind, string Value,
    bool IsResolved, string? Note = null, string? AccessCode = null, string? Password = null,
    string? ExtractionPlanJson = null, string? SourceReference = null,
    IReadOnlyList<WorkflowSourceRef>? SourceRefs = null, string? SourceRefsJson = null);
public sealed record AcquisitionDraftCreationInput(string? Title = null,
    IReadOnlyList<AcquisitionDraftSource>? Sources = null, string? OutputDirectory = null,
    AcquisitionDeliverySettings? DeliverySettings = null, ArtifactStoragePolicy? StoragePolicy = null);
public sealed record AcquisitionDraftUpdateInput(int ExpectedRevision, string? Title = null,
    IReadOnlyList<AcquisitionDraftSource>? Sources = null, string? OutputDirectory = null,
    AcquisitionDeliverySettings? DeliverySettings = null, ArtifactStoragePolicy? StoragePolicy = null);
public sealed record AcquisitionDraftBuilderInput(int ExpectedRevision, string BuilderId);
public sealed record AcquisitionDraftPreparationInput(int ExpectedRevision, AcquisitionPlan Plan);
public sealed record AcquisitionLegacyImportInput(IReadOnlyList<int> ResourceIds);
public sealed record AcquisitionDraftListInput(IReadOnlyList<AcquisitionDraftListRow> Rows);
public sealed record AcquisitionDraftListRow(string? Title, string? Url, string? Password, int LineNumber);
public sealed record AcquisitionDraftImportResult(int Created, int Matched,
    IReadOnlyList<int> TaskIds, IReadOnlyList<string> Problems);
