using Bakabase.Abstractions.Components.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionPlanProcessingConfig(string TargetId, FileProcessingPlan Plan, string? StopAfterStepId = null,
    AcquisitionPlanArtifactInput? ContinueFrom = null, ArtifactStoragePolicy? StoragePolicy = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ExtractionLayout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool UseVerifiedInput = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool ExplicitlyKeepFiles = false);

/// <summary>One recorded node per known instruction, sharing the complete plan's exact input references.</summary>
public sealed class AcquisitionPlanProcessingActivity : IResumableWorkflowActivity,
    IWorkflowArtifactDemandActivity, IWorkflowArtifactTransparentActivity, IFileProcessingPlanConsumer
{
    public const string ActivityKind = "acquisition.plan.process";
    public string Kind => ActivityKind;
    public string DisplayName => "Execute acquisition file-processing instruction";
    public string DescriptionKey => "workflow.activity.acquisitionPlanProcess.description";
    public string Group => "fs";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];
    public string GetArchivePolicy(string configJson) => "instructions";
    public bool PreservesArtifactDemand(string configJson) => true;
    public async Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try
        {
            var config = Validate(context.ConfigJson);
            return await FileProcessingDependencyPreflight.InspectWorkflowAsync(context, [config.Plan], ct);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException)
        {return [new() {Code = "acquisitionPlanInvalid", Message = e.Message}];}
    }
    private static AcquisitionPlanProcessingConfig Validate(string json)
    {
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(json, WorkflowJson.Options)
            ?? throw new ArgumentException("The processing node needs a plan.");
        FileProcessingPlanValidator.Validate(config.Plan);
        if (!FileExtractionLayouts.Valid(config.ExtractionLayout)) throw new ArgumentException("Invalid extraction layout.");
        if (config.StoragePolicy != null) ArtifactStoragePolicies.Validate(config.StoragePolicy);
        if (string.IsNullOrWhiteSpace(config.TargetId) || config.StopAfterStepId != null && !config.Plan.Steps.Any(s => s.Id == config.StopAfterStepId))
            throw new ArgumentException("The processing node must refer to its selected target and a known step.");
        if (config.UseVerifiedInput && (config.ContinueFrom == null || config.StopAfterStepId != null ||
            config.Plan.Steps.Count != 0 || config.Plan.Requirement is not ("unknown" or "notRequired")))
            throw new ArgumentException("A verified-input pass cannot skip configured processing instructions.");
        return config;
    }
    public async Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson, CancellationToken ct)
    {
        var config = Validate(configJson);
        if (config.UseVerifiedInput) return item;
        var workset = (DownloadWorkset)await WorkflowArtifactPlanner.SatisfyDownstreamAsync(context, item, ct);
        var target = workset.Targets.Single(t => t.Id == config.TargetId);
        if (target.PublishedArtifactId != null || target.Excluded) return workset;
        var plan = EffectivePlan(config, target);
        if (plan.Requirement == "unknown") return workset;
        var finalId = plan.Steps.LastOrDefault()?.Id;
        var intent = Intent(context.Services, workset, target, plan, finalId, config.ContinueFrom);
        var processor = context.Services.GetRequiredService<FileArtifactProcessor>();
        var artifact = await processor.ResolveAsync(intent, ct) ??
            await PublishFileArtifactsActivity.ResolveFollowingPublicationAsync(context, workset, target, intent, ct);
        if (artifact == null) return workset;
        await Bind(context, target, artifact, finalId, ct);
        artifact = await AcquisitionProcessingStageCommit.CommitAsync(context, target, artifact, ct);
        await ReleaseContinuationAsync(context, target, config.ContinueFrom, ct);
        return Replace(workset, Apply(target, artifact, intent, finalId) with {ExecutionStatus = "reused"});
    }
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
        Run(ctx, (DownloadWorkset)item, null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct)
    {
        var workset = (DownloadWorkset)item;
        var config = Validate(ctx.ActivityConfigJson);
        var target = workset.Targets.Single(t => t.Id == config.TargetId);
        if (target.DeliveryReview is {Accepted: false} && NeedsInlineReview(ctx, workset, config))
            return AssessFileArtifactDeliveryActivity.ReviewAsync(ctx, workset, InlineReviewConfig(config),
                JsonSerializer.Deserialize<FileArtifactDeliverySignal>(signalJson, WorkflowJson.Options)
                    ?? throw new ArgumentException("Provide a delivery-review answer."), ct);
        return Run(ctx, workset, JsonSerializer.Deserialize<CloudFileProcessingSignal>(signalJson, WorkflowJson.Options)
            ?? throw new ArgumentException("Provide a processing answer."), ct);
    }

    // Early acquisition snapshots went straight from processing to publication. Review only
    // each target's final processing boundary, reusing the manifest-bound response without
    // changing its frozen chain or replaying the original download.
    internal static bool NeedsInlineReview(WorkflowExecutionContext ctx, DownloadWorkset workset, AcquisitionPlanProcessingConfig config)
    {
        if (ctx.WorkflowDefinitionId != 0 || !workset.Id.StartsWith("acquisition:", StringComparison.Ordinal)) return false;
        foreach (var next in ctx.FollowingActivities)
        {
            if (next.Kind == "fs.publishArtifacts")
                return JsonSerializer.Deserialize<FileArtifactPublicationConfig>(next.ConfigJson, WorkflowJson.Options)?.Source == "processed";
            if (next.Kind == ActivityKind &&
                JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(next.ConfigJson, WorkflowJson.Options)?.TargetId == config.TargetId)
                return false;
            if (next.Kind == AssessFileArtifactDeliveryActivity.ActivityKind &&
                JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(next.ConfigJson, WorkflowJson.Options)?.TargetId == config.TargetId)
                return false;
            if (next.Kind is not (ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind)) return false;
        }
        return false;
    }

    private static FileArtifactDeliveryAssessmentConfig InlineReviewConfig(AcquisitionPlanProcessingConfig config) =>
        new(config.TargetId, AutoContinueOuterPackaging: false, ExtractionLayout: config.ExtractionLayout);

    private static async Task<WorkflowItemOutcome> CompleteProcessing(WorkflowExecutionContext ctx, DownloadWorkset workset,
        CloudDownloadTarget target, AcquisitionPlanProcessingConfig config, FileProcessingPlan plan, ArtifactRecord? artifact, CancellationToken ct)
    {
        if (target.ProcessedArtifactId != null && (config.StopAfterStepId == null || config.StopAfterStepId == plan.Steps.LastOrDefault()?.Id))
        {
            var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
            artifact ??= await store.GetArtifactAsync(target.ProcessedArtifactId, ct);
            if (artifact != null)
            {
                var current = await AcquisitionProcessingStageCommit.CommitAsync(ctx, target, artifact, ct);
                if (current.Id != artifact.Id) target = Apply(target, current, target.ProcessingIntentKey!, config.StopAfterStepId);
                artifact = current;
            }
        }
        // A user's explicit choice to keep a downloaded artifact completes the initial review.
        // Bind that choice to these bytes so a downstream AI-enabled assessment respects it.
        if ((config.ExplicitlyKeepFiles || config.Plan.Requirement == "unknown" && config.ContinueFrom == null) &&
            plan.Requirement == "notRequired" && target.ProcessedArtifactId != null && target.DeliveryReview == null)
        {
            artifact ??= await ctx.Services.GetRequiredService<IArtifactOperationStore>().ResolveByIdAsync(target.ProcessedArtifactId, ct)
                ?? throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
            target = target with {DeliveryReview = new(artifact.Id, artifact.Fingerprint, Accepted: true)};
        }
        var result = Replace(workset, target);
        if (target.PublishedArtifactId != null || !NeedsInlineReview(ctx, result, config))
            return WorkflowItemOutcome.ReplaceWith(result);
        var checkUnprocessedMedia = config.ContinueFrom == null && plan.Requirement == "notRequired" &&
            target.DeliveryReview?.Accepted != true && artifact != null &&
            (await FileArtifactDeliveryAssessment.DisguisedVideoArchivesAsync(artifact, ct)).Count > 0;
        if (!checkUnprocessedMedia && !plan.Steps.Any(s => s.Op == "extractArchive"))
            return WorkflowItemOutcome.ReplaceWith(result);
        // The shared assessor handles mixed/large unknown results and finished applications
        // as well as a single signed archive. Narrowing this gate to a confirmed single group
        // would silently publish other unfinished outputs. Legacy review grants no new AI or
        // recursive extraction authority; uncertainty waits for the normal user response.
        return await AssessFileArtifactDeliveryActivity.ReviewAsync(ctx, result, InlineReviewConfig(config), null, ct);
    }

    private async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, DownloadWorkset workset, CloudFileProcessingSignal? signal, CancellationToken ct)
    {
        using var writeScope = StorageWriteScope.Enter(new("workflow", ctx.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var config = Validate(ctx.ActivityConfigJson);
        var target = workset.Targets.Single(t => t.Id == config.TargetId);
        if (target.Excluded) throw new InvalidOperationException("A required acquisition part cannot be excluded from execution.");
        if (signal == null && target.DeliveryReview is {Accepted: false} && NeedsInlineReview(ctx, workset, config))
            return await AssessFileArtifactDeliveryActivity.ReviewAsync(ctx, workset, InlineReviewConfig(config), null, ct);
        if (signal != null && signal.TargetId != target.Id) throw new ArgumentException("This answer belongs to another target.");
        if (signal != null) FileProcessingResumeActions.Validate(ctx, target, signal, config.StopAfterStepId, allowSourceEvidence: true);
        if (signal?.StorageRecovery != null && (signal.Password != null || signal.ExtractionPlanJson != null ||
                signal.ArchivePolicy != null || signal.ArchiveId != null || signal.RecursiveOptions != null))
            throw new ArgumentException("A storage continuation cannot change processing instructions or passwords.");
        var plan = EffectivePlan(config, target);
        if (signal?.ExtractionPlanJson is { } revised)
        {
            if (config.StopAfterStepId != null) throw new ArgumentException("A recorded instruction can accept a password; changing its steps requires a revised acquisition plan.");
            plan = FileExtractionLayouts.Apply(JsonSerializer.Deserialize<FileProcessingPlan>(revised, WorkflowJson.Options) ?? new(), config.ExtractionLayout);
            FileProcessingPlanValidator.Validate(plan);
            target = target with {ProcessingPlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options)};
        }
        if (signal?.ArchivePolicy == "keep")
        {
            if (config.StopAfterStepId != null) throw new ArgumentException("A recorded instruction cannot be silently skipped.");
            plan = new() {Requirement = "notRequired"};
            target = target with {ProcessingPlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options)};
        }
        if (signal?.ArchivePolicy is not (null or "keep" or "instructions"))
            throw new ArgumentException("Choose explicit file-processing instructions or keep the downloaded files.");
        if (target.PublishedArtifactId != null) return WorkflowItemOutcome.ReplaceWith(Replace(workset, target));
        await ctx.ReportStage("verifyingFiles");
        var finalId = plan.Steps.LastOrDefault()?.Id;
        var finalIntent = Intent(ctx.Services, workset, target, plan, finalId, config.ContinueFrom);
        if (signal?.StorageRecovery == null && target.ProcessedArtifactId != null && target.ProcessingIntentKey == finalIntent)
            return await CompleteProcessing(ctx, workset, target, config, plan, null, ct);
        var processor = ctx.Services.GetRequiredService<FileArtifactProcessor>();
        var boundary = config.StopAfterStepId;
        var intent = Intent(ctx.Services, workset, target, plan, boundary ?? finalId, config.ContinueFrom);
        if (!config.UseVerifiedInput && signal?.StorageRecovery == null && await processor.ResolveAsync(intent, ct) is { } cached)
        {
            await Bind(ctx, target, cached, boundary, ct);
            if ((boundary == null || boundary == finalId) && ctx.Services.GetService<DownloadArtifactCoordinator>() is { } cachedCoordinator)
            {
                if (config.ContinueFrom == null) await cachedCoordinator.ReleaseAsync(ctx, target, ct);
                else await ReleaseContinuationAsync(ctx, target, config.ContinueFrom, ct);
            }
            return await CompleteProcessing(ctx, workset, Apply(target, cached, intent, boundary) with {ExecutionStatus = "reused"},
                config, plan, cached, ct);
        }
        var coordinator = ctx.Services.GetRequiredService<DownloadArtifactCoordinator>();
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var serialized = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        var processingConfig = new FileArtifactProcessingConfig
            {StoragePolicy = config.StoragePolicy ?? target.StoragePolicy, StorageTitle = target.Title,
                WorkspaceDirectoryScope = target.ProcessingDirectoryScope};
        FileArtifactCheckpointContinuation? checkpoint = null;
        FileArtifactProcessingResult? checkpointFailure = null;
        ArtifactRecord? input;
        if (config.ContinueFrom is { } continuation)
        {
            var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
            if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, ctx.RunId, continuation.TaskId, ct))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            var registered = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, continuation.TaskId, target.Id, continuation, ct);
            var stepIndex = plan.Steps.FindIndex(s => s.Id == boundary);
            if (stepIndex > 0 && signal?.Password == null && signal?.ExtractionPlanJson == null &&
                target.ProcessedArtifactId is { } previousId)
            {
                var previousStep = plan.Steps[stepIndex - 1].Id;
                if (target.ProcessingStepArtifacts.TryGetValue(previousStep, out var stepArtifact) && stepArtifact == previousId &&
                    target.ProcessingIntentKey == Intent(ctx.Services, workset, target, plan, previousStep, continuation) &&
                    await store.GetArtifactAsync(previousId, ct) is { } previous && previous.OperationId == target.ProcessingOperationId &&
                    await OwnsCheckpointBindingsAsync(db, ctx.RunId, continuation.TaskId, target.Id,
                        registered.OperationId, previous.OperationId, previousStep, ct))
                {
                    try
                    {
                        checkpoint = await processor.ReadCheckpointContinuationAsync(intent, registered, serialized,
                            target.SelectedSource?.ArchivePassword, processingConfig, previous, previousStep, ct);
                    }
                    catch (Exception error) when (error is InvalidDataException or JsonException)
                    { checkpointFailure = new(null, "NeedsReconciliation", error.Message); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { checkpointFailure = new(null, "StorageUnavailable", error.Message); }
                }
            }
            // Only an exact active continuation with a complete private snapshot may avoid
            // rereading the remote original. The executor rechecks all private SHA values.
            input = checkpoint == null && checkpointFailure == null ? await store.ResolveByIdAsync(continuation.ArtifactId, ct) : registered;
        }
        else
        {
            target = await coordinator.RecordAsync(ctx, target, workset.OutputDirectory, ct);
            input = target.DownloadArtifactId == null ? null : await store.ResolveByIdAsync(target.DownloadArtifactId, ct);
        }
        if (input == null) throw new InvalidOperationException("The downloaded file artifact is missing or modified.");
        // A pinned checkpoint has already bound and verified its private input. Do
        // not require the original selected directory again after that hand-off.
        if (checkpoint == null && checkpointFailure == null && input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
            await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ct);
        if (checkpoint == null && checkpointFailure == null && signal?.StorageRecovery == null)
        {
            await ctx.ReportStage("preparingFiles");
            input = await AcquisitionProcessingStageInput.PrepareAsync(ctx, target, input, ct, processingConfig.StoragePolicy);
        }
        if (config.UseVerifiedInput && config.ContinueFrom != null && input.Kind is "processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind)
        {
            // The attachment node has already pinned this task's immutable artifact until the run ends.
            // Carry its exact directory and manifest into assessment/publication, without staging a copy.
            var original = await store.GetOperationAsync(input.OperationId, ct);
            if (original == null || string.IsNullOrWhiteSpace(original.IntentKey))
                throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            await Bind(ctx, target, input, null, ct);
            return WorkflowItemOutcome.ReplaceWith(Replace(workset, target with
            {
                ProcessedArtifactId = input.Id, ProcessingOperationId = input.OperationId,
                ProcessedDirectory = input.RootDirectory, ProcessedFiles = input.Files.ToList(),
                ProcessingIntentKey = input.Kind == "processedFiles" ? original.IntentKey : AcquisitionPlanArtifactInputGuard.Intent(config.ContinueFrom),
                ExecutionStatus = "reused", ProcessingWaitReason = null, Problem = null
            }));
        }
        var result = checkpointFailure ?? await processor.ExecuteAsync(intent, input, serialized, target.SelectedSource?.ArchivePassword,
            processingConfig,
            DownloadArtifactCoordinator.Consumer(ctx, target), null, signal?.Password,
            value => ctx.ReportProgress(value, target.Title).GetAwaiter().GetResult(), ct,
            replan: false, stopAfterStepId: boundary, recovery: signal?.StorageRecovery, checkpointContinuation: checkpoint,
            passwordEvidence: AcquisitionArchivePasswordEvidence.Bind(ctx, target.Id, target.SelectedSource?.Url), reportStage: ctx.ReportStage);
        if (result.Artifact == null)
        {
            var reason = result.WaitReason ?? "ExtractionPlanUnknown";
            target = target with {ProcessingWaitReason = reason, Problem = reason, ProcessingPlanJson = serialized};
            return WorkflowItemOutcome.Suspend(new("cloud.fileProcessing", JsonSerializer.Serialize(new
            {
                TargetId = target.Id, target.Title, Reason = reason, Message = result.Message ?? "Confirm the processing instructions.",
                ExtractionPlanJson = serialized, KnownStepId = config.StopAfterStepId,
                ArtifactId = input.Id, Fingerprint = input.Fingerprint,
                result.RequiredComponentId, result.PathConflict,
                CanEditPlan = config.StopAfterStepId == null, SourcePasswordEvidenceChecked = reason == "PasswordUnknown",
                AllowedActions = FileProcessingResumeActions.Allowed(reason),
                AllowedArchivePolicies = config.StopAfterStepId == null ? new[] {"instructions", "keep"} : new[] {"instructions"}
            }, WorkflowJson.Options), Replace(workset, target)));
        }
        await Bind(ctx, target, result.Artifact, boundary, ct);
        // Later boundaries still resolve their declared inputs against the original plan. Keep
        // the raw-artifact consumer alive until the entire part has completed.
        if (boundary == null || boundary == finalId)
        {
            if (config.ContinueFrom == null) await coordinator.ReleaseAsync(ctx, target, ct);
            else await ReleaseContinuationAsync(ctx, target, config.ContinueFrom, ct);
        }
        return await CompleteProcessing(ctx, workset, Apply(target, result.Artifact, intent, boundary),
            config, plan, result.Artifact, ct);
    }
    internal static FileProcessingPlan EffectivePlan(AcquisitionPlanProcessingConfig config, CloudDownloadTarget target) =>
        config.UseVerifiedInput ? new() {Requirement = "notRequired"} :
        FileExtractionLayouts.Apply(config.StopAfterStepId == null && target.ProcessingPlanJson != null
            ? JsonSerializer.Deserialize<FileProcessingPlan>(target.ProcessingPlanJson, WorkflowJson.Options) ?? config.Plan : config.Plan, config.ExtractionLayout);

    internal static async Task<bool> OwnsCheckpointBindingsAsync(BakabaseDbContext db, long runId, int taskId,
        string targetId, string inputOperation, string previousOperation, string previousStep, CancellationToken ct)
    {
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var consumer = AcquisitionPlanArtifactInputGuard.Consumer(runId, targetId);
        if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, taskId, ct) ||
            !await db.Set<WorkflowRunDbModel>().AsNoTracking().AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Running, ct) ||
            !await db.Set<ArtifactConsumerDbModel>().AsNoTracking().AnyAsync(c => c.OperationId == inputOperation &&
                c.ConsumerKey == consumer && c.OwnerKind == "workflow" && c.OwnerId == owner && c.ReleasedAt == null, ct))
            return false;
        return await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == owner && b.Slot == targetId + ":artifact.continuation" && b.OperationId == inputOperation, ct) &&
            await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == owner && b.Slot == targetId + ":process:" + previousStep && b.OperationId == previousOperation, ct);
    }
    private static string Intent(IServiceProvider services, DownloadWorkset workset, CloudDownloadTarget target, FileProcessingPlan plan, string? boundary,
        AcquisitionPlanArtifactInput? continuation = null) =>
        Intent(workset, target, plan, boundary, services.GetService<FileProcessingWorkspaceResolver>(), continuation);

    internal static string Intent(DownloadWorkset workset, CloudDownloadTarget target, FileProcessingPlan plan, string? boundary,
        FileProcessingWorkspaceResolver? workspaces, AcquisitionPlanArtifactInput? continuation = null)
    {
        var inputIntent = continuation == null ? DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, workspaces)
            : AcquisitionPlanArtifactInputGuard.Intent(continuation);
        var intent = FileArtifactProcessor.Intent(inputIntent,
            JsonSerializer.Serialize(plan, WorkflowJson.Options), target.SelectedSource?.ArchivePassword, new());
        // Final demands preserve the existing processor's identity, so a delivered artifact can
        // satisfy this plan even after legacy temporary inputs and intermediate results were cleaned.
        return boundary == null || boundary == plan.Steps.LastOrDefault()?.Id ? intent :
            intent + ":boundary:" + CloudOperationService.Key(boundary)[..16];
    }
    private static Task ReleaseContinuationAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target,
        AcquisitionPlanArtifactInput? input, CancellationToken ct) => input == null ? Task.CompletedTask :
        ctx.Services.GetRequiredService<IArtifactOperationStore>().ReleaseConsumerAsync(input.OperationId,
            AcquisitionPlanArtifactInputGuard.Consumer(ctx.RunId, target.Id), ct);
    private static DownloadWorkset Replace(DownloadWorkset item, CloudDownloadTarget target) => item with
    {Targets = item.Targets.Select(t => t.Id == target.Id ? target : t).ToList()};
    private static CloudDownloadTarget Apply(CloudDownloadTarget target, ArtifactRecord artifact, string intent, string? boundary)
    {
        var steps = new Dictionary<string, string>(target.ProcessingStepArtifacts) {[boundary ?? "complete"] = artifact.Id};
        var updated = target with {ProcessedArtifactId = artifact.Id, ProcessingOperationId = artifact.OperationId,
            ProcessedDirectory = artifact.RootDirectory, ProcessedFiles = artifact.Files.ToList(), ProcessingIntentKey = intent,
            ProcessingStepArtifacts = steps, ProcessingWaitReason = null, Problem = null, ExecutionStatus = "completed"};
        return artifact.Kind == "publishedFiles" ? PublishFileArtifactsActivity.ApplyCloud(updated, artifact) : updated;
    }
    private static async Task Bind(WorkflowExecutionContext ctx, CloudDownloadTarget target, ArtifactRecord artifact, string? boundary, CancellationToken ct) =>
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":process:" + (boundary ?? "complete"), artifact.OperationId, ct);
}
