using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record CloudDownloadTarget
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public Bakabase.Modules.Workflow.Artifacts.ArtifactStoragePolicy? StoragePolicy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Bakabase.Modules.Workflow.Artifacts.ArtifactDirectoryScope? DownloadDirectoryScope { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Bakabase.Modules.Workflow.Artifacts.ArtifactDirectoryScope? ProcessingDirectoryScope { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionPartDirectories? StageDirectories { get; init; }
    public List<CloudSource> Sources { get; init; } = [];
    public string? SelectedSourceId { get; init; }
    public string? AccountId { get; init; }
    public CloudRemoteEntry? RemoteEntry { get; init; }
    public int? DownloadTaskId { get; init; }
    public string? DownloadDirectory { get; init; }
    public string? DownloadLayout { get; init; }
    public string? DownloadOperationId { get; init; }
    public string? DownloadArtifactId { get; init; }
    public string? ProcessingOperationId { get; init; }
    public Dictionary<string, string> ProcessingStepArtifacts { get; init; } = [];
    public string? ProcessingIntentKey { get; init; }
    public Bakabase.Service.Components.FileProcessing.FileArtifactDeliveryReview? DeliveryReview { get; init; }
    public string? RecursiveOptionsJson { get; init; }
    public string? ProcessingArchivePolicy { get; init; }
    public string? PublicationOperationId { get; init; }
    public string? PublishedArtifactId { get; init; }
    public string? PublishedDirectory { get; init; }
    public string? PublicationTargetDirectory { get; init; }
    public string? PublicationWaitReason { get; init; }
    public string? PublicationStatus { get; init; }
    public List<string> PublishedFiles { get; init; } = [];
    public string? ProcessedArtifactId { get; init; }
    public string? ExecutionStatus { get; init; }
    public string? CleanupReceiptId { get; init; }
    public string? CleanupStatus { get; init; }
    public string? AcquisitionNonce { get; init; }
    public List<string> Files { get; init; } = [];
    /// <summary>Processing is separate from the original download manifest, which remains intact.</summary>
    public string? ProcessingPlanJson { get; init; }
    public string? ProcessingWaitReason { get; init; }
    public List<string> ProcessedFiles { get; init; } = [];
    public string? ProcessedDirectory { get; init; }
    public string? Problem { get; init; }
    public bool Excluded { get; init; }
    public string? TransportTaskId { get; init; }
    public CloudSource? SelectedSource => Sources.FirstOrDefault(s => s.Id == SelectedSourceId);
    public string? SelectedSourceIdentity => SelectedSource is { } source ? CloudSourceIdentity.Key(source) : null;
}

public sealed record DownloadWorkset : IWorkflowOutputSummary, IWorkflowSourcePayload, IWorkflowOutputMetadata
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? Title { get; init; }
    public int? PostTaskId { get; init; }
    public int PostRevision { get; init; }
    public List<WorkflowSourceRef> SourceRefs { get; init; } = [];
    IReadOnlyList<WorkflowSourceRef> IWorkflowSourcePayload.SourceRefs => SourceRefs.Count > 0 ? SourceRefs :
        PostTaskId is { } id ? [new("postParser", "task", id.ToString(System.Globalization.CultureInfo.InvariantCulture), PostRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))] : [];
    public string OutputDirectory { get; init; } = "";
    /// <summary>Diagnostic recovery may execute never-started downloads, but cannot implicitly retry
    /// or replace an existing acquisition generation. A separate, revalidated selected-target signal
    /// can authorize the original failed task only. Persisted with every subsequent checkpoint.</summary>
    public bool? NoRepeatExistingDownloads { get; init; }
    public List<CloudDownloadTarget> Targets { get; init; } = [];
    public IReadOnlyList<WorkflowOutputBlock> GetWorkflowOutputMetadata(string activityKind)
    {
        var blocks = new List<WorkflowOutputBlock>();
        foreach (var target in Targets)
        {
            void Artifact(string role, string? id, string? operation, string? directory, int count)
            {
                if (id == null) return;
                blocks.Add(new() {Kind = "artifact", Key = target.Id + ":" + role, ArtifactId = id,
                    OperationId = operation, Directory = directory, FileCount = count > 0 ? count : null,
                    LabelKey = activityKind == Bakabase.Service.Components.FileProcessing.AssessFileArtifactDeliveryActivity.ActivityKind && role == "processed"
                        ? target.DeliveryReview is {Accepted: false} ? "cloudDownload.deliveryReview.pendingFiles" : "cloudDownload.deliveryReview.confirmedFiles"
                        : "cloudDownload.artifacts." + role + "ArtifactId",
                    CleanupReceiptId = target.CleanupReceiptId, CleanupStatus = target.CleanupStatus});
            }
            Artifact("download", target.DownloadArtifactId, target.DownloadOperationId, target.DownloadDirectory, target.Files.Count);
            Artifact("processed", target.ProcessedArtifactId, target.ProcessingOperationId, target.ProcessedDirectory, target.ProcessedFiles.Count);
            Artifact("published", target.PublishedArtifactId, target.PublicationOperationId, target.PublishedDirectory, target.PublishedFiles.Count);
            if ((target.TransportTaskId ?? (target.DownloadTaskId is { } id ? "site:" + id : null)) is { } taskId)
                blocks.Add(new() {Kind = "task", Key = target.Id + ":downloadTask", TaskKind = "download", TaskId = taskId,
                    LabelKey = "cloudDownload.viewTask"});
            if (target.RemoteEntry is { } remote)
                blocks.Add(new() {Kind = "remote", Key = target.Id + ":remote", ProviderId = remote.ProviderId,
                    RemotePath = remote.Path, MethodId = target.SelectedSource?.MethodId,
                    LabelKey = "workflow.nodeOutput.remoteLocation"});
            if (target.Excluded || target.ExecutionStatus != null)
                blocks.Add(new() {Kind = "scalar", Key = target.Id + ":status", Value = target.Excluded ? "excluded" : target.ExecutionStatus,
                    LabelKey = "workflow.nodeOutput.executionStatus"});
            if (target.SelectedSource?.AccessCodeRevisions is {Count: > 0} revisions)
                blocks.Add(new() {Kind = "scalar", Key = target.Id + ":accessCodeRevisions",
                    LabelKey = "workflow.nodeOutput.sourceCredentialsCorrected",
                    Value = revisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)});
            if (target.ProcessingWaitReason is { } reason)
                blocks.Add(new() {Kind = "warning", Key = target.Id + ":processing", Code = reason, Severity = "warning"});
            if (target.Problem != null)
            {
                // Never copy upstream diagnostic strings: they can contain signed URLs or secrets.
                blocks.Add(new() {Kind = "warning", Key = target.Id + ":problem", Severity = "warning",
                    Code = ProblemCode(target.Problem)});
            }
        }
        return blocks;
    }
    private static string? ProblemCode(string? problem)
    {
        if (problem == null) return null;
        var match = System.Text.RegularExpressions.Regex.Match(problem, @"\b[A-Z][A-Z0-9_]{2,}(?=[:\s]|$)");
        return match.Success ? match.Value : "SOURCE_ATTENTION_REQUIRED";
    }
    public object ToWorkflowOutputSummary() => new {Id, Title, PostTaskId, PostRevision, SourceRefs, Targets = Targets.Select(t =>
        new {t.Id, t.Title, t.SelectedSourceId, t.DownloadTaskId, t.TransportTaskId, t.Excluded, FileCount = t.Files.Count,
            ProcessedFileCount = t.ProcessedFiles.Count, t.ProcessedDirectory, Problem = ProblemCode(t.Problem),
            t.DownloadOperationId, t.DownloadArtifactId, t.ProcessingOperationId, t.ProcessedArtifactId,
            t.PublicationOperationId, t.PublishedArtifactId, t.PublishedDirectory, t.PublicationStatus, PublishedFileCount = t.PublishedFiles.Count,
            t.ExecutionStatus, t.CleanupReceiptId, t.CleanupStatus})};
}

/// <summary>Current state of a previously bound download at a workflow wait.</summary>
public sealed record CloudDownloadResumeTarget(string TargetId, string DownloadId, string Status, string? Problem, bool CanRetry);

public sealed record CloudSelectionSignal
{
    /// <summary>Frozen server proof for a transferred share whose first local enqueue has not happened.</summary>
    public string? TransferredDownloadPlacement { get; init; }
    /// <summary>Observe existing downloads and persist the no-repeat guard. Restarting selected failed
    /// tasks requires the separate revalidated recovery capability.</summary>
    public bool? ObserveExistingOnly { get; init; }
    /// <summary>When supplied, resume only these existing targets at the download wait node. Omission retains full resume.</summary>
    public List<string>? ResumeTargetIds { get; init; }
    /// <summary>Diagnostic opt-in: revalidate only selected original failed downloads immediately before
    /// resuming them. Does not remove the workset's prohibition on other retries or new generations.</summary>
    public bool? RevalidateExistingDownloads { get; init; }
    /// <summary>Only verify and register original existing files. Never restart a downloader or transfer.</summary>
    public bool? VerifyExistingFilesOnly { get; init; }
    public List<string> SourceIds { get; init; } = [];
    public Dictionary<string, string> SourceMethods { get; init; } = [];
    public Dictionary<string, string> SourceAccounts { get; init; } = [];
    public string? AccountId { get; init; }
    public string? OutputDirectory { get; init; }
    public List<CloudSourceCredentialCorrection> SourceCredentialCorrections { get; init; } = [];
}

public sealed record CloudSourceCredentialCorrection
{
    public required string CorrectionId { get; init; }
    public required string TargetId { get; init; }
    public required string SourceId { get; init; }
    public required string ExpectedSourceIdentity { get; init; }
    public required string? NewAccessCode { get; init; }
    public string Basis { get; init; } = "manual";
    public string? EvidenceReference { get; init; }
    public string? Evidence { get; init; }
}
