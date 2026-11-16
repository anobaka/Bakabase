using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bakabase.Abstractions.Components.FileSystem;

using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileArtifactDeliveryReview(string ArtifactId, string Fingerprint,
    string? PlanJson = null, string? Intent = null, string? WaitReason = null, bool Accepted = false,
    bool AutomaticOuter = false, RecursiveArchiveOptions? Limits = null, string? ArchiveId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ExtractionLayout = null);
public sealed record FileArtifactDeliverySignal(string ArtifactId, string Fingerprint, string ReviewToken, CloudFileProcessingSignal Processing);
public sealed record FileArtifactDeliveryAssessmentConfig(string TargetId, bool AutoContinueOuterPackaging = true,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ExtractionLayout = null,
    bool EnableAdaptiveAssessment = false,
    int MaxAdaptiveIterations = 3,
    string? AdaptiveContext = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RequiresAdaptiveDecision = false);

/// <summary>Explicit final check: never transparent to upstream artifact demand planning.</summary>
public sealed class AssessFileArtifactDeliveryActivity : IResumableWorkflowActivity
{
    public const string ActivityKind = "fs.assessDelivery";
    public string Kind => ActivityKind;
    public string DisplayName => "Review remaining archive packaging";
    public string DescriptionKey => "workflow.activity.fsAssessDelivery.description";
    public string Group => "fs";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];

    private static FileArtifactDeliveryAssessmentConfig Config(string json)
    {
        var result = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(json, WorkflowJson.Options);
        if (string.IsNullOrWhiteSpace(result?.TargetId) || result.TargetId.Length > 128)
            throw new ArgumentException("A delivery review requires an exact target.");
        return result;
    }
    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try { Config(context.ConfigJson); return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([]); }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([new() {Code = "deliveryReviewInvalid", Message = e.Message}]); }
    }
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
        ReviewAsync(ctx, (DownloadWorkset)item, Config(ctx.ActivityConfigJson), null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        ReviewAsync(ctx, (DownloadWorkset)item, Config(ctx.ActivityConfigJson), JsonSerializer.Deserialize<FileArtifactDeliverySignal>(signalJson, WorkflowJson.Options)
            ?? throw new ArgumentException("Provide a delivery-review answer."), ct);

    private static DownloadWorkset Replace(DownloadWorkset workset, CloudDownloadTarget target) =>
        workset with {Targets = workset.Targets.Select(t => t.Id == target.Id ? target : t).ToList()};
    private static bool CanRevise(string? reason) => reason is FileArtifactDeliveryAssessment.ReviewRequired or FileArtifactDeliveryAssessment.ResultUncertain or
        "ExtractionPlanUnknown" or "ArchiveInputRequired" or "MissingVolumes" or "ProcessingInputNotMatched" or "RepeatedContent" or "ExtractionLayoutConflict";

    private static bool BudgetWait(string? reason) => reason is "DepthLimit" or "ArchiveCountLimit" or
        "FileCountLimit" or "ExpandedBytesLimit" or "DiskSpaceLimit";
    private static RecursiveArchiveOptions DefaultOuterLimits() => new()
        {Enabled = true, MaxDepth = 3, MaxArchiveGroups = 3, MaxFiles = 10000, MaxExpandedBytes = 20L * 1024 * 1024 * 1024};
    private static FileArtifactProcessingConfig ProcessingConfig(FileArtifactDeliveryReview review, CloudDownloadTarget target) => new()
        {ArchivePolicy = review.AutomaticOuter ? "autoDetect" : "instructions", Recursive = review.Limits ?? new(),
            StoragePolicy = target.StoragePolicy, StorageTitle = target.Title, ExtractionLayout = review.ExtractionLayout,
            WorkspaceDirectoryScope = target.ProcessingDirectoryScope};

    internal static string ReviewToken(string targetId, FileArtifactDeliveryReview review) =>
        ArtifactManifestFiles.Hash("delivery-review-v1:" + targetId + ":" + JsonSerializer.Serialize(review, WorkflowJson.Options));

    private static void ValidateAnswer(WorkflowExecutionContext ctx, CloudDownloadTarget target, FileArtifactDeliverySignal answer)
    {
        var review = target.DeliveryReview;
        if (review == null || review.Accepted || answer.Processing == null || answer.Processing.TargetId != target.Id ||
            answer.ArtifactId != review.ArtifactId || answer.Fingerprint != review.Fingerprint ||
            answer.ReviewToken != ReviewToken(target.Id, review) || ctx.ResumePromptJson == null)
            throw new ArgumentException("FILE_DELIVERY_REVIEW_CHANGED");
        using var document = JsonDocument.Parse(ctx.ResumePromptJson);
        var prompt = document.RootElement;
        if (prompt.GetProperty("artifactId").GetString() != review.ArtifactId ||
            prompt.GetProperty("fingerprint").GetString() != review.Fingerprint ||
            prompt.GetProperty("targetId").GetString() != target.Id ||
            prompt.GetProperty("reason").GetString() != review.WaitReason ||
            prompt.GetProperty("reviewToken").GetString() != answer.ReviewToken)
            throw new ArgumentException("FILE_DELIVERY_REVIEW_CHANGED");
        var input = answer.Processing;
        if (input.StorageRecovery != null ||
            input.RecursiveOptions != null && (!review.AutomaticOuter || !BudgetWait(review.WaitReason)) ||
            input.ArchiveId != null && (!review.AutomaticOuter || input.ArchiveId != review.ArchiveId) ||
            input.Password?.Length > 2048 || input.ExtractionPlanJson?.Length > 131072 || input.UserContext?.Length > 16384 ||
            input.ArchivePolicy is not (null or "keep" or "instructions"))
            throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
        if (input.RecursiveOptions != null) RecursiveArchiveExecutor.ValidateOptions(input.RecursiveOptions);
        if (input.Password != null && review.WaitReason != "PasswordUnknown")
            throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
        if (review.AutomaticOuter && input.Password != null && input.ArchiveId != review.ArchiveId)
            throw new ArgumentException("FILE_DELIVERY_REVIEW_CHANGED");
        if (CanRevise(review.WaitReason))
        {
            var hasUserContext = !string.IsNullOrWhiteSpace(input.UserContext);
            if (input.Action != null || input.Password != null ||
                input.ArchivePolicy == "keep" && input.ExtractionPlanJson != null ||
                !hasUserContext && input.ArchivePolicy != "keep" && input.ExtractionPlanJson == null ||
                hasUserContext && (input.ArchivePolicy != null || input.ExtractionPlanJson != null))
                throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
        }
        else
        {
            if (input.ExtractionPlanJson != null || input.ArchivePolicy != null)
                throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
            FileProcessingResumeActions.Validate(ctx, target.Id, review.WaitReason, input, allowSourceEvidence: true);
        }
    }

    internal static async Task<WorkflowItemOutcome> ReviewAsync(WorkflowExecutionContext ctx, DownloadWorkset workset,
        FileArtifactDeliveryAssessmentConfig config, FileArtifactDeliverySignal? answer, CancellationToken ct)
    {
        using var writeScope = StorageWriteScope.Enter(new("workflow", ctx.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var target = workset.Targets.Single(t => t.Id == config.TargetId);
        if (target.Excluded) return WorkflowItemOutcome.ReplaceWith(workset);
        if (answer != null) ValidateAnswer(ctx, target, answer);
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var review = target.DeliveryReview;
        if (review != null && review.ArtifactId != target.ProcessedArtifactId)
            throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        var id = review?.ArtifactId ?? target.ProcessedArtifactId;
        var input = id == null ? null : await store.ResolveByIdAsync(id, ct);
        if (input == null || review != null && (input.Fingerprint != review.Fingerprint || input.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind)))
            throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
            await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ct);
        var wasExtracted = await FileArtifactDeliveryAssessment.WasExtractedBeforeDeliveryAsync(input, store, ct);
        if (review?.Accepted == true) return WorkflowItemOutcome.ReplaceWith(workset);
        // Protect the immutable input through human review and publication.
        // Terminal workflow states release the durable owner without a timing gap between nodes.
        var consumer = "workflow:" + ctx.RunId + ":delivery-input:" + input.Id;
        if (!await store.PinConsumerAsync(input.OperationId, consumer, "workflow", ctx.RunId.ToString(), ct))
            throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        input = await store.ResolveByIdAsync(input.Id, ct)
            ?? throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        // A migrated historical run already has a deterministic DeliveryReview parked on the
        // target. The adaptive planner must still get a chance to inspect the same immutable
        // artifact before that review is treated as a final human-only checkpoint. Once a review
        // has been accepted, however, never reinterpret it automatically.
        var adaptiveService = ctx.Services.GetService<AdaptiveFileProcessingAssessmentService>();
        if ((answer == null || !string.IsNullOrWhiteSpace(answer.Processing.UserContext)) && config.EnableAdaptiveAssessment && review?.Accepted != true &&
            adaptiveService is { } adaptive)
        {
            ctx.Logger.LogInformation("Running adaptive post-download assessment for run {RunId}, target {TargetId}, artifact {ArtifactId}",
                ctx.RunId, target.Id, input.Id);
            var adaptiveOutcome = await RunAdaptiveAsync(ctx, workset, target, input, config, store, adaptive,
                answer?.Processing.UserContext, ct);
            if (adaptiveOutcome is { } outcome) return outcome;
        }
        else if (answer == null && config.EnableAdaptiveAssessment)
        {
            ctx.Logger.LogWarning("Adaptive post-download assessment is enabled but its service is unavailable for run {RunId}", ctx.RunId);
        }

        // Explicit AI review is a decision boundary. An unavailable model must not authorize publication.
        if (config.RequiresAdaptiveDecision && (answer == null || !string.IsNullOrWhiteSpace(answer.Processing.UserContext)))
            return Suspend(workset, target, review ?? new(input.Id, input.Fingerprint), FileArtifactDeliveryAssessment.ResultUncertain,
                input.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                adaptiveAssessment: true);

        if (review == null)
        {
            var assessment = await FileArtifactDeliveryAssessment.AssessAsync(input, ct, extractionWasReviewed: wasExtracted);
            if (assessment.Files.Count == 0) return WorkflowItemOutcome.ReplaceWith(workset);
            var inherited = input.Kind == "processedFiles" && input.MetadataJson != null
                ? JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(input.MetadataJson, WorkflowJson.Options)?.InheritedExtractionLayout : null;
            review = new(input.Id, input.Fingerprint, ExtractionLayout: inherited ?? config.ExtractionLayout);
            if (!assessment.Automatic || !config.AutoContinueOuterPackaging)
                return Suspend(workset, target, review, assessment.Uncertain ? FileArtifactDeliveryAssessment.ResultUncertain :
                    FileArtifactDeliveryAssessment.ReviewRequired, assessment.Files, adaptiveAssessment: config.EnableAdaptiveAssessment);
            var autoPlan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "notRequired"}, WorkflowJson.Options);
            review = review with {AutomaticOuter = true, Limits = DefaultOuterLimits(), PlanJson = autoPlan};
            review = review with {Intent = FileArtifactProcessor.Intent("delivery-outer-v1:" + input.OperationId + ":" + input.Fingerprint,
                autoPlan, target.SelectedSource?.ArchivePassword, ProcessingConfig(review, target))};
        }
        if (answer?.Processing.ArchivePolicy == "keep")
        {
            return WorkflowItemOutcome.ReplaceWith(Replace(workset, target with
            {DeliveryReview = review with {Accepted = true, WaitReason = null}, ProcessingWaitReason = null, Problem = null}));
        }
        if (answer?.Processing.ExtractionPlanJson is { } instructions)
        {
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(instructions, WorkflowJson.Options);
            FileProcessingPlanValidator.Validate(plan);
            if (plan == null || plan.Requirement != "required" || plan.Steps.Count == 0)
                throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
            var normalized = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            var intent = FileArtifactProcessor.Intent("delivery-review-v1:" + input.OperationId + ":" + input.Fingerprint,
                normalized, target.SelectedSource?.ArchivePassword, new() {ExtractionLayout = review.ExtractionLayout});
            review = review with {PlanJson = normalized, Intent = intent, AutomaticOuter = false, Limits = null, ArchiveId = null};
        }
        if (answer?.Processing.RecursiveOptions is { } revisedLimits)
            review = review with {Limits = revisedLimits with {Enabled = true}};
        // Apply an explicit review answer first. A checkpoint without a plan needs a fresh assessment.
        if (review.PlanJson == null || review.Intent == null)
        {
            var reassessment = await FileArtifactDeliveryAssessment.AssessAsync(input, ct,
                extractionWasReviewed: true);
            if (reassessment.Files.Count == 0)
            {
                return WorkflowItemOutcome.ReplaceWith(Replace(workset, target with
                {
                    DeliveryReview = null,
                    ProcessingWaitReason = null,
                    Problem = null,
                    ExecutionStatus = "completed"
                }));
            }

            var reassessmentReason = reassessment.Uncertain
                ? FileArtifactDeliveryAssessment.ResultUncertain
                : FileArtifactDeliveryAssessment.ReviewRequired;
            return Suspend(workset, target, review, reassessmentReason, reassessment.Files,
                adaptiveAssessment: config.EnableAdaptiveAssessment);
        }
        // Only the retained processed artifact is input. Neither its original download nor a
        // provider operation can be re-created by a review answer.
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":process:delivery-input:" + input.Id, input.OperationId, ct);
        FileArtifactDeliveryStorageContinuation.Checkpoint? savedAdaptive = null;
        var savedPlan = JsonSerializer.Deserialize<FileProcessingPlan>(review.PlanJson, WorkflowJson.Options)!;
        if (answer != null && (review.WaitReason is "StorageUnavailable" or "DiskSpaceLimit") &&
            FileArtifactDeliveryStorageContinuation.IsAdaptivePlan(savedPlan))
        {
            savedAdaptive = await FileArtifactDeliveryStorageContinuation.ReadAsync(
                ctx.Services.GetRequiredService<Bakabase.InsideWorld.Business.BakabaseDbContext>(), store,
                ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ctx.RunId, target, input, review,
                config, savedPlan, ProcessingConfig(review, target), ct);
            if (savedAdaptive == null)
                return Suspend(workset, target, review, "NeedsReconciliation",
                    input.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                    "RECOVERY_CHECKPOINT_UNSUPPORTED", config.EnableAdaptiveAssessment);
        }
        var result = await ctx.Services.GetRequiredService<FileArtifactProcessor>().ExecuteAsync(review.Intent,
            input, review.PlanJson, target.SelectedSource?.ArchivePassword,
            ProcessingConfig(review, target), savedAdaptive?.Consumer ?? "workflow:" + ctx.RunId + ":delivery-review:" + target.Id,
            review.AutomaticOuter && answer?.Processing.Password is { } recursivePassword && review.ArchiveId != null
                ? new(review.ArchiveId, recursivePassword) : null,
            review.AutomaticOuter ? null : answer?.Processing.Password,
            value => ctx.ReportProgress(value, target.Title).GetAwaiter().GetResult(), ct,
            replan: false, stopAfterStepId: null, recovery: savedAdaptive == null ? null : new(savedAdaptive.OperationId, savedAdaptive.Attempt), onlyOuterPackaging: review.AutomaticOuter,
            passwordEvidence: AcquisitionArchivePasswordEvidence.Bind(ctx, target.Id, target.SelectedSource?.Url));
        if (result.Artifact == null)
            return Suspend(workset, target, review with {ArchiveId = result.ArchiveId}, result.WaitReason ?? "ExtractionPlanUnknown",
                input.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(), result.Message, config.EnableAdaptiveAssessment);
        var artifact = result.Artifact;
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":process:delivery-result:" + artifact.Id, artifact.OperationId, ct);
        artifact = await AcquisitionProcessingStageCommit.CommitAsync(ctx, target, artifact, ct);
        target = target with
        {
            ProcessedArtifactId = artifact.Id, ProcessedDirectory = artifact.RootDirectory,
            ProcessedFiles = artifact.Files.ToList(), ProcessingOperationId = artifact.OperationId,
            ProcessingIntentKey = review.Intent, ProcessingWaitReason = null, Problem = null,
            PublishedArtifactId = null, PublishedDirectory = null, PublishedFiles = [], PublicationOperationId = null,
            PublicationTargetDirectory = null, PublicationWaitReason = null, PublicationStatus = null,
            ExecutionStatus = "completed", DeliveryReview = null
        };
        if (!await store.PinConsumerAsync(artifact.OperationId, "workflow:" + ctx.RunId + ":delivery-input:" + artifact.Id,
                "workflow", ctx.RunId.ToString(), ct) || await store.ResolveByIdAsync(artifact.Id, ct) == null)
            throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        if (savedAdaptive != null)
        {
            await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":adaptive:output:" + artifact.Id, artifact.OperationId, ct);
            if (adaptiveService != null)
            {
                var completed = AdaptiveProcessingExecutionEvidence.Capture(savedAdaptive.Iteration + 1, input, artifact,
                    await store.GetOperationAsync(artifact.OperationId, ct));
                var nextAssessment = await RunAdaptiveAsync(ctx, Replace(workset, target), target, artifact, config, store,
                    adaptiveService, null, ct, savedAdaptive.Iteration + 1,
                    initialCompletedProcessing: completed == null ? null : [completed]);
                if (nextAssessment is { } nextOutcome) return nextOutcome;
            }
            return Suspend(Replace(workset, target), target, new(artifact.Id, artifact.Fingerprint),
                FileArtifactDeliveryAssessment.ResultUncertain,
                artifact.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                "LLM_ASSESSMENT_FAILED", adaptiveAssessment: true);
        }
        var remaining = await FileArtifactDeliveryAssessment.AssessAsync(artifact, ct, extractionWasReviewed: true);
        if (remaining.Files.Count > 0)
        {
            return Suspend(Replace(workset, target), target, new(artifact.Id, artifact.Fingerprint),
                remaining.Uncertain ? FileArtifactDeliveryAssessment.ResultUncertain : FileArtifactDeliveryAssessment.ReviewRequired, remaining.Files,
                adaptiveAssessment: config.EnableAdaptiveAssessment);
        }
        return WorkflowItemOutcome.ReplaceWith(Replace(workset, target));
    }

    /// <summary>
    /// Runs a bounded, post-download planning loop. The activity remains one persisted workflow
    /// node: workflow step indexes and existing checkpoints are never changed at runtime. Each
    /// accepted proposal is compiled into the existing deterministic processor and produces a
    /// new immutable artifact before the next assessment round.
    /// </summary>
    private static async Task<WorkflowItemOutcome?> RunAdaptiveAsync(WorkflowExecutionContext ctx,
        DownloadWorkset workset, CloudDownloadTarget target, ArtifactRecord input,
        FileArtifactDeliveryAssessmentConfig config, IArtifactOperationStore store,
        AdaptiveFileProcessingAssessmentService adaptive, string? userContext, CancellationToken ct, int startIteration = 0,
        IReadOnlyList<AdaptiveCompletedProcessing>? initialCompletedProcessing = null)
    {
        var maxIterations = Math.Clamp(config.MaxAdaptiveIterations, 1, AdaptiveFileProcessingAssessmentService.MaxCallsPerTask);
        var current = input;
        var currentTarget = target;
        var sourceEvidence = await AcquisitionFileProcessingEvidence.ReadAsync(ctx, target.Id, target.SelectedSource?.Url, ct);
        var completedProcessing = initialCompletedProcessing == null ? new List<AdaptiveCompletedProcessing>() :
            AdaptiveProcessingExecutionEvidence.ForCurrent(current, initialCompletedProcessing)?.CompletedProcessing.ToList() ??
            new List<AdaptiveCompletedProcessing>();
        for (var iteration = startIteration; iteration < maxIterations; iteration++)
        {
            var executionEvidence = AdaptiveProcessingExecutionEvidence.ForCurrent(current, completedProcessing);
            var assessmentContext = AdaptiveFileProcessingAssessmentService.DescribeContext(
                target.Title, config.AdaptiveContext, userContext, sourceEvidence,
                executionEvidence);
            var inspectImagePackaging = !string.IsNullOrWhiteSpace(target.SelectedSource?.ArchivePassword) ||
                AdaptiveFileProcessingAssessmentService.HasExtractionStep(target.SelectedSource?.ExtractionPlanJson) ||
                executionEvidence?.CompletedProcessing.Any(round => round.Operations.Any(op => op.Kind == "extractArchive")) == true;
            var decision = await adaptive.AssessAsync(current, assessmentContext, iteration, ct, inspectImagePackaging, executionEvidence);
            var blocks = new List<WorkflowOutputBlock>();
            if (AdaptiveAssessmentDiagnostic.FromCode(decision.DiagnosticCode) is { } diagnostic) blocks.Add(diagnostic);
            if (decision.Call is {} call) blocks.Add(call.Block(currentTarget.Id));
            if (blocks.Count > 0) await ctx.ReportOutputMetadata(blocks);
            if (decision.IsFinal)
            {
                // A model's final vote must review the current result even when a later rename
                // or directory rebase has replaced the extraction metadata. This bounded check
                // never authorizes unpacking application data or unknown companions.
                var remaining = await FileArtifactDeliveryAssessment.AssessAsync(current, ct,
                    extractionWasReviewed: true);
                if (remaining.Files.Count > 0)
                    return Suspend(Replace(workset, currentTarget), currentTarget,
                        new FileArtifactDeliveryReview(current.Id, current.Fingerprint),
                        remaining.Uncertain ? FileArtifactDeliveryAssessment.ResultUncertain :
                            FileArtifactDeliveryAssessment.ReviewRequired,
                        remaining.Files, adaptiveAssessment: true);
                // Publication with Source=processed still needs the deterministic processing
                // intent that produced this artifact. Historical runs normally already carry
                // it; if it is absent, fall through to the legacy assessment path instead of
                // allowing the following publication node to fail with a missing source intent.
                var operation = await store.GetOperationAsync(current.OperationId, ct);
                var processingIntent = currentTarget.ProcessingIntentKey ?? operation?.IntentKey;
                if (string.IsNullOrWhiteSpace(processingIntent)) return null;
                return WorkflowItemOutcome.ReplaceWith(Replace(workset, currentTarget with
                {
                    ProcessedArtifactId = current.Id, ProcessedDirectory = current.RootDirectory,
                    ProcessedFiles = current.Files.ToList(), ProcessingOperationId = current.OperationId,
                    ProcessingIntentKey = processingIntent,
                    ProcessingWaitReason = null, Problem = null,
                    ExecutionStatus = "completed", DeliveryReview = null
                }));
            }

            // Provider failures should not make old deterministic recovery paths unusable. The
            // caller falls through to the existing assessment below, which can still auto-handle
            // an unambiguous outer archive or suspend with the normal review token.
            if (!config.RequiresAdaptiveDecision && decision.NeedsHumanReview && decision.Reason?.StartsWith("LLM_", StringComparison.Ordinal) == true)
                return null;

            if (!decision.ShouldContinue)
            {
                var review = new FileArtifactDeliveryReview(current.Id, current.Fingerprint);
                return Suspend(Replace(workset, currentTarget), currentTarget, review,
                    FileArtifactDeliveryAssessment.ResultUncertain,
                    current.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                    decision.Reason ?? "需要人工确认文件处理方案。", config.EnableAdaptiveAssessment);
            }

            FileProcessingPlan? plan;
            try { plan = AdaptiveFileProcessingAssessmentService.BuildPlan(decision, config.ExtractionLayout); }
            catch (InvalidOperationException error)
            {
                var invalidReview = new FileArtifactDeliveryReview(current.Id, current.Fingerprint);
                return Suspend(Replace(workset, currentTarget), currentTarget, invalidReview,
                    FileArtifactDeliveryAssessment.ResultUncertain,
                    current.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                    "模型方案未通过安全校验：" + error.Message, config.EnableAdaptiveAssessment);
            }
            if (plan == null)
            {
                var review = new FileArtifactDeliveryReview(current.Id, current.Fingerprint);
                return Suspend(Replace(workset, currentTarget), currentTarget, review,
                    FileArtifactDeliveryAssessment.ResultUncertain,
                    current.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                    decision.Reason ?? "模型未生成可执行的处理方案。", config.EnableAdaptiveAssessment);
            }

            var normalized = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            var adaptiveProcessingConfig = new FileArtifactProcessingConfig
            {
                ExtractionLayout = config.ExtractionLayout, StoragePolicy = currentTarget.StoragePolicy,
                StorageTitle = currentTarget.Title, WorkspaceDirectoryScope = currentTarget.ProcessingDirectoryScope
            };
            var reviewForExecution = new FileArtifactDeliveryReview(current.Id, current.Fingerprint,
                PlanJson: normalized, Intent: FileArtifactProcessor.Intent(
                    "adaptive-file-processing-v1:" + current.OperationId + ":" + current.Fingerprint + ":" + decision.DecisionHash,
                    normalized, currentTarget.SelectedSource?.ArchivePassword,
                    adaptiveProcessingConfig),
                AutomaticOuter: false, ExtractionLayout: config.ExtractionLayout);
            await ArtifactWorkflowBindings.BindAsync(ctx, currentTarget.Id + ":adaptive:input:" + current.Id,
                current.OperationId, ct);
            var result = await ctx.Services.GetRequiredService<FileArtifactProcessor>().ExecuteAsync(
                reviewForExecution.Intent!, current, normalized, currentTarget.SelectedSource?.ArchivePassword,
                adaptiveProcessingConfig,
                "workflow:" + ctx.RunId + ":adaptive:" + currentTarget.Id + ":" + iteration,
                null, null, value => ctx.ReportProgress(value, currentTarget.Title).GetAwaiter().GetResult(), ct,
                replan: false, stopAfterStepId: null, recovery: null, onlyOuterPackaging: false,
                passwordEvidence: AcquisitionArchivePasswordEvidence.Bind(ctx, currentTarget.Id, currentTarget.SelectedSource?.Url));
            if (result.Artifact == null)
            {
                return Suspend(Replace(workset, currentTarget), currentTarget,
                    reviewForExecution with {ArchiveId = result.ArchiveId},
                    result.WaitReason ?? FileArtifactDeliveryAssessment.ResultUncertain,
                    current.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
                    result.Message, config.EnableAdaptiveAssessment);
            }

            var completed = AdaptiveProcessingExecutionEvidence.Capture(iteration + 1, current, result.Artifact,
                await store.GetOperationAsync(result.Artifact.OperationId, ct));
            if (completed == null) completedProcessing.Clear();
            else completedProcessing.Add(completed);
            current = await AcquisitionProcessingStageCommit.CommitAsync(ctx, currentTarget, result.Artifact, ct);
            if (completed != null && current.Id != result.Artifact.Id)
                completedProcessing[^1] = completed with {OutputArtifactId = current.Id, OutputFingerprint = current.Fingerprint};
            await ArtifactWorkflowBindings.BindAsync(ctx, currentTarget.Id + ":adaptive:output:" + current.Id,
                current.OperationId, ct);
            if (!await store.PinConsumerAsync(current.OperationId,
                    "workflow:" + ctx.RunId + ":delivery-input:" + current.Id,
                    "workflow", ctx.RunId.ToString(), ct))
                throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
            currentTarget = currentTarget with
            {
                ProcessedArtifactId = current.Id, ProcessedDirectory = current.RootDirectory,
                ProcessedFiles = current.Files.ToList(), ProcessingOperationId = current.OperationId,
                ProcessingIntentKey = reviewForExecution.Intent, ProcessingWaitReason = null, Problem = null,
                PublishedArtifactId = null, PublishedDirectory = null, PublishedFiles = [],
                PublicationOperationId = null, PublicationTargetDirectory = null,
                PublicationWaitReason = null, PublicationStatus = null, ExecutionStatus = "completed",
                DeliveryReview = null
            };
        }

        var exhausted = new FileArtifactDeliveryReview(current.Id, current.Fingerprint);
        return Suspend(Replace(workset, currentTarget), currentTarget, exhausted,
            FileArtifactDeliveryAssessment.ResultUncertain,
            current.Manifest.Entries.Select(e => e.RelativePath).Take(FileArtifactDeliveryAssessment.MaximumFiles).ToArray(),
            "已达到下载后自动判断次数上限，请人工确认剩余文件。", config.EnableAdaptiveAssessment);
    }

    private static WorkflowItemOutcome Suspend(DownloadWorkset workset, CloudDownloadTarget target,
        FileArtifactDeliveryReview review, string reason, IReadOnlyList<string> files, string? message = null,
        bool adaptiveAssessment = false) =>
        WorkflowItemOutcome.Suspend(new("cloud.fileProcessing", JsonSerializer.Serialize(new
        {
            targetId = target.Id, target.Title, reason, message = message ?? reason,
            artifactId = review.ArtifactId, fingerprint = review.Fingerprint,
            reviewToken = ReviewToken(target.Id, review with {WaitReason = reason}), files,
            deliveryReview = true, automaticOuterPackaging = review.AutomaticOuter,
            recursiveOptions = review.Limits, archiveId = review.ArchiveId,
            extractionPlanJson = review.PlanJson ?? "", canEditPlan = CanRevise(reason),
            adaptiveAssessment, sourcePasswordEvidenceChecked = reason == "PasswordUnknown",
            allowedArchivePolicies = new[] {"instructions", "keep"}, allowedActions = FileProcessingResumeActions.Allowed(reason)
        }, WorkflowJson.Options), Replace(workset, target with
        {DeliveryReview = review with {WaitReason = reason}, ProcessingWaitReason = reason, Problem = reason})));
}
