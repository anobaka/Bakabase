using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record ArtifactCleanupConfig
{
    public string Policy { get; init; } = "keep";
    public string? TargetId { get; init; }
    public bool UseTargetStoragePolicy { get; init; }
}

/// <summary>Typed adapter: only the registered input artifact of a verified output can be cleaned.</summary>
public sealed class CleanupDownloadArtifactsActivity : IResumableWorkflowActivity, IWorkflowCheckpointedActivity
{
    public string Kind => "download.cleanupArtifacts";
    public string DisplayName => "Clean up processed inputs";
    public string Description => "Keep inputs by default, or move the exact managed source artifact to recoverable quarantine after verifying its outputs and consumers.";
    public string DescriptionKey => "cloudDownload.activity.download.cleanupArtifacts.description";
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];
    public bool IsDestructive => true;

    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ArtifactCleanupConfig>(context.ConfigJson, WorkflowJson.Options) ?? new();
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(config.Policy is "keep" or "quarantine" or "delete" ? [] :
                [new() {Code = "cleanupPolicyInvalid", Message = "Choose keep, recoverable quarantine, or deletion after verified final delivery."}]);
        }
        catch (JsonException)
        {return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([new() {Code = "cleanupPolicyInvalid", Message = "Invalid cleanup configuration."}]);}
    }

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) => Run(ctx, (DownloadWorkset)item, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) => Run(ctx, (DownloadWorkset)item, ct);

    private static async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, DownloadWorkset item, CancellationToken ct)
    {
        var config = ctx.GetConfig<ArtifactCleanupConfig>() ?? new();
        if (config.Policy is not ("keep" or "quarantine" or "delete")) throw new ArgumentException("Invalid cleanup policy.");
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var cleanup = ctx.Services.GetRequiredService<ArchiveQuarantineService>();
        var coordinator = ctx.Services.GetRequiredService<DownloadArtifactCoordinator>();
        var targets = new List<CloudDownloadTarget>();
        foreach (var target in item.Targets)
        {
            if (config.TargetId != null && target.Id != config.TargetId) {targets.Add(target); continue;}
            var policy = config.UseTargetStoragePolicy ? target.StoragePolicy?.SourceCleanup ?? config.Policy : config.Policy;
            if (policy is not ("keep" or "quarantine" or "delete")) throw new ArgumentException("Invalid source cleanup policy.");
            if (target.Excluded) {targets.Add(target); continue;}
            if (policy == "keep") {targets.Add(target with {CleanupStatus = "kept"}); continue;}
            var output = policy == "delete" || config.UseTargetStoragePolicy
                ? target.PublishedArtifactId : target.PublishedArtifactId ?? target.ProcessedArtifactId;
            if (target.DownloadArtifactId == null || output == null)
                throw new InvalidOperationException("Source cleanup requires its registered source and verified final delivery.");
            await coordinator.ReleaseAsync(ctx, target, ct);
            var input = await store.GetArtifactAsync(target.DownloadArtifactId, ct)
                ?? throw new InvalidOperationException("The source artifact was removed from the registry.");
            // Reused final results do not reattach their removed ancestors as active consumers.
            await store.ReleaseConsumerAsync(input.OperationId, DownloadArtifactCoordinator.Consumer(ctx, target), ct);
            var result = policy == "delete"
                ? await cleanup.DeleteAfterDeliveryAsync(input.Id, [output], ct)
                : await cleanup.QuarantineAsync(input.Id, [output], ct);
            var next = target with {CleanupReceiptId = result.ReceiptId, CleanupStatus = result.Completed ? policy == "delete" ? "deleted" : "quarantined" : "waiting",
                Problem = result.Completed ? null : result.Message};
            targets.Add(next);
            if (!result.Completed)
            {
                targets.AddRange(item.Targets.GetRange(targets.Count, item.Targets.Count - targets.Count));
                var updated = item with {Targets = targets};
                return WorkflowItemOutcome.Suspend(new("artifacts.cleanup", JsonSerializer.Serialize(new
                    {targetId = target.Id, message = result.Message, receiptId = result.ReceiptId}, WorkflowJson.Options), updated));
            }
        }
        return WorkflowItemOutcome.ReplaceWith(item with {Targets = targets});
    }
}
