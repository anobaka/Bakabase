using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Owns intent preparation; execution, files and checkpoints remain owned by existing modules.</summary>
public sealed partial class AcquisitionPlanService(BakabaseDbContext db, IEnumerable<IAcquisitionPlanBuilder> builders,
    IAcquisitionService acquisitions, IWorkflowDefinitionService workflows, DownloadIntentService downloads,
    IUserStoragePolicy storage, SourceProcessingPlanService? processingPlans = null, IArtifactOperationStore? artifacts = null,
    AcquisitionOutputDirectoryResolver? outputDirectories = null, AcquisitionDeliveryDirectoryResolver? deliveryDirectories = null,
    FileProcessingWorkspaceResolver? workspaces = null, AcquisitionCreationOriginService? creationOrigins = null,
    AcquisitionStoragePolicyDefaults? storageDefaults = null, AcquisitionDeliveryRetirementQueue? deliveryRetirements = null,
    IEnumerable<IDependentComponentService>? processingDependencies = null,
    AcquisitionDownloadActivityProjection? downloadActivity = null, IStorageWriteGuard? stageWriteGuard = null,
    IServiceScopeFactory? stageScopeFactory = null)
{
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;
    private AcquisitionDeliveryDirectoryResolver DeliveryDirectories => deliveryDirectories ?? new(storage);
    private DbSet<AcquisitionTaskDbModel> Tasks => db.Set<AcquisitionTaskDbModel>();
    public IReadOnlyList<AcquisitionPlanBuilderDescriptor> Builders => builders.Select(b => b.Descriptor)
        .Append(new("prepared", "acquisition.plan.builder.prepared.name", "acquisition.plan.builder.prepared.description", ["module"], "prepared")).ToList();

    public async Task<AcquisitionPlanTaskDetail> CreateAsync(AcquisitionPlanCreationInput input, CancellationToken ct)
    {
        input = input with {OutputDirectory = outputDirectories?.ResolveNew(input.OutputDirectory) ?? input.OutputDirectory};
        var title = Title(input.Title);
        var builder = builders.SingleOrDefault(b => b.Descriptor.Id == input.BuilderId && b.Descriptor.EntryPoints.Contains("manual"))
            ?? throw new ArgumentException("Choose an available manual acquisition builder.");
        ValidateDirectory(input.OutputDirectory, allowEmpty: true);
        var row = new AcquisitionTaskDbModel {Title = title, BuilderId = builder.Descriptor.Id, Phase = "preparing",
            Status = AcquisitionStatus.Waiting, PlanRevision = 0, TargetDirectory = input.OutputDirectory,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, BuilderStateJson = Serialize(new AcquisitionPlanBuilderState
            {Query = title, DeliverySettings = DeliveryDirectories.Capture(input.DeliverySettings), StoragePolicy = CaptureStorage(input.StoragePolicy),
                CreationOrigins = creationOrigins == null ? [] : await creationOrigins.CaptureAsync([], builder.Descriptor.Id, true, ct)})};
        Tasks.Add(row);
        await db.SaveChangesAsync(ct);
        row.SourceRefsJson = Serialize(new[] {Owner(row)});
        await db.SaveChangesAsync(ct);
        // The durable row exists before the first network request, including cancellation and no hits.
        return await SearchAsync(row.Id, title, ct);
    }

    public async Task<AcquisitionPlanTaskDetail> CreatePreparedPlanAsync(AcquisitionPlan plan,
        IReadOnlyList<WorkflowSourceRef> sourceRefs, CancellationToken ct = default)
    {
        if (plan.OutputDirectory == null)
            plan = plan with {OutputDirectory = outputDirectories?.ResolveNew(null) ?? ""};
        plan = WithoutProducerStoragePolicy(plan) with {DeliveryLayout = null, StageDirectories = null};
        var deliverySettings = DeliveryDirectories.Capture();
        _ = AcquisitionPlanCompiler.Compile(plan);
        ValidateDirectory(plan.OutputDirectory, allowEmpty: true);
        if (sourceRefs.Count > 501) throw new ArgumentException("Too many acquisition source references.");
        await using var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(ct) : null;
        var row = new AcquisitionTaskDbModel {Title = Title(plan.Title), BuilderId = "prepared", Phase = "ready",
            Status = AcquisitionStatus.Waiting, PlanRevision = 0, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        Tasks.Add(row);
        await db.SaveChangesAsync(ct);
        var sources = sourceRefs.Append(Owner(row)).Distinct().ToList();
        row.SourceRefsJson = Serialize(sources);
        row.BuilderStateJson = Serialize(new AcquisitionPlanBuilderState {SourceRefs = sources, DeliverySettings = deliverySettings, StoragePolicy = CaptureStorage(),
            CreationOrigins = creationOrigins == null ? [] : await creationOrigins.CaptureAsync(sourceRefs, "prepared", false, ct)});
        SavePlan(row, plan, sources);
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return await DetailAsync(row, ct);
    }

    public async Task<AcquisitionPlanTaskDetail?> GetAsync(int id, CancellationToken ct)
    {
        var row = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.BuilderId != null, ct);
        return row == null ? null : await DetailAsync(row, ct);
    }

    public async Task<AcquisitionPlanTaskDetail> SearchAsync(int id, string query, CancellationToken ct)
    {
        query = Title(query);
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct);
            var builder = builders.SingleOrDefault(b => b.Descriptor.Id == row.BuilderId)
                ?? throw new InvalidOperationException("This plan was supplied by another module and cannot be searched.");
            row.Phase = "preparing"; row.Status = AcquisitionStatus.Waiting; row.UpdatedAt = DateTime.Now;
            var state = State(row) with {Query = query, Candidates = [], Issues = []};
            if (state.Attempts.Count >= 100) throw new InvalidOperationException("This task has reached its preparation-attempt limit.");
            state.Attempts.Add(new(state.Attempts.Count + 1, query, DateTime.Now, null, "running", 0));
            row.BuilderStateJson = Serialize(state);
            await db.SaveChangesAsync(ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var candidates = await builder.SearchAsync(query, timeout.Token);
                state = state with {Candidates = candidates.ToList(), Issues = candidates.Count == 0
                    ? [new("BUILDER_NO_RESULTS", "No candidates were found. Change the search text and try again.")] : []};
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                state = state with {Issues = [new(error is OperationCanceledException ? "BUILDER_INTERRUPTED" : "BUILDER_SEARCH_FAILED",
                    "The search did not complete. Retry this task after checking the site configuration.")]};
            }
            state.Attempts[^1] = state.Attempts[^1] with {CompletedAt = DateTime.Now,
                Status = state.Issues.Count == 0 ? "completed" : "attention", CandidateCount = state.Candidates.Count,
                Code = state.Issues.FirstOrDefault()?.Code};
            await db.Entry(row).ReloadAsync(CancellationToken.None);
            if (row.Status == AcquisitionStatus.Cancelled) return await DetailAsync(row, CancellationToken.None);
            row.BuilderStateJson = Serialize(state); row.Phase = state.Issues.Any(i => i.Blocking) ? "attention" : "selection"; row.PlanJson = null;
            row.PlanRevision++; row.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(CancellationToken.None);
            return await DetailAsync(row, CancellationToken.None);
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> SelectAsync(int id, AcquisitionPlanSelectionInput input, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            var candidate = State(row).Candidates.SingleOrDefault(c => c.Id == input.CandidateId)
                ?? throw new ArgumentException("Choose a candidate from this task's current search results.");
            var builder = builders.SingleOrDefault(b => b.Descriptor.Id == row.BuilderId)
                ?? throw new InvalidOperationException("The acquisition builder is no longer available.");
            var plan = builder.BuildPlan(candidate, row.Title!, row.TargetDirectory ?? "");
            row.BuilderStateJson = Serialize(State(row) with {Issues = []});
            SavePlan(row, plan, Sources(row), refreshStageDirectories: await HasNeverExecutedAsync(row, ct)); await db.SaveChangesAsync(ct);
            return await DetailAsync(row, ct);
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> UpdateAsync(int id, AcquisitionPlanUpdateInput input, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            if (input.OutputDirectory != null)
            { ValidateDirectory(input.OutputDirectory, allowEmpty: true); row.TargetDirectory = input.OutputDirectory; }
            await UpdateDeliverySettingsAsync(row, input.DeliverySettings, ct);
            await UpdateStoragePolicyAsync(row, input.StoragePolicy, input.StoragePolicyOverrides, ct);
            if (row.PlanJson != null) SavePlan(row, Plan(row) with {OutputDirectory = row.TargetDirectory ?? ""}, Sources(row),
                refreshStageDirectories: input.OutputDirectory != null || input.DeliverySettings != null ||
                    input.StoragePolicy != null || input.StoragePolicyOverrides != null);
            else {row.PlanRevision++; row.UpdatedAt = DateTime.Now;}
            await db.SaveChangesAsync(ct); return await DetailAsync(row, ct);
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> ApplyProcessingRevisionsAsync(int id,
        ApplyProcessingPlanRevisionsInput input, CancellationToken ct)
    {
        if (processingPlans == null || input.Items == null || input.Items.Count is < 1 or > 100 ||
            input.Items.Any(i => i == null) || input.Items.Select(i => i.PartId).Distinct().Count() != input.Items.Count)
            throw new ArgumentException("PROCESSING_PLAN_REVISION_INVALID");
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            var sources = Sources(row);
            var plan = await BindProcessingSourcesAsync(Plan(row), sources, ct);
            foreach (var item in input.Items)
            {
                var part = plan.Parts.SingleOrDefault(p => p.Id == item.PartId);
                if (part == null || part.ProcessingSource != item.Source || !sources.Contains(item.Source))
                    throw new ArgumentException("PROCESSING_PLAN_SOURCE_INVALID");
                var revision = await processingPlans.RequireCurrentRevisionAsync(item.Source, item.RevisionId, ct);
                plan = plan with {Parts = plan.Parts.Select(p => p.Id == part.Id ? p with
                {Processing = revision.Plan, AppliedProcessingRevisionId = revision.Id} : p).ToList()};
            }
            SavePlan(row, plan, sources);
            row.BuilderStateJson = Serialize(State(row) with {Issues = []});
            // The SQL predicate also fences a second process or a concurrent start, not only this process's gate.
            var changed = await Tasks.Where(t => t.Id == id && t.DeletedAt == null && t.PlanRevision == input.ExpectedRevision &&
                t.WorkflowRunId == null && t.Status != AcquisitionStatus.Cancelled).ExecuteUpdateAsync(update => update
                .SetProperty(t => t.PlanRevision, row.PlanRevision).SetProperty(t => t.PlanJson, row.PlanJson)
                .SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson).SetProperty(t => t.BuilderStateJson, row.BuilderStateJson)
                .SetProperty(t => t.Phase, row.Phase).SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
            db.Entry(row).State = EntityState.Detached;
            if (changed != 1) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
            await transaction.CommitAsync(ct);
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> PreflightAsync(int id, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); var plan = Plan(row);
            await ValidateContinuationInputsAsync(row.Id, plan, ct);
            var check = await PreflightDownloadsAsync(Request(plan, $"acquisition:{row.Id}") with
                {IgnoreSourceAvailability = !await HasNeverExecutedAsync(row, ct)}, plan.OutputDirectory, ct);
            var issues = check.Requirements.Concat(check.Candidates.SelectMany(c => c.Requirements))
                .Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList();
            issues.AddRange((await FileProcessingDependencyPreflight.InspectAsync(plan.Parts.Select(AcquisitionPlanCompiler.Processing),
                processingDependencies ?? [], ct)).Select(i => new AcquisitionPlanIssue(i.Code, i.Message)));
            if (check.CanSubmit && await ReplanIdentityIssueAsync(row, ResolvedWorkset(row, plan, Sources(row), check), ct) is { } identityIssue)
                issues.Add(identityIssue);
            row.BuilderStateJson = Serialize(State(row) with {Issues = issues});
            row.Phase = issues.Any(i => i.Blocking) ? "attention" : "ready";
            row.UpdatedAt = DateTime.Now; await db.SaveChangesAsync(ct);
            return await DetailAsync(row, ct);
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> ReplanAsync(int id, AcquisitionPlanReplanInput input, CancellationToken ct,
        Func<string?, Task>? guardDirectory = null)
        => await ReplanCoreAsync(id, input, ct, guardDirectory);

    internal Task<AcquisitionPlanTaskDetail> ReplanLocalFilesWhileLockedAsync(int id, AcquisitionPlanReplanInput input,
        AcquisitionLocalFilesStorage reviewedStorage, Func<string?, Task>? guardDirectory, CancellationToken ct) =>
        ReplanCoreAsync(id, input, ct, guardDirectory, reviewedStorage);

    private async Task<AcquisitionPlanTaskDetail> ReplanCoreAsync(int id, AcquisitionPlanReplanInput input, CancellationToken ct,
        Func<string?, Task>? guardDirectory, AcquisitionLocalFilesStorage? localFilesStorage = null)
    {
        if (artifacts == null || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 8000 ||
            input.Items == null || input.Items.Count is < 1 or > 100 || input.Items.Any(i => i == null || i.Plan == null ||
                i.ArtifactEvidenceIds == null || i.ArtifactEvidenceIds.Count > 10 ||
                i.InputMode != "stageDirectory" && i.ArtifactEvidenceIds.Count == 0 ||
                i.ArtifactEvidenceIds.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 128) ||
                i.ArtifactEvidenceIds.Distinct().Count() != i.ArtifactEvidenceIds.Count) ||
            input.Items.Any(i => i.InputMode is not (null or "verifiedArtifact" or "currentDirectory" or "stageDirectory") ||
                i.InputMode == "stageDirectory" && (!AcquisitionCurrentDirectoryFiles.ValidStage(i.InputStage) || i.ContinueFromArtifactId != null ||
                    i.ArtifactEvidenceIds.Count != 0 || i.ExpectedDeliveryProjectionFingerprint != null) ||
                i.InputMode != "stageDirectory" && i.InputStage != null) ||
            input.Items.Select(i => i.PartId).Distinct().Count() != input.Items.Count ||
            input.Items.Any(i => i.ContinueFromArtifactId != null && !i.ArtifactEvidenceIds.Contains(i.ContinueFromArtifactId)) ||
            input.Items.Sum(i => i.ArtifactEvidenceIds.Count) > 100 ||
            input.Items.Any(i => i.UseAdaptiveAssessment == true &&
                (i.Plan.Requirement != "unknown" || i.Plan.Steps is not {Count: 0} || i.ContinueFromArtifactId == null && i.InputMode != "stageDirectory")) ||
            input.MaxAdaptiveIterations is < 1 or > 5 ||
            input.AdaptiveContext?.Length > 16384 ||
            (input.EnableAdaptiveAssessment != null || input.MaxAdaptiveIterations != null) &&
            input.Items.Any(i => i.ContinueFromArtifactId == null && i.InputMode != "stageDirectory"))
            throw new ArgumentException("PLAN_REPLAN_INVALID");
        var gate = AcquisitionTaskGate.For(id);
        if (localFilesStorage == null) await gate.WaitAsync(ct);
        var replanRunGate = WorkflowRunMutationGate.For(input.ExpectedRunId);
        var replanRunGateHeld = false;
        try
        {
            if (localFilesStorage == null)
            {
                await replanRunGate.WaitAsync(ct);
                replanRunGateHeld = true;
            }
            var row = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.BuilderId != null, ct)
                ?? throw new ArgumentException("PLAN_REPLAN_INVALID");
            AcquisitionTaskGuard.EnsureNotDeleted(row);
            if (State(row).ProcessingStageInputId != null)
            {
                if (workspaces == null || stageScopeFactory == null) throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
                await AcquisitionProcessingStageInput.RecoverAsync(db, artifacts, storage, workspaces, id,
                    new FileArtifactMaterializer(stageWriteGuard ?? throw new InvalidOperationException("STAGE_COMMIT_STORAGE_GUARD_UNAVAILABLE")),
                    stageScopeFactory, ct);
                row = await Tasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            }
            if (State(row).ProcessingStageCommitId != null)
            {
                if (workspaces == null) throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
                using var stageWriteScope = StorageWriteScope.Enter(new("workflow", input.ExpectedRunId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                await AcquisitionProcessingStageCommit.RecoverAsync(db, artifacts, storage, workspaces, id, ct,
                    new FileArtifactMaterializer(stageWriteGuard ?? throw new InvalidOperationException("STAGE_COMMIT_STORAGE_GUARD_UNAVAILABLE")));
                row = await Tasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            }
            if (State(row).DirectoryChange != null) throw new InvalidOperationException("STAGE_DIRECTORY_CHANGE_PENDING");
            Revision(row, input.ExpectedRevision);
            if (row.WorkflowRunId != input.ExpectedRunId) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
            var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == input.ExpectedRunId, ct);
            bool Allowed(AcquisitionTaskDbModel task, WorkflowRunDbModel candidate) =>
                CanReplan(task.Status, candidate) && (candidate.Status != WorkflowRunStatus.Waiting ||
                    AcquisitionProcessingConflictWait.MatchesForLocalContinuation(candidate) || AcquisitionPublicationStorageWait.Matches(candidate) ||
                    input.Items.All(i => i.InputMode is "currentDirectory" or "stageDirectory")) ||
                task.Status == AcquisitionStatus.Waiting && candidate.Status == WorkflowRunStatus.Waiting &&
                    input.Items.All(i => i.InputMode == "stageDirectory") ||
                localFilesStorage != null && AcquisitionLocalFilesRecoveryReceipt.CanReplan(task, candidate);
            if (run == null || !Allowed(row, run)) throw new InvalidOperationException("PLAN_REPLAN_NOT_STOPPED");
            var publicationStorageWait = AcquisitionPublicationStorageWait.Matches(run);
            if (publicationStorageWait && !input.UseCurrentStorageDefaults)
                throw new ArgumentException("PLAN_REPLAN_INVALID");
            var previousStatus = row.Status;
            var previousRunStatus = run.Status;
            var completedRevision = row.Status == AcquisitionStatus.Completed;
            var deliveryOnly = input.RetryDeliveryOnly is true;
            var stageRevision = input.Items.All(i => i.InputMode == "stageDirectory");
            var useCurrentPublicationStorage = publicationStorageWait ||
                deliveryOnly && !completedRevision && input.UseCurrentStorageDefaults;
            var reviewedStorage = localFilesStorage ?? (useCurrentPublicationStorage ? CaptureLocalFilesStorage(row) : null);
            if (deliveryOnly && (!stageRevision && !completedRevision && !AcquisitionPublicationStorageWait.CanRetryDelivery(run) ||
                !stageRevision && completedRevision && input.DeliveryContentLayout == null || input.CompositeInputs != null ||
                input.EnableAdaptiveAssessment != null || input.MaxAdaptiveIterations != null || input.AdaptiveContext != null ||
                input.Items.Any(item => item.Plan.Requirement != "notRequired" || item.Plan.Steps is not {Count: 0} ||
                    !stageRevision && (item.ContinueFromArtifactId == null || item.ArtifactEvidenceIds.Count != 1))))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
            if (input.DeliveryRootDirectory != null)
            {
                ValidateDirectory(input.DeliveryRootDirectory, allowEmpty: false);
                if (guardDirectory != null) await guardDirectory(input.DeliveryRootDirectory);
            }
            if (input.DeliveryContentLayout != null && !completedRevision || input.DeliveryContentLayout == null &&
                input.Items.Any(i => i.ExpectedDeliveryProjectionFingerprint != null)) throw new ArgumentException("PLAN_REPLAN_INVALID");
            var plan = Plan(row) with {PublishRetainedFiles = false};
            // Adaptive assessment is a local continuation option. It must never be
            // applied to a revision that could recreate a download or transfer: every
            // part is required to retain a server-verified artifact below. The old run
            // and its frozen plan remain unchanged; only the new local revision opts in.
            if ((input.EnableAdaptiveAssessment != null || input.MaxAdaptiveIterations != null) &&
                (!input.Items.Select(i => i.PartId).ToHashSet(StringComparer.Ordinal)
                     .SetEquals(plan.Parts.Select(p => p.Id)) ||
                 input.Items.Any(i => i.ContinueFromArtifactId == null && i.InputMode != "stageDirectory")))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
            if (input.EnableAdaptiveAssessment is { } adaptive)
                plan = plan with {AdaptiveFileProcessing = adaptive,
                    AdaptiveFileProcessingContext = adaptive ? plan.AdaptiveFileProcessingContext : null};
            if (input.MaxAdaptiveIterations is { } maxIterations)
                plan = plan with {AdaptiveFileProcessingMaxIterations = maxIterations};
            if (input.EnableAdaptiveAssessment != false && input.AdaptiveContext is { } adaptiveContext)
                plan = plan with {AdaptiveFileProcessingContext = string.IsNullOrWhiteSpace(adaptiveContext) ? null : adaptiveContext.Trim()};
            if (run.Status == WorkflowRunStatus.Waiting &&
                (input.Items.Any(item => item.ContinueFromArtifactId == null && item.InputMode != "stageDirectory") ||
                 !input.Items.Select(item => item.PartId).ToHashSet().SetEquals(plan.Parts.Select(part => part.Id))))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
            (plan, input) = await PrepareCompositeInputsAsync(row, plan, input, guardDirectory, ct);
            input = await PrepareCurrentDirectoryInputsAsync(row, plan, input, guardDirectory, ct);
            var currentDirectoryRevision = input.Items.Any(i => i.InputMode == "currentDirectory");
            if (currentDirectoryRevision)
            {
                // A new current-directory attempt uses today's default layout. Frozen old
                // executions retain their null layout; an explicit archive-directory choice survives.
                plan = plan with {ExtractionLayout = plan.ExtractionLayout ?? FileExtractionLayouts.CurrentDirectory};
                plan = PrepareCurrentDirectoryStorage(row, plan);
                if (guardDirectory != null) await guardDirectory(plan.StoragePolicy?.ProcessingWorkspaceDirectory);
            }
            var revisePlacement = completedRevision || input.UseCurrentStorageDefaults || input.DeliveryRootDirectory != null ||
                                  deliveryOnly;
            if (revisePlacement)
                ValidateLocalContinuationRevision(plan, input, requireProcessingChange: completedRevision);
            // A review-only revision carries an exact immutable artifact reference, not
            // a second full manifest in the plan history/client detail. It performs the
            // same complete content and ownership checks below; no files are truncated.
            var referenceOnlyReview = completedRevision && input.Items.All(item =>
                item.Plan.Requirement == "unknown" && item.Plan.Steps.Count == 0 &&
                item.ContinueFromArtifactId != null && item.ArtifactEvidenceIds.Count == 1 &&
                item.ArtifactEvidenceIds[0] == item.ContinueFromArtifactId);
            // The local recovery input is the registered artifact; its frozen historical
            // download path need not remain mounted. Every write uses reviewed local paths.
            ValidateDirectory(reviewedStorage?.Policy.ProcessingWorkspaceDirectory ??
                (currentDirectoryRevision && !string.IsNullOrWhiteSpace(plan.StoragePolicy?.ProcessingWorkspaceDirectory)
                    ? plan.StoragePolicy.ProcessingWorkspaceDirectory : plan.OutputDirectory), allowEmpty: false);
            var evidence = new List<AcquisitionPlanArtifactEvidence>();
            var evidenceRuns = new Dictionary<(string PartId, string ArtifactId), int>();
            var continuations = new List<(string PartId, AcquisitionPlanArtifactInput Input)>();
            var layoutChanged = false;
            foreach (var item in input.Items)
            {
                var part = plan.Parts.SingleOrDefault(p => p.Id == item.PartId)
                    ?? throw new ArgumentException("PLAN_REPLAN_INVALID");
                // Editing processing instructions cannot turn a local continuation back
                // into a download. Older clients omit the optional input field; a newer
                // client may send the same artifact explicitly. Both preserve the frozen
                // owner/run/part identity, including a source from an earlier revision.
                var retainedInput = part.ContinueFrom is { } existing &&
                    (item.ContinueFromArtifactId == null || item.ContinueFromArtifactId == existing.ArtifactId)
                        ? existing : null;
                if (retainedInput != null)
                {
                    await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, item.PartId, retainedInput, ct);
                    // Keep the established chain through the most recent run when it
                    // actually borrowed this input. A failure before that node has no
                    // such binding and must keep the earlier verified provenance.
                    if (await HasArtifactBindingAsync(input.ExpectedRunId, item.PartId, retainedInput.OperationId, ct))
                    {
                        retainedInput = retainedInput with {PreviousRunId = input.ExpectedRunId};
                        await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, item.PartId, retainedInput, ct);
                    }
                }
                try { FileProcessingPlanValidator.Validate(item.Plan); }
                catch (InvalidOperationException) { throw new ArgumentException("PLAN_REPLAN_INVALID"); }
                var artifactIds = retainedInput == null ? item.ArtifactEvidenceIds :
                    item.ArtifactEvidenceIds.Append(retainedInput.ArtifactId).Distinct().ToArray();
                foreach (var artifactId in artifactIds)
                {
                    var evidenceRun = retainedInput?.ArtifactId == artifactId ? retainedInput.PreviousRunId : input.ExpectedRunId;
                    var artifact = await artifacts.GetArtifactAsync(artifactId, ct);
                    if (artifact == null || !await HasArtifactBindingAsync(evidenceRun, item.PartId, artifact.OperationId, ct))
                        throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
                    if (completedRevision && artifact.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCompositeFiles.Kind or AcquisitionCurrentDirectoryFiles.Kind))
                        throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
                    if (input.DeliveryContentLayout != null)
                    {
                        if (artifact.Kind != "publishedFiles" || !await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
                            .AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == input.ExpectedRunId.ToString() &&
                                b.OperationId == artifact.OperationId && b.Slot == item.PartId + ":artifact.publish", ct))
                            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
                        var preview = FileArtifactDeliveryLayout.Preview(artifact, input.DeliveryContentLayout);
                        if (preview.ProjectionFingerprint != item.ExpectedDeliveryProjectionFingerprint)
                            throw new InvalidOperationException("DELIVERY_LAYOUT_PREVIEW_CHANGED");
                        layoutChanged |= preview.Changed;
                    }
                    storage.EnsurePathAllowed(artifact.RootDirectory);
                    if (guardDirectory != null) await guardDirectory(artifact.RootDirectory);
                    foreach (var file in artifact.Files) storage.EnsurePathAllowed(file);
                    if (artifact.State != ArtifactLocationState.Available || artifact.Manifest.Entries.Count == 0 ||
                        (!referenceOnlyReview && input.DeliveryContentLayout == null && artifact.Manifest.Entries.Count > 10000) ||
                        !(item.InputMode == "currentDirectory" && artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind) &&
                        !(await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)).Available)
                        throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                    evidence.Add(new(item.PartId, artifact.Id, artifact.OperationId, artifact.Kind, artifact.RootDirectory,
                        artifact.Fingerprint, artifact.Manifest));
                    evidenceRuns.Add((item.PartId, artifact.Id), evidenceRun);
                }
                AcquisitionPlanArtifactInput? continuation = retainedInput;
                if (continuation == null && item.ContinueFromArtifactId is { } continuedId)
                {
                    var recorded = evidence.Single(e => e.PartId == item.PartId && e.ArtifactId == continuedId);
                    continuation = new(row.Id, input.ExpectedRunId, recorded.ArtifactId, recorded.OperationId, recorded.Fingerprint);
                    await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, item.PartId, continuation, ct);
                }
                if (continuation != null) continuations.Add((item.PartId, continuation));
                plan = plan with {Parts = plan.Parts.Select(p => p.Id == item.PartId ? p with
                    {Processing = item.Plan, AppliedProcessingRevisionId = null, ContinueFrom = continuation,
                        UseAdaptiveAssessment = item.UseAdaptiveAssessment,
                        DeliveryProjectionFingerprint = item.ExpectedDeliveryProjectionFingerprint} : p).ToList()};
            }
            if (input.DeliveryContentLayout != null && !layoutChanged && !deliveryOnly)
                throw new ArgumentException("DELIVERY_LAYOUT_UNCHANGED");
            if (revisePlacement) plan = await PrepareLocalContinuationDeliveryAsync(row, plan, input.ExpectedRunId,
                input.UseCurrentStorageDefaults, input.DeliveryContentLayout, guardDirectory, ct, reviewedStorage,
                input.DeliveryRootDirectory, preservePreviousDelivery: deliveryOnly || input.DeliveryRootDirectory != null || ExplicitlyKeepAll(input));
            if (input.DeliveryRootDirectory != null && State(row).DeliveryLayout is { } destination &&
                evidence.Any(artifact => Overlap(artifact.RootDirectory, destination.TaskDirectory)))
                throw new InvalidOperationException("PLAN_REPLAN_DELIVERY_CONFLICT");
            plan = plan with {PublishRetainedFiles = input.DeliveryContentLayout != null || deliveryOnly};
            // Hashing may take minutes; do not hold a SQLite write transaction while reading files.
            // Recheck every durable identity after hashing, inside the short compare-and-swap transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var current = await Tasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            AcquisitionTaskGuard.EnsureNotDeleted(current);
            if (reviewedStorage != null && AcquisitionLocalFilesRecoveryReceipt.Hash(CaptureLocalFilesStorage(current)) !=
                AcquisitionLocalFilesRecoveryReceipt.Hash(reviewedStorage))
                throw new InvalidOperationException("RECOVERY_STORAGE_SETTINGS_CHANGED");
            Revision(current, input.ExpectedRevision);
            if (current.WorkflowRunId != input.ExpectedRunId) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
            var currentRun = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == input.ExpectedRunId, ct);
            if (current.Status != previousStatus || currentRun.Status != previousRunStatus ||
                !Allowed(current, currentRun) || currentRun.PendingSignalJson != run.PendingSignalJson ||
                currentRun.WaitPromptJson != run.WaitPromptJson || currentRun.CurrentStepIndex != run.CurrentStepIndex)
                throw new InvalidOperationException("PLAN_REPLAN_NOT_STOPPED");
            foreach (var item in evidence)
            {
                var currentArtifact = await artifacts.GetArtifactAsync(item.ArtifactId, ct);
                if (currentArtifact?.State != ArtifactLocationState.Available || currentArtifact.OperationId != item.OperationId ||
                    currentArtifact.Kind != item.Kind ||
                    currentArtifact.Fingerprint != item.Fingerprint || currentArtifact.RootDirectory != item.RootDirectory ||
                    currentArtifact.Manifest.Fingerprint != item.Manifest.Fingerprint ||
                    !currentArtifact.Manifest.Entries.SequenceEqual(item.Manifest.Entries) ||
                    !(currentArtifact.Manifest.Directories ?? []).SequenceEqual(item.Manifest.Directories ?? []) ||
                    !await HasArtifactBindingAsync(evidenceRuns[(item.PartId, item.ArtifactId)], item.PartId, item.OperationId, ct))
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                if (input.DeliveryContentLayout != null && FileArtifactDeliveryLayout.Preview(currentArtifact!, input.DeliveryContentLayout)
                    .ProjectionFingerprint != input.Items.Single(i => i.PartId == item.PartId).ExpectedDeliveryProjectionFingerprint)
                    throw new InvalidOperationException("DELIVERY_LAYOUT_PREVIEW_CHANGED");
            }
            foreach (var continuation in continuations)
                await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, continuation.PartId, continuation.Input, ct,
                    publishedOutputOnly: input.DeliveryContentLayout != null, publicationInputOnly: deliveryOnly && !completedRevision);
            try { SavePlan(row, plan, Sources(row), input.Reason.Trim(), input.ExpectedRunId, referenceOnlyReview || input.DeliveryContentLayout != null ? null : evidence,
                preserveStorageOverrides: !input.UseCurrentStorageDefaults,
                currentDirectoryBindings: currentDirectoryRevision ? await CurrentStageDirectoriesAsync(row, Plan(row), ct) : null); }
            catch (ArgumentException) { throw new ArgumentException("PLAN_REPLAN_INVALID"); }
            var revisedState = State(row);
            row.BuilderStateJson = Serialize(revisedState with {Issues = [], LocalFilesRecovery = localFilesStorage == null
                ? revisedState.LocalFilesRecovery : revisedState.LocalFilesRecovery! with {ResultingRevision = row.PlanRevision}});
            // Keep the old run, attempts, outputs and frozen snapshot. Only the task's current association changes.
            // The transaction plus predicates fences cross-scope retry; the shared gate also covers the legacy retry API.
            var changed = await Tasks.Where(t => t.Id == id && t.DeletedAt == null && t.PlanRevision == input.ExpectedRevision &&
                t.WorkflowRunId == input.ExpectedRunId && t.Status == previousStatus &&
                db.Set<WorkflowRunDbModel>().Any(r => r.Id == input.ExpectedRunId &&
                    r.Status == previousRunStatus))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.PlanRevision, row.PlanRevision).SetProperty(t => t.PlanJson, row.PlanJson)
                    .SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson).SetProperty(t => t.BuilderStateJson, row.BuilderStateJson)
                    .SetProperty(t => t.WorkflowRunId, (int?)null).SetProperty(t => t.Status, AcquisitionStatus.Waiting)
                    .SetProperty(t => t.Phase, "ready").SetProperty(t => t.Error, (string?)null)
                    .SetProperty(t => t.WaitReason, (AcquisitionWaitReason?)null).SetProperty(t => t.CompletedAt, (DateTime?)null)
                    .SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
            if (changed != 1) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
            var tracked = db.ChangeTracker.Entries<AcquisitionTaskDbModel>().FirstOrDefault(e => e.Entity.Id == id);
            if (tracked != null) tracked.State = EntityState.Detached;
            await transaction.CommitAsync(ct);
            if (completedRevision && State(row).PendingDeliveryRetirements?.Any(p => p.Revision == row.PlanRevision) == true)
                deliveryRetirements?.Enqueue(row.Id, row.PlanRevision);
            return (await GetAsync(id, ct))!;
        }
        finally {if (replanRunGateHeld) replanRunGate.Release(); if (localFilesStorage == null) gate.Release();}
    }

    private static bool Replannable(WorkflowRunStatus status) => status is
        WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled;

    private static bool CanReplan(AcquisitionStatus task, WorkflowRunStatus run) =>
        task == AcquisitionStatus.Completed ? run == WorkflowRunStatus.Success :
        task is AcquisitionStatus.Failed or AcquisitionStatus.Cancelled && Replannable(run);

    private static bool CanReplan(AcquisitionStatus task, WorkflowRunDbModel run) =>
        CanReplan(task, run.Status) || task == AcquisitionStatus.Waiting &&
        (AcquisitionProcessingConflictWait.MatchesForLocalContinuation(run) || AcquisitionProcessingConflictWait.MatchesCurrentDirectoryWait(run) || AcquisitionPublicationStorageWait.Matches(run));

    private async Task<bool> HasArtifactBindingAsync(int runId, string partId, string operationId, CancellationToken ct)
    {
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var download = partId + ":artifact.download"; var processing = partId + ":process:"; var continuation = partId + ":artifact.continuation";
        if (await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.OperationId == operationId && (b.Slot == download || b.Slot == continuation || b.Slot == partId + ":artifact.publish" ||
                b.Slot.StartsWith(processing) || b.Slot.StartsWith(partId + ":adaptive:output:")), ct)) return true;
        if (artifacts == null || await artifacts.GetOperationAsync(operationId, ct) is not {OutputArtifactId: { } artifactId} ||
            await artifacts.GetArtifactAsync(artifactId, ct) is not { } artifact ||
            artifact.Kind is not (AcquisitionCompositeFiles.Kind or AcquisitionCurrentDirectoryFiles.Kind)) return false;
        var owners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(r => r.WorkflowRunId == runId &&
            r.Module == "acquisition" && r.EntityKind == "task").Select(r => r.EntityId).Distinct().ToArrayAsync(ct);
        return owners.Length == 1 && int.TryParse(owners[0], out var taskId) &&
            (artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind
                ? await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, artifacts, artifact, taskId, runId, partId, ct)
                : await AcquisitionCompositeFiles.IsOwnedAsync(db, artifacts, artifact, taskId, runId, partId, ct));
    }

    private async Task<IReadOnlyList<AcquisitionPlanArtifactCandidate>> ReplanArtifactsAsync(AcquisitionTask task, AcquisitionPlan? plan, CancellationToken ct)
    {
        if (artifacts == null || plan == null || task.WorkflowRunId is not { } runId ||
            task.Status is not (AcquisitionStatus.Failed or AcquisitionStatus.Cancelled or AcquisitionStatus.Completed or AcquisitionStatus.Waiting)) return [];
        var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null || !CanReplan(task.Status, run)) return [];
        var publicationStorageWait = AcquisitionPublicationStorageWait.Matches(run);
        var stoppedPublication = AcquisitionPublicationStorageWait.CanRetryDelivery(run);
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner).ToListAsync(ct);
        var publicationBindings = bindings.Where(b => b.Slot.EndsWith(":artifact.publish", StringComparison.Ordinal))
            .Select(b => (b.Slot, b.OperationId)).ToHashSet();
        var candidates = new List<AcquisitionPlanArtifactCandidate>();
        foreach (var part in plan.Parts)
        foreach (var operationId in bindings.Where(b => b.Slot == part.Id + ":artifact.download" || b.Slot == part.Id + ":artifact.continuation" ||
                     b.Slot == part.Id + ":artifact.publish" ||
                     b.Slot.StartsWith(part.Id + ":process:", StringComparison.Ordinal) ||
                     b.Slot.StartsWith(part.Id + ":adaptive:output:", StringComparison.Ordinal)).Select(b => b.OperationId).Distinct().Take(100))
        {
            var operation = await artifacts.GetOperationAsync(operationId, ct);
            if (operation?.OutputArtifactId is not { } artifactId) continue;
            var artifact = await artifacts.GetArtifactAsync(artifactId, ct);
            if (artifact == null) continue;
            var canReadCurrent = await AcquisitionCurrentDirectoryFiles.CanReadAsync(db, artifacts, storage, workspaces,
                task.Id, runId, part.Id, artifact, ct);
            if (artifact.State != ArtifactLocationState.Available || publicationStorageWait && artifact.Kind is not ("processedFiles" or "publishedFiles"))
            {
                if (canReadCurrent) candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory,
                    artifact.Fingerprint, artifact.Files.Count, CanReadCurrentDirectory: true));
                continue;
            }
            if (task.Status == AcquisitionStatus.Completed || artifact.Kind == "publishedFiles" || publicationStorageWait ||
                run.Status == WorkflowRunStatus.Waiting)
            {
                if ((task.Status == AcquisitionStatus.Completed || publicationStorageWait) &&
                    artifact.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind))
                {
                    if (canReadCurrent) candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory,
                        artifact.Fingerprint, artifact.Files.Count, CanReadCurrentDirectory: true));
                    continue;
                }
                try { await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, part.Id,
                    new(task.Id, runId, artifact.Id, artifact.OperationId, artifact.Fingerprint), ct); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
                {
                    if (canReadCurrent) candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory,
                        artifact.Fingerprint, artifact.Files.Count, CanReadCurrentDirectory: true));
                    continue;
                }
            }
            // A borrowed publication is valid processing evidence; only this part's published output can be republished.
            var canRepublish = task.Status == AcquisitionStatus.Completed && artifact.Kind == "publishedFiles" &&
                               publicationBindings.Contains((part.Id + ":artifact.publish", artifact.OperationId));
            var canRetryDelivery = stoppedPublication && publicationStorageWait;
            if (stoppedPublication && !publicationStorageWait && artifact.Kind is "processedFiles" or "publishedFiles")
            {
                try
                {
                    await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, part.Id,
                        new(task.Id, runId, artifact.Id, artifact.OperationId, artifact.Fingerprint), ct, publicationInputOnly: true);
                    canRetryDelivery = true;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException) { }
            }
            candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory, artifact.Fingerprint,
                artifact.Files.Count, canRepublish, canRetryDelivery, canReadCurrent));
        }
        // A run can fail before its continuation node binds the borrowed input. Its
        // saved earlier-run provenance remains reviewable without inventing a binding
        // on the failed run or requiring a new download.
        foreach (var part in plan.Parts.Where(p => p.ContinueFrom != null && !publicationStorageWait))
        {
            if (candidates.Any(c => c.PartId == part.Id && c.ArtifactId == part.ContinueFrom!.ArtifactId)) continue;
            try
            {
                var artifact = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, part.Id, part.ContinueFrom!, ct);
                candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory, artifact.Fingerprint, artifact.Files.Count,
                    CanReadCurrentDirectory: await AcquisitionCurrentDirectoryFiles.CanReadAsync(db, artifacts, storage, workspaces,
                        task.Id, part.ContinueFrom!.PreviousRunId, part.Id, artifact, ct, activeRunId: runId)));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                var artifact = await artifacts.GetArtifactAsync(part.ContinueFrom!.ArtifactId, ct);
                if (artifact != null && await AcquisitionCurrentDirectoryFiles.CanReadAsync(db, artifacts, storage, workspaces,
                        task.Id, part.ContinueFrom.PreviousRunId, part.Id, artifact, ct, activeRunId: runId))
                    candidates.Add(new(part.Id, artifact.Id, artifact.Kind, artifact.RootDirectory, artifact.Fingerprint,
                        artifact.Files.Count, CanReadCurrentDirectory: true));
            }
        }
        return candidates;
    }

    public async Task<AcquisitionPlanTaskDetail> RunAsync(int id, int expectedRevision, CancellationToken ct,
        bool? useEmergencySpaceReserve = null)
        => await RunGuardedAsync(id, expectedRevision, ct, false, useEmergencySpaceReserve: useEmergencySpaceReserve);

    // Recovery already owns the acquisition gate. Keep the same execution/preflight path
    // without recursively taking the semaphore or manufacturing a replacement workflow.
    internal Task<AcquisitionPlanTaskDetail> RunWhileLockedAsync(int id, int expectedRevision, string? expectedInputs, CancellationToken ct,
        bool? useEmergencySpaceReserve = null)
        => RunGuardedAsync(id, expectedRevision, ct, true, expectedInputs, useEmergencySpaceReserve);

    internal async Task<(string? Problem, string? Inputs)> CheckUnstartedRecoveryAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (row.DeletedAt != null || row.Status != AcquisitionStatus.Waiting || row.PlanJson == null || row.BuilderId == null || !await HasNeverExecutedAsync(row, ct))
            return ("RECOVERY_CHECKPOINT_UNSUPPORTED", null);
        var plan = Plan(row);
        AcquisitionPlanCompiler.Validate(plan);
        await ValidateContinuationInputsAsync(row.Id, plan, ct);
        var dependencyIssues = await FileProcessingDependencyPreflight.InspectAsync(plan.Parts.Select(AcquisitionPlanCompiler.Processing),
            processingDependencies ?? [], ct);
        if (dependencyIssues.Count > 0) return (dependencyIssues[0].Code, null);
        // Inspect the already-selected inputs. Do not choose another mirror, install a tool,
        // start an account flow or alter the retained plan during the recovery preview.
        var check = await PreflightDownloadsAsync(Request(plan, $"acquisition:{row.Id}"), plan.OutputDirectory, ct);
        return check.CanSubmit ? (null, RecoveryInputs(plan, check)) :
            (check.Requirements.Concat(check.Candidates.SelectMany(c => c.Requirements))
                .Select(r => r.Code).FirstOrDefault() ?? "RECOVERY_PREPARED_PLAN_BLOCKED", null);
    }

    private static string RecoveryInputs(AcquisitionPlan plan, DownloadIntentPreflight check) => CloudOperationService.Key(
        plan.OutputDirectory, Serialize(check.Candidates.Where(c => plan.Parts.Any(p => p.SelectedSourceId == c.Candidate.Id))
            .OrderBy(c => c.Candidate.Id).Select(c => c.ResolvedSource).ToArray()));

    private async Task<AcquisitionPlanTaskDetail> RunGuardedAsync(int id, int expectedRevision, CancellationToken ct, bool gateHeld,
        string? expectedInputs = null, bool? useEmergencySpaceReserve = null)
    {
        var gate = AcquisitionTaskGate.For(id); if (!gateHeld) await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, expectedRevision);
            if (State(row).DirectoryChange != null) throw new InvalidOperationException("STAGE_DIRECTORY_CHANGE_PENDING");
            var plan = Plan(row); AcquisitionPlanCompiler.Validate(plan);
            await ValidateContinuationInputsAsync(row.Id, plan, ct, verifyContents: true);
            var dependencyIssues = await FileProcessingDependencyPreflight.InspectAsync(plan.Parts.Select(AcquisitionPlanCompiler.Processing),
                processingDependencies ?? [], ct);
            if (dependencyIssues.Count > 0)
            {
                row.BuilderStateJson = Serialize(State(row) with {Issues = dependencyIssues
                    .Select(i => new AcquisitionPlanIssue(i.Code, i.Message)).ToList()});
                row.Phase = "attention"; row.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
                return await DetailAsync(row, ct);
            }
            var ownerId = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var revision = row.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var existingRunId = await (from link in db.Set<WorkflowRunSourceDbModel>()
                join existingRun in db.Set<WorkflowRunDbModel>() on link.WorkflowRunId equals existingRun.Id
                where link.Module == "acquisition" && link.EntityKind == "task" && link.EntityId == ownerId && link.Revision == revision
                orderby existingRun.Id
                select (int?)existingRun.Id).FirstOrDefaultAsync(ct);
            if (existingRunId != null)
            {
                row.WorkflowRunId = existingRunId; row.Status = AcquisitionStatus.Running; row.Phase = "executing";
                BindRunReserve(row, useEmergencySpaceReserve);
                await db.SaveChangesAsync(ct);
                if (useEmergencySpaceReserve != null) AcquisitionStorageWritePolicyProvider.Invalidate();
                return await DetailAsync(row, ct);
            }
            // Never change a task that has any historical execution, including a lost association
            // or a processing-only replan. Transfers/downloads and artifact reuse remain frozen.
            var neverExecuted = await HasNeverExecutedAsync(row, ct);
            var canFallback = expectedInputs == null && neverExecuted && plan.Parts.Any(p => p.SelectionMode == "automatic" && p.Sources.Count > 1);
            var preflight = await PreflightDownloadsAsync(Request(plan, $"acquisition:{row.Id}", canFallback) with
                {CheckSourceAvailability = neverExecuted, IgnoreSourceAvailability = !neverExecuted}, plan.OutputDirectory, ct);
            if (canFallback)
            {
                var selected = AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(plan, preflight.Candidates);
                if (!ReferenceEquals(selected, plan))
                {
                    SavePlan(row, selected, Sources(row), "sourceSelection.preStartFallback");
                    await db.SaveChangesAsync(ct);
                    plan = selected;
                    revision = row.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    preflight = await PreflightDownloadsAsync(Request(plan, $"acquisition:{row.Id}"), plan.OutputDirectory, ct);
                }
            }
            if (!preflight.CanSubmit)
            {
                var problems = preflight.Requirements.Concat(preflight.Candidates.Where(c => plan.Parts.Any(p => p.SelectedSourceId == c.Candidate.Id))
                    .SelectMany(c => c.Requirements))
                    .Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList();
                row.BuilderStateJson = Serialize(State(row) with {Issues = problems});
                row.Phase = "attention"; row.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(ct); return await DetailAsync(row, ct);
            }
            if (expectedInputs != null && (!neverExecuted || RecoveryInputs(plan, preflight) != expectedInputs))
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            // Bind checked source/account selection into the frozen execution payload.
            var sources = Sources(row).Select(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == ownerId
                ? s with {Revision = revision} : s).ToList();
            var workset = ResolvedWorkset(row, plan, sources, preflight);
            // A processing-only revision must not silently choose a new default account, method,
            // expanded short link or directory. Compare against the actual prior download binding,
            // not the draft plan whose account/method may have been left on automatic selection.
            if (await ReplanIdentityIssueAsync(row, workset, ct) is { } identityIssue)
            {
                row.BuilderStateJson = Serialize(State(row) with {Issues = [identityIssue]});
                row.Phase = "attention"; row.UpdatedAt = DateTime.Now;
                row.UpdatedAt = DateTime.Now; await db.SaveChangesAsync(ct);
                return await DetailAsync(row, ct);
            }
            var run = await workflows.RunPlanAsync(AcquisitionPlanCompiler.Compile(plan,
                workset.Targets.Where(t => plan.Parts.Single(p => p.Id == t.Id).ContinueFrom == null)
                    .Select(t => t.SelectedSource!).ToArray()), workset, sources, ct);
            row.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Running; row.Phase = "executing";
            row.BuilderStateJson = Serialize(State(row) with {Issues = [], DownloadContinuation =
                new(run.Id, AcquisitionDownloadContinuationService.Automatic, DateTimeOffset.UtcNow)}); row.UpdatedAt = DateTime.Now;
            // The new association and its explicit reserve are published atomically. Before
            // this save, writers cannot find a current owner and use the configured reserve.
            // In particular, false must override a globally enabled emergency reserve.
            BindRunReserve(row, useEmergencySpaceReserve);
            await db.SaveChangesAsync(ct);
            if (useEmergencySpaceReserve != null) AcquisitionStorageWritePolicyProvider.Invalidate();
            return await DetailAsync(row, ct);
        }
        finally {if (!gateHeld) gate.Release();}
    }

    private static void BindRunReserve(AcquisitionTaskDbModel row, bool? useEmergencySpaceReserve)
    {
        if (useEmergencySpaceReserve is not {} emergency || row.WorkflowRunId is not {} runId) return;
        var now = DateTimeOffset.UtcNow;
        row.BuilderStateJson = AcquisitionStorageOverrideGrant.WithState(row.BuilderStateJson,
            new(runId, row.PlanRevision, "start:" + Guid.NewGuid().ToString("N"), now, now.AddHours(24),
                ReserveMode: emergency ? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Configured));
    }

    private async Task<bool> HasNeverExecutedAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (row.WorkflowRunId != null || ReadHistory(row).Any(h => h.PreviousRunId != null)) return false;
        var ownerId = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(link =>
            link.Module == "acquisition" && link.EntityKind == "task" && link.EntityId == ownerId, ct);
    }

    private static DownloadWorkset ResolvedWorkset(AcquisitionTaskDbModel row, AcquisitionPlan plan,
        IReadOnlyList<WorkflowSourceRef> sources, DownloadIntentPreflight preflight)
    {
        var workset = AcquisitionPlanCompiler.Workset(row.Id, row.PlanRevision, plan, sources);
        return workset with {Targets = workset.Targets.Select(t =>
        {
            if (plan.Parts.Single(p => p.Id == t.Id).ContinueFrom != null) return t;
            var resolved = preflight.Candidates.Single(c => c.Candidate.Id == t.SelectedSourceId).ResolvedSource!;
            return t with {Sources = t.Sources.Select(s => s.Id == resolved.Id
                ? resolved with {OriginalUrl = s.OriginalUrl ?? resolved.OriginalUrl,
                    ResolutionChain = s.ResolutionChain.Count > 0 ? s.ResolutionChain : resolved.ResolutionChain} : s).ToList(), AccountId = resolved.AccountId};
        }).ToList()};
    }

    private async Task<AcquisitionPlanIssue?> ReplanIdentityIssueAsync(AcquisitionTaskDbModel row,
        DownloadWorkset workset, CancellationToken ct)
    {
        var continued = Plan(row).Parts.Where(p => p.ContinueFrom != null).Select(p => p.Id).ToHashSet();
        workset = workset with {Targets = workset.Targets.Where(t => !continued.Contains(t.Id)).ToList()};
        if (workset.Targets.Count == 0) return null;
        var previousRevision = ReadHistory(row).LastOrDefault(r => r.PreviousRunId != null);
        // A reviewed source decision deliberately changes the download identity. Only the exact
        // selected sources recorded by that decision opt out of processing-only reuse protection.
        if (previousRevision?.Reason == AcquisitionSourceRevisionService.RevisionReason)
            return MatchesSourceRevisionExecution(Plan(row), previousRevision, workset) ? null :
                new("SOURCE_REVISION_EXECUTION_CHANGED", "The source, method, account configuration or output directory changed after approval. Starting is blocked. Re-approving this prepared revision is not supported yet; keep the task and its history.");
        var previous = previousRevision?.PreviousRunId;
        return previous is { } runId && !await HasUnchangedDownloadIdentityAsync(runId, workset, ct)
            ? new("PLAN_REPLAN_SOURCE_CHANGED", "The resolved download identity differs from the previous run. Restore the original download configuration or create another acquisition task.")
            : null;
    }

    internal static bool MatchesSourceRevisionExecution(AcquisitionPlan plan, AcquisitionPlanRevision revision, DownloadWorkset workset) =>
        revision is {Reason: AcquisitionSourceRevisionService.RevisionReason, SourceRevision.SelectedExecutionIdentity: not null} &&
        plan.Parts.Count == 1 && revision.Plan.Parts.Count == 1 && workset.Targets.Count == 1 &&
        workset.Targets[0].Id == revision.SourceRevision.PartId && workset.Targets[0].SelectedSourceId == revision.SourceRevision.SelectedSourceId &&
        Serialize(plan.Parts[0].Sources.Single(s => s.Id == plan.Parts[0].SelectedSourceId)) ==
        Serialize(revision.Plan.Parts[0].Sources.Single(s => s.Id == revision.Plan.Parts[0].SelectedSourceId)) &&
        AcquisitionSourceRevisionService.SelectedExecutionIdentity(workset.Targets[0], workset.OutputDirectory) == revision.SourceRevision.SelectedExecutionIdentity;

    private async Task ValidateContinuationInputsAsync(int taskId, AcquisitionPlan plan, CancellationToken ct, bool verifyContents = false)
    {
        foreach (var part in plan.Parts.Where(p => p.ContinueFrom != null))
        {
            if (artifacts == null) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, taskId, part.Id, part.ContinueFrom!, ct);
            if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
                await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, workspaces, ct);
            storage.EnsurePathAllowed(input.RootDirectory);
            foreach (var file in input.Files) storage.EnsurePathAllowed(file);
            if (verifyContents && !(await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest, ct)).Available)
                throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
        }
    }

    private Task<DownloadIntentPreflight> PreflightDownloadsAsync(DownloadIntentRequest request, string outputDirectory, CancellationToken ct)
    {
        if (request.SelectedCandidateIds.Count == 0) return Task.FromResult(new DownloadIntentPreflight("files", [], [], true));
        // The selected root is a storage boundary, not a task folder to recreate if its mount
        // disappears. This is read-only and precedes any account, transfer or download work.
        try
        {
            ValidateDirectory(outputDirectory, allowEmpty: false);
            AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(outputDirectory, storage);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(new DownloadIntentPreflight("files", [],
                [new("OUTPUT_DIRECTORY_REQUIRED", "configuration", error.Message, Actions: ["configure"])], false));
        }
        return downloads.PreflightAsync(request, outputDirectory, ct);
    }

    internal async Task<bool> HasUnchangedDownloadIdentityAsync(int previousRunId, DownloadWorkset workset, CancellationToken ct)
    {
        var owner = previousRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bindings = await (from binding in db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            join operation in db.Set<ArtifactOperationDbModel>().AsNoTracking() on binding.OperationId equals operation.Id
            where binding.OwnerKind == "workflow" && binding.OwnerId == owner && operation.Kind == "download"
            select new {binding.Slot, operation.IntentKey}).ToListAsync(ct);
        try
        {
            return workset.Targets.Count > 0 && workset.Targets.All(target => target.SelectedSource != null &&
                bindings.Count(b => b.Slot == target.Id + ":artifact.download") == 1 &&
                bindings.Single(b => b.Slot == target.Id + ":artifact.download").IntentKey ==
                    DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, workspaces));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return false;}
    }

    public async Task<IReadOnlyList<AcquisitionPlanSourceTasks>> BySourcesAsync(IReadOnlyList<WorkflowSourceRef> sources, CancellationToken ct)
    {
        if (sources.Count is 0 or > 100) throw new ArgumentException("Supply between one and 100 source references.");
        // Keep source summaries and their associated run times in one read. A left join preserves
        // unstarted tasks and tasks whose historical run is no longer available.
        var rows = await (from t in Tasks.AsNoTracking()
            where t.BuilderId != null && t.SourceRefsJson != null
            join run in db.Set<WorkflowRunDbModel>().AsNoTracking() on t.WorkflowRunId equals (int?) run.Id into associatedRuns
            from run in associatedRuns.DefaultIfEmpty()
            orderby t.Id descending
            select new {t.SourceRefsJson, t.BuilderStateJson, RunStatus = run == null ? (WorkflowRunStatus?) null : run.Status, Task = new AcquisitionTask
            {
                Id = t.Id, DeletedAt = t.DeletedAt, Title = t.Title, ResourceName = t.Title, BuilderId = t.BuilderId, Phase = t.Phase,
                PlanRevision = t.PlanRevision, Status = t.Status, WorkflowRunId = t.WorkflowRunId,
                CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, CompletedAt = t.CompletedAt,
                ExecutionStartedAt = run == null ? null : (DateTime?) run.StartedAt,
                ExecutionCompletedAt = run == null ? null : run.CompletedAt,
                TargetDirectory = t.TargetDirectory
            }}).ToListAsync(ct);
        foreach (var row in rows)
        {
            row.Task.OriginResourceId = AcquisitionTaskExtensions.ReadOriginResourceId(row.BuilderStateJson);
            if (row.RunStatus is { } status) row.Task.Status = status switch
            {
                WorkflowRunStatus.Pending or WorkflowRunStatus.Running => AcquisitionStatus.Running,
                WorkflowRunStatus.Waiting => AcquisitionStatus.Waiting, WorkflowRunStatus.Success => AcquisitionStatus.Completed,
                WorkflowRunStatus.Cancelled => AcquisitionStatus.Cancelled, _ => AcquisitionStatus.Failed
            };
        }
        var indexed = rows.Select(r => new {Task = r.Task.NormalizeTimestampsToUtc(), Refs = JsonSerializer.Deserialize<List<WorkflowSourceRef>>(r.SourceRefsJson!, Json)!}).ToList();
        var result = sources.Select(source => new AcquisitionPlanSourceTasks(source, indexed.Where(row => row.Refs.Any(s =>
            s.Module == source.Module && s.EntityKind == source.EntityKind && s.EntityId == source.EntityId &&
            (source.Revision == null || s.Revision == source.Revision) && (source.ResultKey == null || s.ResultKey == source.ResultKey)))
            .Select(r => r.Task).ToList())).ToList();
        if (creationOrigins != null) await creationOrigins.EnrichAsync(result.SelectMany(r => r.Tasks).DistinctBy(t => t.Id).ToArray(), ct);
        return result;
    }

    private async Task<AcquisitionPlanTaskDetail> DetailAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        var task = await acquisitions.GetAsync(row.Id, ct) ?? row.ToDomainModel();
        if (downloadActivity != null)
            task.DownloadActivity = (await downloadActivity.ReadAsync([task.Id], ct)).GetValueOrDefault(task.Id)?.Activity;
        if (task.CreationOrigins.Count == 0 && creationOrigins != null) await creationOrigins.EnrichAsync([task], ct);
        var plan = row.PlanJson == null ? null : await BindProcessingSourcesAsync(Plan(row), Sources(row), ct);
        var state = State(row);
        // A runtime answer can supersede the frozen initial instructions. Current run/wait metadata is
        // authoritative after submission; do not keep advertising the initial preparation warning.
        var issues = state.Issues.Where(i => row.WorkflowRunId == null ||
            i.Code is not ("PROCESSING_CONFIRMATION_REQUIRED" or "PROCESSING_STEPS_MISSING")).ToList();
        if (plan == null && state.Candidates.Count > 0) issues.Add(new("SOURCE_SELECTION_REQUIRED", "Choose the intended resource from the candidates."));
        if (string.IsNullOrWhiteSpace(plan?.OutputDirectory ?? row.TargetDirectory)) issues.Add(new("OUTPUT_DIRECTORY_REQUIRED", "Choose the directory for the acquired files."));
        if (row.WorkflowRunId == null && plan?.Parts.Any(p => p.Processing == null || p.Processing.Requirement == "unknown") == true)
            issues.Add(new("PROCESSING_CONFIRMATION_REQUIRED", "Processing instructions require confirmation before final delivery.", false));
        if (row.WorkflowRunId == null && plan?.Parts.Any(p => p.Processing?.Requirement == "required" && p.Processing.Steps.Count == 0) == true)
            issues.Add(new("PROCESSING_STEPS_MISSING", "File processing is required, but the plan contains no instructions.", false));
        var steps = plan == null ? new List<AcquisitionPlanStepView>() : AcquisitionPlanCompiler.Compile(plan).Nodes.Select(n =>
        {
            if (n.Kind != AcquisitionPlanProcessingActivity.ActivityKind) return new AcquisitionPlanStepView(n.NodeId, n.Kind, n.Kind switch
            {
                "download.selectSources" => "cloudDownload.activity.download.selectSources.name",
                "baidu.transfer" => "cloudDownload.activity.baidu.transfer.name",
                "download.enqueueSupported" => "cloudDownload.activity.download.enqueueSupported.name",
                "download.awaitCompletion" => "cloudDownload.activity.download.awaitCompletion.name",
                AcquisitionPlanContinueFilesActivity.ActivityKind => "acquisition.plan.continueFiles",
                "fs.publishArtifacts" => "workflow.activity.fsPublishArtifacts.name",
                _ => n.Kind
            }, TargetDirectory: n.Kind == "fs.publishArtifacts" ? plan.DeliveryLayout?.TaskDirectory : null);
            var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(n.ConfigJson, Json)!;
            var step = config.Plan.Steps.SingleOrDefault(s => s.Id == config.StopAfterStepId);
            return new AcquisitionPlanStepView(n.NodeId, n.Kind, "acquisition.plan.processing." + (step?.Op ?? "confirm"),
                step?.Op, step?.Input, step?.Selector, step?.TargetName, step?.TargetDirectory, step?.Extension, step?.Inputs,
                step?.FindText, step?.ReplaceText, step?.SourceDirectory);
        }).ToList();
        if (steps.Count > 1 && AcquisitionStageHistory.LocalInputStage(steps[0].Kind, steps[1].Kind) is {} localStage)
            steps[0] = steps[0] with { Stage = localStage };
        // Stage locations describe the current task, while attempt manifests remain historical.
        // Reading old tasks binds their recorded locations without moving or adopting files.
        return new(task, plan, state.Candidates, state.Query, issues.DistinctBy(i => i.Code).ToList(), steps, Sources(row))
        {StageAttempts = await AcquisitionStageHistory.ReadAsync(db, row.Id, row.WorkflowRunId, ct),
            StageDirectories = plan == null ? null : await CurrentStageDirectoriesAsync(row, plan, ct),
            DeliverySettings = plan?.DeliveryLayout?.Settings ?? state.DeliverySettings,
            StoragePolicy = plan?.StoragePolicy ?? state.StoragePolicy,
            CanEditStoragePolicy = row.DeletedAt == null && row.Status != AcquisitionStatus.Cancelled && await HasNeverExecutedAsync(row, ct),
            CanEditDeliverySettings = row.DeletedAt == null && row.Status != AcquisitionStatus.Cancelled && await HasNeverExecutedAsync(row, ct),
            DraftSources = state.DraftSources, LegacyLeads = state.LegacyLeads, LegacyTitle = state.LegacyTitle, ImportedListRow = state.ImportedListRow,
            PreparationAttempts = state.Attempts, ReplanArtifacts = await ReplanArtifactsAsync(task, plan, ct), PlanHistory = row.PlanHistoryJson == null ? [] :
            ReadHistory(row).Select(r => r.CreatedAt.Kind == DateTimeKind.Local ? r with {CreatedAt = r.CreatedAt.ToUniversalTime()} : r).ToList()};
    }
    private async Task<AcquisitionPlan> BindProcessingSourcesAsync(AcquisitionPlan plan,
        IReadOnlyList<WorkflowSourceRef> refs, CancellationToken ct)
    {
        if (processingPlans == null) return plan;
        var parts = new List<AcquisitionPlanPart>();
        foreach (var part in plan.Parts)
        {
            var bound = part;
            if (part.ProcessingSource == null)
            {
                var candidates = refs.Where(r => r.Revision != null && r.ResultKey != null &&
                    r.ResultKey == part.SelectedSourceId).Distinct().ToList();
                if (candidates.Count == 1)
                {
                    try
                    {
                        var original = await processingPlans.ReadSourceAsync(candidates[0], ct);
                        // Legacy plans never stored the binding. A unique saved reference plus exact locator
                        // equality establishes it; an ambiguous or rewritten locator remains unbound.
                        if (original.SourceLocator != null && part.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId)?.Url == original.SourceLocator)
                            bound = part with {ProcessingSource = candidates[0]};
                    }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
                }
            }
            parts.Add(bound);
        }
        return plan with {Parts = parts};
    }

    private async Task<AcquisitionTaskDbModel> Editable(int id, CancellationToken ct)
    {
        var row = await Tasks.SingleOrDefaultAsync(t => t.Id == id && t.BuilderId != null, ct)
            ?? throw new InvalidOperationException("This acquisition plan does not exist.");
        await db.Entry(row).ReloadAsync(ct);
        AcquisitionTaskGuard.EnsureNotDeleted(row);
        if (row.WorkflowRunId != null || row.Status == AcquisitionStatus.Cancelled)
            throw new InvalidOperationException("PLAN_ALREADY_STARTED: Resume the recorded execution rather than changing its plan.");
        return row;
    }
    private async Task UpdateDeliverySettingsAsync(AcquisitionTaskDbModel row, AcquisitionDeliverySettings? input, CancellationToken ct)
    {
        if (input == null) return;
        if (!await HasNeverExecutedAsync(row, ct))
            throw new InvalidOperationException("DELIVERY_ALREADY_STARTED: A recorded execution keeps its delivery directories.");
        row.BuilderStateJson = Serialize(State(row) with {DeliverySettings = DeliveryDirectories.Capture(input), DeliveryLayout = null});
    }
    private static void Revision(AcquisitionTaskDbModel row, int expected)
    {if (row.PlanRevision != expected) throw new InvalidOperationException("PLAN_VERSION_CONFLICT: Reload the acquisition plan before changing it.");}
    private static string Title(string title) => !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 512 ? title.Trim() :
        throw new ArgumentException("Enter a resource name of at most 512 characters.");
    private void ValidateDirectory(string? directory, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(directory) && allowEmpty) return;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute output directory.");
        storage.EnsurePathAllowed(directory);
    }
    private void SavePlan(AcquisitionTaskDbModel row, AcquisitionPlan plan, IReadOnlyList<WorkflowSourceRef> sources,
        string? reason = null, int? previousRunId = null, IReadOnlyList<AcquisitionPlanArtifactEvidence>? evidence = null,
        bool preserveStorageOverrides = true, bool refreshStageDirectories = false,
        AcquisitionStageDirectories? currentDirectoryBindings = null)
    {
        var state = State(row);
        var history = ReadHistory(row);
        // A search/draft edit may increment the revision before the first actual plan exists.
        // Once prepared, recompiling or reviewing it keeps its original (including legacy null) default.
        if (plan.ExtractionLayout == null)
            plan = plan with {ExtractionLayout = row.PlanJson != null ? Plan(row).ExtractionLayout :
                history.Count > 0 ? history[^1].Plan.ExtractionLayout : FileExtractionLayouts.CurrentDirectory};
        if (preserveStorageOverrides) plan = StoredStorageOverrides(plan, row.PlanJson == null ? null : Plan(row));
        // Borrowing does not change the user's frozen retention preference. Pending plans,
        // active owners and consumers protect the input until a verified successful
        // continuation safely retires its superseded owners. A permanent synthetic keep
        // here would prevent the original cleanup policy from ever taking effect.
        // Only a saved settings snapshot opts a plan into the new layout. Historical null stays null.
        // Client-supplied mappings are never trusted; previously resolved folders survive title edits/re-preparation.
        var layout = state.DeliverySettings == null ? null : DeliveryDirectories.Resolve(row.Id, plan.Title,
            plan.OutputDirectory, plan.Parts, state.DeliverySettings, state.DeliveryLayout);
        plan = plan with {DeliveryLayout = layout, StoragePolicy = state.StoragePolicy};
        // Producers cannot claim directory ownership. New task storage is allocated here,
        // after the server has assigned an id; existing tasks keep their recorded locations.
        var recordedDirectories = currentDirectoryBindings ??
            (row.PlanJson != null ? Plan(row).StageDirectories : history.LastOrDefault()?.Plan.StageDirectories);
        if (recordedDirectories != null)
        {
            var currentParts = plan.Parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            recordedDirectories = recordedDirectories with {Parts = recordedDirectories.Parts
                .Where(p => currentParts.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)};
        }
        if (recordedDirectories != null && refreshStageDirectories)
            recordedDirectories = PreserveBoundDirectories(recordedDirectories, AcquisitionStageDirectoryResolver.Allocate(row.Id, plan, DeliveryDirectories));
        else if (recordedDirectories != null && previousRunId != null)
        {
            var nextProcessing = (!preserveStorageOverrides || currentDirectoryBindings != null) &&
                recordedDirectories.Parts.Values.Any(p => p.Processing?.BindingId == null)
                ? AcquisitionStageDirectoryResolver.AllocateProcessingOnly(row.Id, plan, DeliveryDirectories) : null;
            recordedDirectories = recordedDirectories with {Parts = recordedDirectories.Parts.ToDictionary(p => p.Key,
                p => p.Value with {Processing = p.Value.Processing?.BindingId != null ? p.Value.Processing : nextProcessing?.GetValueOrDefault(p.Key) ?? p.Value.Processing,
                    Delivery = p.Value.Delivery?.BindingId != null ? p.Value.Delivery : layout?.PartDirectories.GetValueOrDefault(p.Key) is { } destination
                        ? p.Value.Delivery is { } previous && string.Equals(
                            Path.TrimEndingDirectorySeparator(Path.GetFullPath(previous.Directory)),
                            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase)
                            ? previous : new(destination, "exclusive", "taskLayout")
                        : p.Value.Delivery}, StringComparer.Ordinal)};
        }
        plan = plan with {StageDirectories = recordedDirectories ??
            (row.PlanJson == null && history.Count == 0 && Path.IsPathFullyQualified(plan.OutputDirectory)
                ? AcquisitionStageDirectoryResolver.Allocate(row.Id, plan, DeliveryDirectories) : null)};
        ValidateStoragePaths(plan);
        _ = AcquisitionPlanCompiler.Compile(plan);
        row.BuilderStateJson = Serialize(state with {DeliveryLayout = layout});
        if (history.Count >= 100) throw new InvalidOperationException("This plan has reached its revision limit.");
        row.PlanRevision++; row.PlanJson = Serialize(plan); row.Title = plan.Title; row.TargetDirectory = plan.OutputDirectory;
        row.Phase = State(row).Issues.Any(i => i.Blocking && i.Code != "OUTPUT_DIRECTORY_REQUIRED") ? "attention" : "ready";
        row.UpdatedAt = DateTime.Now;
        history.Add(new(row.PlanRevision, plan, sources, DateTime.UtcNow, reason, previousRunId, evidence)); row.PlanHistoryJson = Serialize(history);
    }
    private static List<AcquisitionPlanRevision> ReadHistory(AcquisitionTaskDbModel row) => row.PlanHistoryJson == null ? [] :
        JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(row.PlanHistoryJson, Json)!;
    private static DownloadIntentRequest Request(AcquisitionPlan plan, string? contextKey = null, bool includeMirrors = false) => new()
    {
        Mode = "files", Title = plan.Title, AvailabilityContextKey = contextKey,
        Candidates = plan.Parts.Where(p => p.ContinueFrom == null).SelectMany(p => p.Sources.Where(s => includeMirrors || s.Id == p.SelectedSourceId).Select(s => new DownloadCandidate
        {
            Id = s.Id, Url = s.Url, Kind = s.Kind, ProviderId = s.ProviderId, MethodId = s.MethodId, AccountId = s.AccountId,
            AccessCode = s.AccessCode, ArchivePassword = s.ArchivePassword,
            ExtractionPlanJson = s.Id == p.SelectedSourceId ? Serialize(p.Processing ?? new FileProcessingPlan()) :
                s.OriginalProcessingPlanJson ?? Serialize(s.Processing ?? new())
        })).ToList(),
        SelectedCandidateIds = plan.Parts.Where(p => p.ContinueFrom == null).Select(p => p.SelectedSourceId!).ToList()
    };
    private static AcquisitionPlan Plan(AcquisitionTaskDbModel row) => row.PlanJson == null ? throw new InvalidOperationException("Select a source before starting this task.") : JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson, Json)!;
    private static AcquisitionPlanBuilderState State(AcquisitionTaskDbModel row) => row.BuilderStateJson == null ? new() : JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson, Json)!;
    private static List<WorkflowSourceRef> Sources(AcquisitionTaskDbModel row) => row.SourceRefsJson == null ? [Owner(row)] : JsonSerializer.Deserialize<List<WorkflowSourceRef>>(row.SourceRefsJson, Json)!;
    private static WorkflowSourceRef Owner(AcquisitionTaskDbModel row) => new("acquisition", "task", row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
public sealed record AcquisitionPlanSourceTasks(WorkflowSourceRef Source, IReadOnlyList<AcquisitionTask> Tasks);
public sealed record AcquisitionPlanSourcesInput(IReadOnlyList<WorkflowSourceRef> Sources);
