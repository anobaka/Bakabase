using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>A versioned intent, independent of its input producer and execution engine.</summary>
public sealed record AcquisitionPlan
{
    /// <summary>Only a reviewed, task-owned local delivery revision may bypass file processing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublishRetainedFiles { get; init; }
    public int Version { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtractionLayout { get; init; }
    /// <summary>Optional local layout for Baidu transfers; null preserves the historical plan contract.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadLayout { get; init; }
    public string Title { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string Delivery { get; init; } = "files";
    /// <summary>Frozen final paths, independent of the download identity. Null preserves the historical layout.</summary>
    public AcquisitionDeliveryLayout? DeliveryLayout { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcquisitionStageDirectories? StageDirectories { get; init; }
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    /// <summary>
    /// Enables the bounded post-download adaptive file assessment.  It is persisted in the
    /// plan so old runs keep their exact node contract while migrated runs can opt in safely.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AdaptiveFileProcessing { get; init; }
    /// <summary>Maximum LLM assessment rounds for each acquired part.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AdaptiveFileProcessingMaxIterations { get; init; } = 3;
    /// <summary>
    /// Optional caller-provided context for a local adaptive retry. It is frozen with the
    /// continuation revision so a later retry can explain the same operator observation
    /// without mutating the original run.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdaptiveFileProcessingContext { get; init; }
    /// <summary>Frozen provider ordering. Existing/manual plans never inherit a changed global preference.</summary>
    public List<string>? SourceProviderPriority { get; init; } = [];
    /// <summary>Every explicitly selected part is required. Sources inside a part are mirrors.</summary>
    public List<AcquisitionPlanPart> Parts { get; init; } = [];
    /// <summary>Optional visual metadata. These candidates are not sources and are not downloaded or published by the plan.</summary>
    public List<AcquisitionCoverCandidate> CoverCandidates { get; init; } = [];
}

public sealed record AcquisitionCoverCandidate
{
    public string Url { get; init; } = "";
    public string Kind { get; init; } = "cover";
    public List<string> Evidence { get; init; } = [];
}

public sealed record AcquisitionPlanPart
{
    public string Id { get; init; } = "";
    public string? Title { get; init; }
    public List<AcquisitionPlanSource> Sources { get; init; } = [];
    public string? SelectedSourceId { get; init; }
    /// <summary>Only a new producer-verified mirror group may opt into automatic pre-start fallback.</summary>
    public string? SelectionMode { get; init; } = "manual";
    public ArtifactStoragePolicyOverride? StoragePolicy { get; init; }
    public FileProcessingPlan? Processing { get; init; }
    public WorkflowSourceRef? ProcessingSource { get; init; }
    public string? AppliedProcessingRevisionId { get; init; }
    /// <summary>Server-verified input for a task-local continuation. Sources remain historical context.</summary>
    public AcquisitionPlanArtifactInput? ContinueFrom { get; init; }
    /// <summary>Task-local choice for a verified part. Older revisions inherit the plan's setting.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseAdaptiveAssessment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryProjectionFingerprint { get; init; }
}

public sealed record AcquisitionPlanArtifactInput(int TaskId, int PreviousRunId, string ArtifactId,
    string OperationId, string Fingerprint);

public sealed record AcquisitionPlanSource
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    /// <summary>The producer's locator before a reviewed redirect resolution. Never another execution candidate.</summary>
    public string? OriginalUrl { get; init; }
    public List<string>? ResolutionChain { get; init; }
    public string Kind { get; init; } = "auto";
    public string? ProviderId { get; init; }
    public string? AccessCode { get; init; }
    public string? ArchivePassword { get; init; }
    public string? MethodId { get; init; }
    public string? AccountId { get; init; }
    /// <summary>Instructions belong to this source; a different mirror may use a different archive format or password.</summary>
    public FileProcessingPlan? Processing { get; init; }
    /// <summary>Unreadable producer instructions are retained as evidence, never treated as an executable plan.</summary>
    public string? OriginalProcessingPlanJson { get; init; }
    public string? ProcessingIssueCode { get; init; }
    public WorkflowSourceRef? ProcessingSource { get; init; }
    public string? AppliedProcessingRevisionId { get; init; }
    /// <summary>Frozen, producer-supplied correction evidence; source history remains unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonConverter(typeof(Bakabase.Modules.Acquisition.Abstractions.Serialization.JsonElementNewtonsoftConverter))]
    public JsonElement? SourceCorrection { get; init; }
}

public sealed record AcquisitionPlanRevision(int Revision, AcquisitionPlan Plan,
    IReadOnlyList<WorkflowSourceRef> SourceRefs,
    [property: Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))] DateTime CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PreviousRunId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AcquisitionPlanArtifactEvidence>? ArtifactEvidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AcquisitionPlanSourceRevisionEvidence? SourceRevision = null);

/// <summary>A task-local source decision. Exact sources remain in the immutable plan revisions.</summary>
public sealed record AcquisitionPlanSourceRevisionEvidence(string DecisionId, string PartId,
    string PreviousSourceId, string SelectedSourceId, string Evidence, string EvidenceVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SelectedExecutionIdentity = null);

/// <summary>Task-local observations retain the exact validated input manifest; they are not post-author instructions.</summary>
public sealed record AcquisitionPlanArtifactEvidence(string PartId, string ArtifactId, string OperationId,
    string Kind, string RootDirectory, string Fingerprint, ArtifactManifest Manifest);

/// <summary>Empty root inherits the task's saved download directory plus Acquired, never live global settings.</summary>
public sealed record AcquisitionDeliverySettings
{
    public string RootDirectory { get; init; } = "";
    public string DirectoryTemplate { get; init; } = "{Title} [A{TaskId}]";
    public string PartDirectoryTemplate { get; init; } = "{PartTitle} [{PartId}]";
    /// <summary>Frozen caller override for the generic publication node. Null preserves historical layouts.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArtifactDeliveryPolicy? ContentLayout { get; init; }
}

/// <summary>Server-resolved snapshot. Empty paths mean a new draft still needs its download root.</summary>
public sealed record AcquisitionDeliveryLayout
{
    public int Version { get; init; } = 1;
    public AcquisitionDeliverySettings Settings { get; init; } = new();
    public int TaskId { get; init; }
    public string RootDirectory { get; init; } = "";
    public string TaskDirectory { get; init; } = "";
    public Dictionary<string, string> PartDirectories { get; init; } = [];
}
