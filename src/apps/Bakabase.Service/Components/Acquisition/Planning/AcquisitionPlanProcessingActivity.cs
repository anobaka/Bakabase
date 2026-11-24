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
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
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
        var processor = context.Services.GetRequiredService<IFileArtifactProcessor>();
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

    internal static bool InspectUnprocessedArchives(FileProcessingPlan plan, AcquisitionPlanArtifactInput? input,
        bool explicitlyKeepFiles) =>
        !explicitlyKeepFiles && plan is {Requirement: "notRequired", Steps.Count: 0} &&
        (input == null || input.ManualDownload == true);

    private static FileArtifactDeliveryAssessmentConfig InlineReviewConfig(AcquisitionPlanProcessingConfig config) =>
        new(config.TargetId, AutoContinueOuterPackaging: false, ExtractionLayout: config.ExtractionLayout)
        {
            InspectUnprocessedArchives = InspectUnprocessedArchives(config.Plan, config.ContinueFrom, config.ExplicitlyKeepFiles)
        };

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
        var reviewConfig = InlineReviewConfig(config);
        if (!plan.Steps.Any(s => s.Op == "extractArchive"))
        {
            if (!reviewConfig.InspectUnprocessedArchives || plan.Requirement != "notRequired" ||
                target.DeliveryReview?.Accepted == true || artifact == null)
                return WorkflowItemOutcome.ReplaceWith(result);
            var assessment = await FileArtifactDeliveryAssessment.AssessAsync(artifact, ct, inspectUnprocessedArchives: true);
            return assessment.Files.Count == 0 ? WorkflowItemOutcome.ReplaceWith(result) :
                await AssessFileArtifactDeliveryActivity.ReviewAsync(ctx, result, reviewConfig, null, ct, (artifact, assessment));
        }
        // The shared assessor handles mixed/large unknown results and finished applications
        // as well as a single signed archive. Narrowing this gate to a confirmed single group
        // would silently publish other unfinished outputs. Legacy review grants no new AI or
        // recursive extraction authority; uncertainty waits for the normal user response.
        return await AssessFileArtifactDeliveryActivity.ReviewAsync(ctx, result, reviewConfig, null, ct);
    }

    // A published artifact selected for delivery as-is is already the finished input.
    // The processing directory can belong to another operation and must not be populated.
    internal static bool KeepsPublishedInput(AcquisitionPlanProcessingConfig config, ArtifactRecord input) =>
        config is {UseVerifiedInput: true, ExplicitlyKeepFiles: true, ContinueFrom: not null, StopAfterStepId: null,
            Plan: {Requirement: "notRequired", Steps.Count: 0}} && input.Kind == "publishedFiles" &&
        input.Id == config.ContinueFrom.ArtifactId && input.OperationId == config.ContinueFrom.OperationId &&
        input.Fingerprint == config.ContinueFrom.Fingerprint;

    internal static async Task<bool> CanForwardPublishedInputAsync(BakabaseDbContext db, int runId,
        DownloadWorkset workset, CloudDownloadTarget target, ArtifactRecord input, IReadOnlyList<WorkflowPlannedActivity> following,
        CancellationToken ct)
    {
        if (following is not [{Kind: "fs.publishArtifacts"} publication] ||
            target.StageDirectories?.Delivery is not {Ownership: "exclusive", BindingId: not null} binding ||
            target.PublishedDirectory != null || target.PublishedArtifactId != null) return false;
        var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(publication.ConfigJson, WorkflowJson.Options);
        if (config is not {Source: "processed", ReusePolicy: "reuseVerified"}) return false;
        var directory = target.PublicationTargetDirectory ?? PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, config);
        if (PublishFileArtifactsActivity.CurrentStageBinding(target, config, directory) != binding.BindingId ||
            AcquisitionPlanService.Overlap(input.RootDirectory, directory)) return false;
        ArtifactManifestFiles.RejectLinks(directory);
        return await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, runId, target.Id, input.OperationId, ct);
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
        var groupAction = signal?.Action is FileProcessingResumeActions.InspectGroup or FileProcessingResumeActions.RetainGroup;
        if (groupAction) FileProcessingResumeActions.ValidateGroup(ctx, target, signal!, config.StopAfterStepId);
        else if (signal != null) FileProcessingResumeActions.Validate(ctx, target, signal, config.StopAfterStepId, allowSourceEvidence: true);
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
        if (!groupAction && signal?.StorageRecovery == null && target.ProcessedArtifactId != null && target.ProcessingIntentKey == finalIntent)
            return await CompleteProcessing(ctx, workset, target, config, plan, null, ct);
        var processor = ctx.Services.GetRequiredService<IFileArtifactProcessor>();
        var boundary = config.StopAfterStepId;
        var intent = Intent(ctx.Services, workset, target, plan, boundary ?? finalId, config.ContinueFrom);
        if (!groupAction && !config.UseVerifiedInput && signal?.StorageRecovery == null && await processor.ResolveAsync(intent, ct) is { } cached)
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
                WorkspaceDirectoryScope = target.ProcessingDirectoryScope,
                ArchiveGroupOwner = boundary != null && plan.Steps.Any(s => s.Id == boundary && s.Op == "extractArchive")
                    ? new(ctx.RunId, target.Id, boundary) : null, RetainArchiveGroupToken = groupAction ? signal?.ArchiveGroupToken : null};
        if (groupAction)
        {
            // Only server-persisted workset input and prompt identities can authorize a group lookup.
            // GetArtifactAsync reads the registration. The regular execution path verifies actual bytes on apply.
            if (await store.GetArtifactAsync(signal!.ArtifactId!, ct) is not {} registered || registered.Fingerprint != signal.Fingerprint)
                throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
            if (signal.Action == FileProcessingResumeActions.InspectGroup)
            {
                try
                {
                    var group = FileProcessingResumeActions.IsLegacyGroupProbeWait(ctx.ResumePromptJson!)
                        ? await processor.InspectIncompleteGroupAsync(intent, registered, plan, target.SelectedSource?.ArchivePassword,
                            processingConfig, ct) : null;
                    return SuspendProcessing(workset, target, config, serialized, registered,
                        new(null, "MissingVolumes", group == null ? "The current incomplete archive group could not be verified." :
                            "A required archive volume is missing.") {IncompleteArchiveGroup = group});
                }
                catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException)
                {
                    return SuspendProcessing(workset, target, config, serialized, registered,
                        new(null, "NeedsReconciliation", "The saved archive group evidence changed. Review the original files."));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return SuspendProcessing(workset, target, config, serialized, registered,
                        new(null, "StorageUnavailable", "The saved archive group is unavailable. Reconnect its storage."));
                }
            }
        }
        FileArtifactCheckpointContinuation? checkpoint = null;
        FileArtifactProcessingResult? checkpointFailure = null;
        ArtifactRecord? input;
        var forwardPublication = false;
        var selectedInputId = signal?.StorageRecovery?.SelectedInputArtifactId;
        if (selectedInputId != null && (config.ContinueFrom?.ArtifactId != selectedInputId ||
                target.StageDirectories?.Processing?.BindingId == null))
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        async Task<ArtifactRecord?> ResolveProcessingInputAsync(string id, ArtifactRecord? registered = null)
        {
            if (signal?.StorageRecovery != null && target.StageDirectories?.Processing?.BindingId != null)
            {
                registered ??= await store.GetArtifactAsync(id, ct);
                if (registered == null) return null;
                var owner = workset.SourceRefs.Single(s => s.Module == "acquisition" && s.EntityKind == "task").EntityId;
                var prepared = await AcquisitionProcessingStageInput.ReadPreparedAsync(ctx.Services.GetRequiredService<BakabaseDbContext>(),
                    store, ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>(), int.Parse(owner, System.Globalization.CultureInfo.InvariantCulture),
                    checked((int)ctx.RunId), target, registered, ct);
                if (selectedInputId != null)
                {
                    if (registered.Id != selectedInputId || !AcquisitionProcessingStageInput.WaitUsedSelectedInput(
                            target, registered, prepared, boundary, ctx.ResumePromptJson))
                        throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
                }
                else if (prepared != null) id = prepared.Id;
            }
            var verified = await store.ResolveByIdAsync(id, ct);
            if (verified != null || signal == null || target.StageDirectories?.Processing?.BindingId == null)
                return verified;
            var unavailable = await store.GetArtifactAsync(id, ct);
            if (unavailable?.State is not (ArtifactLocationState.Modified or ArtifactLocationState.Missing or ArtifactLocationState.StorageUnavailable))
                return null;
            var taskId = workset.SourceRefs.Single(s => s.Module == "acquisition" && s.EntityKind == "task").EntityId;
            var borrowed = await AcquisitionProcessingStageInput.ReadPreparedAsync(ctx.Services.GetRequiredService<BakabaseDbContext>(),
                store, ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>(), int.Parse(taskId, System.Globalization.CultureInfo.InvariantCulture),
                checked((int)ctx.RunId), target, unavailable, ct, readOnlyReceiptOnly: true);
            if (borrowed == null) return null;
            checkpointFailure = unavailable.State == ArtifactLocationState.StorageUnavailable
                ? new(null, "StorageUnavailable", "The registered archive storage is unavailable.")
                : new(null, "NeedsReconciliation", "A registered archive input is missing or changed. Restore its original contents before continuing.");
            // Only the bound identity is needed to report this suspension. Do not pass
            // unavailable bytes to an extractor or initialize a replacement snapshot.
            return borrowed;
        }
        if (config.ContinueFrom is { } continuation)
        {
            var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
            if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, ctx.RunId, continuation.TaskId, ct))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            var registered = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, continuation.TaskId, target.Id, continuation, ct);
            // continueFiles has already hashed and pinned this part. Showing an unambiguous
            // instructions prompt needs only its current registration and file layout. The
            // response (including keep) still takes the original content-verification path.
            if (signal == null && boundary == null && plan is {Requirement: "unknown", Steps.Count: 0} &&
                processingConfig.ResolveInitialPlan(plan) == null &&
                await CanPromptForPreparedDownloadAsync(ctx, db, continuation.TaskId, target, registered, ct))
                return SuspendProcessing(workset, target, config, serialized, registered, FileArtifactProcessingResult.InstructionsRequired());
            if (KeepsPublishedInput(config, registered) && plan is {Requirement: "notRequired", Steps.Count: 0} &&
                ctx.FollowingActivities is [{Kind: "fs.publishArtifacts"}] && target.StageDirectories?.Delivery?.BindingId != null)
            {
                if (!await CanForwardPublishedInputAsync(db, checked((int)ctx.RunId), workset, target, registered, ctx.FollowingActivities, ct))
                    throw new InvalidOperationException("STAGE_COMMIT_REFERENCED");
                // A failed run may have released its earlier pin. Reacquire it before
                // resolving these bytes on every retry and hold it through publication.
                if (!await store.PinConsumerAsync(registered.OperationId, AcquisitionPlanArtifactInputGuard.Consumer(ctx.RunId, target.Id),
                    "workflow", ctx.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct))
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                forwardPublication = true;
            }
            var stepIndex = plan.Steps.FindIndex(s => s.Id == boundary);
            if (stepIndex > 0 && selectedInputId == null && signal?.Password == null && signal?.ExtractionPlanJson == null &&
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
                        // The continuation pins the selected download. A bound processing stage
                        // can have its own copy and hence a different checkpoint input identity.
                        var priorInput = registered;
                        if (registered.Kind == AcquisitionCurrentDirectoryFiles.Kind && !previous.ParentArtifactIds.Contains(registered.Id) &&
                            target.StageDirectories?.Processing?.BindingId != null)
                            priorInput = await AcquisitionProcessingStageInput.ReadPreparedAsync(db, store,
                                ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>(), continuation.TaskId,
                                checked((int)ctx.RunId), target, registered, ct) ?? registered;
                        checkpoint = await processor.ReadCheckpointContinuationAsync(intent, priorInput, serialized,
                            target.SelectedSource?.ArchivePassword, processingConfig, previous, previousStep, ct);
                        if (checkpoint != null) registered = priorInput;
                    }
                    catch (Exception error) when (error is InvalidDataException or JsonException)
                    { checkpointFailure = new(null, "NeedsReconciliation", error.Message); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { checkpointFailure = new(null, "StorageUnavailable", error.Message); }
                }
            }
            // Only an exact active continuation with a complete private snapshot may avoid
            // rereading the remote original. The executor rechecks all private SHA values.
            input = checkpoint == null && checkpointFailure == null ? await ResolveProcessingInputAsync(continuation.ArtifactId, registered) : registered;
        }
        else
        {
            target = await coordinator.RecordAsync(ctx, target, workset.OutputDirectory, ct);
            input = target.DownloadArtifactId == null ? null : await ResolveProcessingInputAsync(target.DownloadArtifactId);
        }
        if (input == null) throw new InvalidOperationException("The downloaded file artifact is missing or modified.");
        if (groupAction && (input.Id != signal!.ArtifactId || input.Fingerprint != signal.Fingerprint))
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        // A pinned checkpoint has already bound and verified its private input. Do
        // not require the original selected directory again after that hand-off.
        if (checkpoint == null && checkpointFailure == null && input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
            await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ct);
        if (!forwardPublication && checkpoint == null && checkpointFailure == null && signal?.StorageRecovery == null)
        {
            await ctx.ReportStage("preparingFiles");
            var waitForInstructions = processingConfig.ResolveInitialPlan(plan) == null;
            try
            {
                input = await AcquisitionProcessingStageInput.PrepareAsync(ctx, target, input, ct,
                    processingConfig.StoragePolicy, plan, waitForInstructions);
            }
            catch (ArchiveFormatProbeIncompleteException error)
            {
                return SuspendProcessing(workset, target, config, serialized, input,
                    new FileArtifactProcessingResult(null, error.Code, error.Message));
            }
        }
        if (checkpointFailure == null && config.UseVerifiedInput && config.ContinueFrom != null && input.Kind is "processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind)
        {
            // The attachment node has already pinned this task's immutable artifact until the run ends.
            // Carry its exact directory and manifest into assessment/publication, without staging a copy.
            var original = await store.GetOperationAsync(input.OperationId, ct);
            if (original == null || string.IsNullOrWhiteSpace(original.IntentKey))
                throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            // A forwarded publication is still owned by its producer. The continuation
            // binding records our read; do not register its operation as our own processing.
            if (!forwardPublication) await Bind(ctx, target, input, null, ct);
            return WorkflowItemOutcome.ReplaceWith(Replace(workset, target with
            {
                ProcessedArtifactId = input.Id, ProcessingOperationId = input.OperationId,
                ProcessedDirectory = input.RootDirectory, ProcessedFiles = input.Files.ToList(),
                ProcessingIntentKey = input.Kind == "processedFiles" ? original.IntentKey : AcquisitionPlanArtifactInputGuard.Intent(config.ContinueFrom),
                ExecutionStatus = "reused", ProcessingWaitReason = null, Problem = null
            }));
        }
        if (target.StageDirectories?.Processing is {BindingId: not null} stage && ArtifactManifestFiles.PathComparer.Equals(stage.Directory, input.RootDirectory))
            processingConfig = processingConfig with {BoundInputDirectory = stage.Directory};
        string? originalDownloadPassword = null;
        if (checkpointFailure == null && target.SelectedSource?.ArchivePassword is {Length: > 0} selectedPassword &&
            plan.Steps.Any(step => (boundary == null || step.Id == boundary) && step.Op == "extractArchive" &&
                step.Password != null && step.Password != selectedPassword && step.GetInputReferences() is ["download"]))
        {
            var evidence = await AcquisitionFileProcessingEvidence.ReadAsync(ctx, target.Id, target.SelectedSource.Url, ct,
                includeDescription: false);
            originalDownloadPassword = await AcquisitionFileProcessingEvidence.OriginalDownloadPasswordAsync(evidence, target,
                input, store, ct, ctx.Services.GetRequiredService<BakabaseDbContext>(), checked((int)ctx.RunId));
        }
        var result = checkpointFailure ?? await processor.ExecuteAsync(intent, input, serialized, target.SelectedSource?.ArchivePassword,
            processingConfig,
            DownloadArtifactCoordinator.Consumer(ctx, target), null, signal?.Password,
            value => ctx.ReportProgress(value, target.Title).GetAwaiter().GetResult(), ct,
            replan: false, stopAfterStepId: boundary, recovery: signal?.StorageRecovery, checkpointContinuation: checkpoint,
            passwordEvidence: AcquisitionArchivePasswordEvidence.Bind(ctx, target.Id, target.SelectedSource?.Url), reportStage: ctx.ReportStage,
            originalDownloadPassword: originalDownloadPassword);
        if (result.Artifact == null) return SuspendProcessing(workset, target, config, serialized, input, result);
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
    private static async Task<bool> CanPromptForPreparedDownloadAsync(WorkflowExecutionContext ctx, BakabaseDbContext db,
        int taskId, CloudDownloadTarget target, ArtifactRecord registered, CancellationToken ct)
    {
        if (registered is not {Kind: "download", State: ArtifactLocationState.Available} || target.StageDirectories?.Processing?.BindingId != null ||
            target is not {ProcessingWaitReason: null, ProcessingIntentKey: null, ProcessingOperationId: null,
                ProcessedArtifactId: null, ProcessedDirectory: null, ProcessedFiles.Count: 0, ProcessingStepArtifacts.Count: 0} ||
            ctx.Services.GetService<IUserStoragePolicy>() is not {} storage) return false;
        var runId = checked((int)ctx.RunId);
        return await db.AcquisitionTasks.AnyAsync(t => t.Id == taskId && t.WorkflowRunId == runId && t.DeletedAt == null, ct) &&
            await AcquisitionPlanArtifactInputGuard.HasPinnedContinuationAsync(db, runId, target.Id, registered.OperationId, ct) &&
            AcquisitionPlanArtifactInputGuard.HasExpectedFileLayout(registered, storage, ct);
    }

    private static WorkflowItemOutcome SuspendProcessing(DownloadWorkset workset, CloudDownloadTarget target,
        AcquisitionPlanProcessingConfig config, string serialized, ArtifactRecord input, FileArtifactProcessingResult result)
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
            ArchiveGroupInspected = reason == "MissingVolumes" && config.StopAfterStepId != null,
            result.IncompleteArchiveGroup,
            AllowedActions = result.IncompleteArchiveGroup != null ? new[] {FileProcessingResumeActions.RetainGroup} : FileProcessingResumeActions.Allowed(reason),
            AllowedArchivePolicies = config.StopAfterStepId == null ? new[] {"instructions", "keep"} : new[] {"instructions"}
        }, WorkflowJson.Options), Replace(workset, target)));
    }

    internal static FileProcessingPlan EffectivePlan(AcquisitionPlanProcessingConfig config, CloudDownloadTarget target) =>
        config.UseVerifiedInput ? new() {Requirement = "notRequired"} :
        FileExtractionLayouts.Apply(config.StopAfterStepId == null && target.ProcessingPlanJson != null
            ? JsonSerializer.Deserialize<FileProcessingPlan>(target.ProcessingPlanJson, WorkflowJson.Options) ?? config.Plan : config.Plan, config.ExtractionLayout);

    internal static async Task<bool> OwnsCheckpointBindingsAsync(BakabaseDbContext db, long runId, int taskId,
        string targetId, string inputOperation, string previousOperation, string previousStep, CancellationToken ct)
    {
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, taskId, ct) &&
            await AcquisitionPlanArtifactInputGuard.HasPinnedContinuationAsync(db, checked((int)runId), targetId, inputOperation, ct) &&
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
