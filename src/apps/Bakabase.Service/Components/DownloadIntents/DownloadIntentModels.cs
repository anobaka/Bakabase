using System.Collections.Generic;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.View;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>A reusable acquisition candidate. It contains no post-parser task or browser state.</summary>
public sealed record DownloadCandidate
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? GroupId { get; init; }
    public bool? MirrorGroupConfirmed { get; init; }
    public string Kind { get; init; } = "auto";
    public string? ProviderId { get; init; }
    public string? AccessCode { get; init; }
    public string? ArchivePassword { get; init; }
    public string? ExtractionPlanJson { get; init; }
    public string? Version { get; init; }
    public string? MethodId { get; init; }
    public string? AccountId { get; init; }
    /// <summary>Server-frozen user-reviewed source evidence; never an automatic OCR claim.</summary>
    [Newtonsoft.Json.JsonConverter(typeof(Bakabase.Modules.Acquisition.Abstractions.Serialization.JsonElementNewtonsoftConverter))]
    public JsonElement? SourceCorrection { get; init; }
}

public sealed record DownloadRequirement(string Code, string Kind, string Message,
    string? ComponentId = null, string? AccountProvider = null, IReadOnlyList<string>? Actions = null);

public sealed record DownloadMethodView(string Id, string DisplayName, string ProviderId, string Transport,
    bool Supported, bool Ready, string State, IReadOnlyList<DownloadRequirement> Requirements,
    IReadOnlyList<string> DependencyIds, bool SupportsTransfer = false, string? AccountProvider = null,
    string? EngineId = null, IReadOnlyList<string>? SourceKinds = null, string? Scope = null);

public sealed record DownloadCandidateView(DownloadCandidate Candidate, IReadOnlyList<DownloadMethodView> Methods,
    bool CanSelect, string State, IReadOnlyList<DownloadRequirement> Requirements, CloudSource? ResolvedSource = null,
    string? SelectedMethodId = null, ProcessingPlanPreflightResult? ProcessingPlan = null,
    SourceAvailabilityResult? Availability = null, bool? SourceDisabled = null, bool? CanSelectManually = null);

public sealed record DownloadIntentPreflight(string Mode, IReadOnlyList<DownloadCandidateView> Candidates,
    IReadOnlyList<DownloadRequirement> Requirements, bool CanSubmit);

public sealed record DownloadIntentRequest
{
    // Server-owned acquisition grants only. Ordinary intent submission cannot supply these.
    internal IReadOnlyDictionary<string, string> AllowedUnavailableSourceFingerprints { get; init; } = new Dictionary<string, string>();
    public string? AvailabilityContextKey { get; init; }
    public bool? CheckSourceAvailability { get; init; }
    /// <summary>Frozen executions retain their source and reuse existing artifacts regardless of later share observations.</summary>
    public bool? IgnoreSourceAvailability { get; init; }
    public string Mode { get; init; } = "files";
    public string? Title { get; init; }
    public string? AccountId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public List<DownloadCandidate> Candidates { get; init; } = [];
    public List<string> SelectedCandidateIds { get; init; } = [];
}

/// <summary>The boundary adapter re-reads candidates and stamps source references on the server.</summary>
public sealed record PostDownloadIntentRequest
{
    public int TaskId { get; init; }
    public int Revision { get; init; }
    public string Mode { get; init; } = "files";
    public string? Title { get; init; }
    public string? AccountId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public List<int> ResourceIndices { get; init; } = [];
    public Dictionary<string, string> CandidateMethods { get; init; } = [];
    public Dictionary<string, string> CandidateAccounts { get; init; } = [];
    /// <summary>Explicit user edits to instructions only; saved parsing results remain unchanged.</summary>
    public Dictionary<string, string> CandidateProcessingPlans { get; init; } = [];
    /// <summary>Opaque hash of the exact saved result, obtained from a server selection preview.</summary>
    public string? ExpectedResultsHash { get; init; }
    /// <summary>Task-local, user-reviewed source corrections. Prior parsing results remain immutable.</summary>
    public Dictionary<string, PostCandidateSourceCorrection> CandidateSourceCorrections { get; init; } = [];
}

public sealed record PostCandidateSourceCorrection
{
    public int ResourceIndex { get; init; }
    public string ExpectedOriginalUrl { get; init; } = "";
    public string Url { get; init; } = "";
    public string? AccessCode { get; init; }
    public string Codec { get; init; } = "qr";
    /// <summary>mainHtml or commentHtmlList; CommentIndex is required only for the latter.</summary>
    public string ImageScope { get; init; } = "mainHtml";
    public int? CommentIndex { get; init; }
    public int ImageIndex { get; init; }
    public string ImageUrl { get; init; } = "";
    public string SourceHtmlSha256 { get; init; } = "";
    public string ImageSha256 { get; init; } = "";
    public string DecodedValue { get; init; } = "";
}

public sealed record DownloadIntentSubmission(string Mode, IReadOnlyList<WorkflowRunViewModel> Runs,
    IReadOnlyList<int> ResourceIds, IReadOnlyList<int> AcquisitionTaskIds);
