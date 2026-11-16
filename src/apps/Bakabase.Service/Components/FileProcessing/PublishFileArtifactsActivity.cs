using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileArtifactPublicationConfig
{
    public string? TargetDirectory { get; init; }
    /// <summary>Exact immutable per-item destinations. Null retains the legacy directory strategy.</summary>
    public Dictionary<string, string>? TargetDirectories { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? StageDirectoryBindingIds { get; init; }
    public string Source { get; init; } = "processed";
    public string ReusePolicy { get; init; } = "reuseVerified";
    /// <summary>Null keeps the exact historical layout and publication identity.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArtifactDeliveryPolicy? ContentLayout { get; init; }
}
public sealed record FileArtifactPublicationSignal(string? TargetId = null, string? TargetDirectory = null);

public sealed class PublishFileArtifactsActivity : IResumableWorkflowActivity, IWorkflowArtifactDemandActivity
{
    public string Kind => "fs.publishArtifacts";
    public string DisplayName => "Deliver file artifacts";
    public string Description => "Copy the exact verified artifact into a user directory. Keep shared originals, verify every delivered file and never overwrite conflicting files. A completed delivery can satisfy future runs even when temporary inputs were cleaned.";
    public string DescriptionKey => "workflow.activity.fsPublishArtifacts.description";
    public string Group => "fs";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType, AcquisitionWorkflowKinds.ItemAcquisition];

    private static FileArtifactPublicationConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(json, WorkflowJson.Options) ?? new();
        if (config.Source is not ("processed" or "download") || config.ReusePolicy is not ("reuseVerified" or "force"))
            throw new ArgumentException("Choose a valid source artifact and publication reuse policy.");
        if (config.ContentLayout is {Version: not 1}) throw new ArgumentException("DELIVERY_CONTENT_LAYOUT_INVALID");
        if (!string.IsNullOrWhiteSpace(config.TargetDirectory) && !Path.IsPathFullyQualified(config.TargetDirectory))
            throw new ArgumentException("The delivery directory must be absolute.");
        if (config.TargetDirectories != null && (config.TargetDirectories.Count > 100 || config.TargetDirectories.Any(pair =>
            string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || string.IsNullOrWhiteSpace(pair.Value) || !Path.IsPathFullyQualified(pair.Value))))
            throw new ArgumentException("The delivery mapping requires bounded target identities and absolute directories.");
        if (config.StageDirectoryBindingIds != null && (config.StageDirectoryBindingIds.Count > 100 || config.StageDirectoryBindingIds.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || !Guid.TryParseExact(pair.Value, "N", out _))))
            throw new ArgumentException("STAGE_DIRECTORY_BINDING_INVALID");
        return config;
    }
    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try
        {
            var config = Parse(context.ConfigJson);
            if (!string.IsNullOrWhiteSpace(config.TargetDirectory))
                context.Services.GetRequiredService<FileArtifactPublisher>().ValidateTarget(config.TargetDirectory);
            foreach (var directory in config.TargetDirectories?.Values ?? Enumerable.Empty<string>())
                context.Services.GetRequiredService<FileArtifactPublisher>().ValidateTarget(directory);
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([]);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([new() {Code = "publicationConfigInvalid", Message = error.Message}]); }
    }

    public static string CloudTargetDirectory(DownloadWorkset item, CloudDownloadTarget target, FileArtifactPublicationConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.TargetDirectory))
            return item.Targets.Count(t => !t.Excluded) == 1 ? Path.GetFullPath(config.TargetDirectory) :
                Path.Combine(Path.GetFullPath(config.TargetDirectory), StableTarget(target));
        if (config.TargetDirectories != null)
            return config.TargetDirectories.TryGetValue(target.Id, out var directory) ? Path.GetFullPath(directory) :
                throw new InvalidOperationException("DELIVERY_DIRECTORY_UNRESOLVED: The frozen plan needs a destination for this item.");
        return Path.Combine(Path.GetFullPath(item.OutputDirectory), StableTarget(target), "result");
    }
    private static string StableTarget(CloudDownloadTarget target) => CloudOperationService.Key("delivery-target-v1", DownloadArtifactCoordinator.Source(target))[..24];

    internal static string? CurrentStageBinding(CloudDownloadTarget target, FileArtifactPublicationConfig config, string directory)
    {
        if (config.StageDirectoryBindingIds?.TryGetValue(target.Id, out var id) != true) return null;
        var binding = target.StageDirectories?.Delivery;
        if (!Guid.TryParseExact(id, "N", out _) || binding?.BindingId != id ||
            !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(binding.Directory), Path.GetFullPath(directory)))
            throw new InvalidOperationException("STAGE_DIRECTORY_BINDING_CHANGED");
        return id;
    }

    public async Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson, CancellationToken ct)
    {
        var config = Parse(configJson);
        if (config.ReusePolicy == "force") return item;
        var publisher = context.Services.GetRequiredService<FileArtifactPublisher>();
        if (item is AcquisitionWorkItem local)
        {
            var variables = local.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
            var intent = LocalSourceIntent(variables, config.Source);
            var directory = LocalTargetDirectory(local, config);
            if (intent == null || string.IsNullOrWhiteSpace(directory)) return item;
            var published = await publisher.ResolveAsync(intent, directory, ct, config.ContentLayout);
            if (published == null) return item;
            await ArtifactWorkflowBindings.BindAsync(context, "files:artifact.publish", published.OperationId, ct);
            variables["artifact.publishedId"] = published.Id;
            variables["artifact.publicationId"] = published.OperationId;
            if (config.Source == "download") variables["artifact.inputId"] = published.Id;
            else variables["artifact.outputId"] = published.Id;
            return local with {Variables = variables, AlreadyProcessed = true, Files = published.Files,
                WorkingDirectory = config.Source == "download" ? published.RootDirectory : local.WorkingDirectory,
                ExtractedDirectory = published.RootDirectory, TargetDirectory = published.RootDirectory, PreserveDirectoryStructure = true};
        }
        if (item is not DownloadWorkset workset) return item;
        var targets = new List<CloudDownloadTarget>();
        foreach (var target in workset.Targets)
        {
            if (target.Excluded || target.SelectedSource == null || target.AcquisitionNonce != null) { targets.Add(target); continue; }
            var sourceIntent = config.Source == "processed" ? target.ProcessingIntentKey : DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, context.Services.GetService<FileProcessingWorkspaceResolver>());
            if (sourceIntent == null) { targets.Add(target); continue; }
            var directory = CloudTargetDirectory(workset, target, config);
            var artifact = await publisher.ResolveAsync(sourceIntent, directory, ct, config.ContentLayout, CurrentStageBinding(target, config, directory));
            if (artifact != null) await ArtifactWorkflowBindings.BindAsync(context, target.Id + ":artifact.publish", artifact.OperationId, ct);
            targets.Add(artifact == null ? target : ApplyCloud(target, artifact));
        }
        return workset with {Targets = targets};
    }

    /// <summary>Called by a preceding processor's demand planner before it considers reacquiring its inputs.</summary>
    public static async Task<ArtifactRecord?> ResolveFollowingPublicationAsync(WorkflowExecutionContext context,
        DownloadWorkset item, CloudDownloadTarget target, string processingIntent, CancellationToken ct)
    {
        foreach (var next in context.FollowingActivities)
        {
            var activity = context.Services.GetRequiredService<IWorkflowActivityRegistry>().Get(next.Kind);
            if (activity is PublishFileArtifactsActivity)
            {
                var config = Parse(next.ConfigJson);
                if (config.Source != "processed" || config.ReusePolicy == "force") return null;
                var directory = CloudTargetDirectory(item, target, config);
                return await context.Services.GetRequiredService<FileArtifactPublisher>()
                    .ResolveAsync(processingIntent, directory, ct, config.ContentLayout, CurrentStageBinding(target, config, directory));
            }
            // This explicit review still executes later, against the published artifact's
            // verified manifest and source-processing metadata. Looking through it here only
            // prevents reacquiring already delivered bytes after private inputs were cleaned.
            if (activity is AssessFileArtifactDeliveryActivity) continue;
            if (activity is not IWorkflowArtifactTransparentActivity transparent ||
                !transparent.PreservesArtifactDemand(next.ConfigJson)) return null;
        }
        return null;
    }

    private static string? LocalSourceIntent(IReadOnlyDictionary<string, string> variables, string source) =>
        source == "processed" ? variables.GetValueOrDefault("artifact.processingIntent") :
            variables.GetValueOrDefault("download.intentKey") ?? variables.GetValueOrDefault("artifact.downloadIntent");
    private static string? LocalTargetDirectory(AcquisitionWorkItem item, FileArtifactPublicationConfig config) =>
        string.IsNullOrWhiteSpace(config.TargetDirectory) ? item.TargetDirectory ?? item.Variables.GetValueOrDefault("artifact.publicationTargetDirectory") : config.TargetDirectory;

    public static async Task<ArtifactRecord?> ResolveFollowingLocalPublicationAsync(WorkflowExecutionContext context,
        AcquisitionWorkItem item, string processingIntent, CancellationToken ct)
    {
        var registry = context.Services.GetService<IWorkflowActivityRegistry>();
        if (registry == null) return null;
        foreach (var next in context.FollowingActivities)
        {
            var activity = registry.Get(next.Kind);
            if (activity is PublishFileArtifactsActivity)
            {
                var config = Parse(next.ConfigJson);
                var directory = LocalTargetDirectory(item, config);
                if (config.Source != "processed" || config.ReusePolicy == "force" || string.IsNullOrWhiteSpace(directory)) return null;
                return await context.Services.GetRequiredService<FileArtifactPublisher>().ResolveAsync(processingIntent, directory, ct, config.ContentLayout);
            }
            if (activity is not IWorkflowArtifactTransparentActivity transparent || !transparent.PreservesArtifactDemand(next.ConfigJson)) return null;
        }
        return null;
    }

    internal static CloudDownloadTarget ApplyCloud(CloudDownloadTarget target, ArtifactRecord artifact) => target with
    {
        PublicationOperationId = artifact.OperationId, PublishedArtifactId = artifact.Id,
        PublishedDirectory = artifact.RootDirectory, PublishedFiles = artifact.Files.ToList(),
        PublicationTargetDirectory = artifact.RootDirectory, PublicationWaitReason = null, PublicationStatus = "reused", ExecutionStatus = "reused", Problem = null
    };
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) => Run(ctx, item, null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        Run(ctx, item, JsonSerializer.Deserialize<FileArtifactPublicationSignal>(signalJson, WorkflowJson.Options), ct);

    private async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, object item, FileArtifactPublicationSignal? answer, CancellationToken ct)
    {
        var config = Parse(ctx.ActivityConfigJson ?? "{}");
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var publisher = ctx.Services.GetRequiredService<FileArtifactPublisher>();
        if (item is DownloadWorkset workset)
        {
            var targets = workset.Targets.ToList();
            if (answer != null)
            {
                var waiting = targets.Where(t => !t.Excluded && t.PublicationWaitReason != null).ToList();
                if (waiting.Count != 1 || waiting[0].Id != answer.TargetId)
                    throw new ArgumentException("The delivery answer does not belong to the waiting target.");
            }
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                if (target.Excluded) continue;
                var targetConfig = answer?.TargetId == target.Id && answer.TargetDirectory != null
                    ? config with {TargetDirectory = answer.TargetDirectory} : config;
                var sourceIntent = config.Source == "processed" ? target.ProcessingIntentKey : DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, ctx.Services.GetService<FileProcessingWorkspaceResolver>());
                // Older adaptive runs may have persisted the processed artifact while the
                // assessment node still left ProcessingIntentKey empty. Recover the exact
                // intent from that artifact's completed operation instead of reacquiring or
                // creating a new processing generation.
                if (sourceIntent == null && config.Source == "processed" && target.ProcessingOperationId is { } processingOperationId)
                {
                    var operation = await store.GetOperationAsync(processingOperationId, ct);
                    if (operation is {Kind: "files.process", State: ArtifactOperationState.Completed})
                        sourceIntent = operation.IntentKey;
                }
                if (sourceIntent == null) throw new InvalidOperationException("Process the selected source before delivering its processed artifact.");
                var directory = target.PublishedDirectory ?? target.PublicationTargetDirectory ?? CloudTargetDirectory(workset, target, targetConfig);
                if (answer?.TargetId == target.Id && answer.TargetDirectory != null)
                    directory = CloudTargetDirectory(workset, target, targetConfig);
                var currentStageBinding = CurrentStageBinding(target, targetConfig, directory);
                var reusable = config.ReusePolicy == "force" ? null : await publisher.ResolveAsync(sourceIntent, directory, ct, config.ContentLayout, currentStageBinding);
                FileArtifactPublicationResult result;
                if (reusable != null) result = new(reusable);
                else
                {
                    var id = config.Source == "processed" ? target.ProcessedArtifactId : target.DownloadArtifactId;
                    var input = id == null ? null : await store.ResolveByIdAsync(id, ct);
                    result = input == null ? new(null, "PublicationInputMissing", "The selected artifact is missing or modified. Reacquire or process it before delivery.") :
                        await publisher.PublishAsync(sourceIntent, input, directory, "workflow:" + ctx.RunId + ":publish:" + target.Id,
                            ctx.RunId, ct, config.ReusePolicy == "force" ? ctx.RunId.ToString() : null, config.ContentLayout, currentStageBinding);
                }
                if (result.Artifact == null)
                {
                    targets[index] = target with {Problem = result.Message, PublicationWaitReason = result.WaitReason,
                        PublicationTargetDirectory = directory};
                    return Suspend(workset with {Targets = targets}, target.Id, directory, result);
                }
                await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":artifact.publish", result.Artifact.OperationId, ct);
                targets[index] = ApplyCloud(target, result.Artifact) with {ExecutionStatus = reusable != null ? "reused" : "completed",
                    PublicationStatus = reusable != null ? "reused" : "delivered"};
            }
            return WorkflowItemOutcome.ReplaceWith(workset with {Targets = targets});
        }
        if (item is AcquisitionWorkItem local)
        {
            if (answer != null && answer.TargetId != "files")
                throw new ArgumentException("The delivery answer does not belong to the waiting files.");
            if (answer?.TargetDirectory != null) config = config with {TargetDirectory = answer.TargetDirectory};
            var variables = local.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
            var id = variables.GetValueOrDefault(config.Source == "processed" ? "artifact.outputId" : "artifact.inputId");
            var sourceIntent = LocalSourceIntent(variables, config.Source);
            var directory = LocalTargetDirectory(local, config);
            if (id == null || sourceIntent == null || string.IsNullOrWhiteSpace(directory))
                return Suspend(local, "files", directory ?? "", new(null, "PublicationInputMissing", "Provide a verified artifact and a user delivery directory."));
            var reuse = config.ReusePolicy == "force" ? null : await publisher.ResolveAsync(sourceIntent, directory, ct, config.ContentLayout);
            var input = reuse == null ? await store.ResolveByIdAsync(id, ct) : null;
            var result = reuse != null ? new FileArtifactPublicationResult(reuse) : input == null
                ? new(null, "PublicationInputMissing", "The source artifact is missing or modified.") :
                await publisher.PublishAsync(sourceIntent, input, directory, "workflow:" + ctx.RunId + ":publish:files", ctx.RunId, ct,
                    config.ReusePolicy == "force" ? ctx.RunId.ToString() : null, config.ContentLayout);
            if (result.Artifact == null) return Suspend(local, "files", directory, result);
            await ArtifactWorkflowBindings.BindAsync(ctx, "files:artifact.publish", result.Artifact.OperationId, ct);
            variables["artifact.publishedId"] = result.Artifact.Id;
            variables["artifact.publicationId"] = result.Artifact.OperationId;
            return WorkflowItemOutcome.ReplaceWith(local with {Variables = variables, TargetDirectory = result.Artifact.RootDirectory,
                Files = result.Artifact.Files, PreserveDirectoryStructure = true});
        }
        throw new ArgumentException("This node requires a file artifact work item.");
    }
    private static WorkflowItemOutcome Suspend(object item, string targetId, string directory, FileArtifactPublicationResult result) =>
        WorkflowItemOutcome.Suspend(new("artifacts.publication", JsonSerializer.Serialize(new
            {targetId, targetDirectory = directory, reason = result.WaitReason, message = result.Message}, WorkflowJson.Options), item));
}
