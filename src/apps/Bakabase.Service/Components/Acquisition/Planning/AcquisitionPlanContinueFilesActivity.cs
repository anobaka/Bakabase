using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionPlanContinueFilesConfig(IReadOnlyList<AcquisitionPlanContinuedTarget> Targets)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublishRetainedFiles { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArtifactDeliveryPolicy? ContentLayout { get; init; }
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    public Dictionary<string, ArtifactStoragePolicyOverride>? TargetStorageOverrides { get; init; }
}
public sealed record AcquisitionPlanContinuedTarget(CloudDownloadTarget Target, AcquisitionPlanArtifactInput Input)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedDeliveryProjectionFingerprint { get; init; }
}

/// <summary>Restores task-owned, verified processing inputs after any other parts finish downloading.</summary>
public sealed class AcquisitionPlanContinueFilesActivity : IWorkflowActivity
{
    public const string ActivityKind = "acquisition.plan.continueFiles";
    public string Kind => ActivityKind;
    public string DisplayName => "Continue from verified files";
    public string DescriptionKey => "workflow.activity.acquisitionContinueFiles.description";
    public string Group => "fs";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<AcquisitionPlanContinueFilesConfig>(ctx.ActivityConfigJson, WorkflowJson.Options)
            ?? throw new ArgumentException("PLAN_REPLAN_INVALID");
        if (config.Targets == null || config.Targets.Count is < 1 or > 100 ||
            config.Targets.Any(t => t == null || t.Target == null || t.Input == null) ||
            config.Targets.Select(t => t.Target.Id).Distinct().Count() != config.Targets.Count)
            throw new ArgumentException("PLAN_REPLAN_INVALID");
        var effective = CloudStoragePolicyBinding.Apply(config.Targets.Select(t => t.Target).ToList(),
            config.StoragePolicy, config.TargetStorageOverrides).ToDictionary(t => t.Id, StringComparer.Ordinal);
        config = config with {Targets = config.Targets.Select(t => t with {Target = effective[t.Target.Id]}).ToArray()};
        var workset = (DownloadWorkset)item;
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
        var targets = workset.Targets.ToList();
        var outputs = new List<WorkflowOutputBlock>();
        var attached = new List<(string OperationId, string Consumer)>();
        try
        {
            foreach (var entry in config.Targets)
            {
                if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, ctx.RunId, entry.Input.TaskId, ct))
                    throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
                // A projected delivery borrows an earlier publication. A storage retry
                // borrows only the exact processed input of a stopped publication node.
                var projected = entry.ExpectedDeliveryProjectionFingerprint != null;
                var artifact = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, entry.Input.TaskId, entry.Target.Id, entry.Input, ct,
                    publishedOutputOnly: config.PublishRetainedFiles && projected,
                    publicationInputOnly: config.PublishRetainedFiles && !projected);
                if (artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind)
                    await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(artifact,
                        ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ct);
                var consumer = AcquisitionPlanArtifactInputGuard.Consumer(ctx.RunId, entry.Target.Id);
                if (!await store.PinConsumerAsync(artifact.OperationId, consumer, "workflow", ctx.RunId.ToString(), ct))
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                attached.Add((artifact.OperationId, consumer));
                // No download fallback: this revision is explicitly about this immutable input.
                if (await store.ResolveByIdAsync(artifact.Id, ct) is not { } verified || verified.Fingerprint != entry.Input.Fingerprint)
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                await ArtifactWorkflowBindings.BindAsync(ctx, entry.Target.Id + ":artifact.continuation", artifact.OperationId, ct);
                var existing = targets.FindIndex(t => t.Id == entry.Target.Id);
                var target = existing < 0 ? entry.Target : targets[existing] with {StoragePolicy = entry.Target.StoragePolicy};
                if (config.PublishRetainedFiles)
                {
                    if (projected && (verified.Kind != "publishedFiles" || config.ContentLayout == null ||
                        FileArtifactDeliveryLayout.Preview(verified, config.ContentLayout).ProjectionFingerprint != entry.ExpectedDeliveryProjectionFingerprint))
                        throw new ArgumentException("DELIVERY_LAYOUT_PREVIEW_CHANGED");
                    // Preserve the original processing intent across repeated publication failures.
                    // A republished input has a separate identity for its reviewed projection.
                    var intent = verified.Kind == "publishedFiles" ? AcquisitionPlanArtifactInputGuard.Intent(entry.Input) :
                        (await store.GetOperationAsync(verified.OperationId, ct))?.IntentKey;
                    if (string.IsNullOrWhiteSpace(intent)) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                    target = target with {ProcessingIntentKey = intent,
                        ProcessedArtifactId = verified.Id, ProcessingOperationId = verified.OperationId,
                        ProcessedDirectory = verified.RootDirectory, ProcessedFiles = verified.Files.ToList()};
                }
                if (existing < 0) targets.Add(target); else targets[existing] = target;
                outputs.Add(new() {Kind = "artifact", Key = entry.Target.Id + ":continuation", LabelKey = "acquisition.plan.continueFiles",
                    ArtifactId = verified.Id, OperationId = verified.OperationId, Directory = verified.RootDirectory,
                    FileCount = verified.Manifest.Entries.Count, TotalBytes = verified.Manifest.Entries.Sum(e => e.Length), State = "reused"});
            }
            await ctx.ReportOutputMetadata(outputs);
            return WorkflowItemOutcome.ReplaceWith(workset with {Targets = targets});
        }
        catch
        {
            foreach (var pin in attached)
            {
                try { await store.ReleaseConsumerAsync(pin.OperationId, pin.Consumer, CancellationToken.None); }
                catch (Exception error) { ctx.Logger.LogWarning(error, "Could not release a failed continuation input; the workflow terminal state fences its consumer."); }
            }
            throw;
        }
    }
}
