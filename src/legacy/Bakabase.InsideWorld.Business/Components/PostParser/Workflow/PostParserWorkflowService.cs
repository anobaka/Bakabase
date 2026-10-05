using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.PostParser.Models.Domain;
using PostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bootstrap.Components.Orm;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

/// <summary>
/// Compatibility orchestration for saved parser tasks. A task and its workflow run are committed
/// together; the task revision prevents a deleted or superseded execution from publishing results.
/// The workflow engine owns execution, step snapshots, retry and restart recovery.
/// </summary>
public sealed class PostParserWorkflowService<TDbContext>(TDbContext db,
    FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int> cache,
    PostParserTaskExecutionGate gate, IWorkflowDefinitionService definitions,
    BTaskManager tasks, IWorkflowRunResumer resumer,
    WorkflowRunSchedulingPolicyResolver scheduling,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub, IBOptions<SoulPlusOptions> purchaseOptions) : IPostParserWorkflowTaskBridge
    where TDbContext : DbContext
{
    private DbSet<PostParserTaskDbModel> ParserTasks => db.Set<PostParserTaskDbModel>();
    private DbSet<WorkflowRunDbModel> Runs => db.Set<WorkflowRunDbModel>();

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        var existing = await db.Set<WorkflowDefinitionDbModel>().AsNoTracking().FirstOrDefaultAsync(d =>
            d.TriggerKind == PostParserWorkflow.Trigger && d.Name == PostParserWorkflow.BuiltinName && d.IsBuiltin, ct);
        if (existing != null) return existing.Id;
        var definition = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = PostParserWorkflow.BuiltinName,
            Description = "Save post content, assess restricted items before purchases, and extract download links with ordered extraction instructions. This workflow does not download, import or create resources.",
            DescriptionKey = "workflow.recipe.parsePostDownloadInfo.description",
            TriggerKind = PostParserWorkflow.Trigger, Enabled = true,
            Activities =
            [
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.ReadContent,
                    ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail},
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.UnlockContent,
                    ConfigJson = "{\"useConfiguredSoulPlusPurchaseLimit\":true}", OnItemError = WorkflowActivityErrorBehavior.Fail},
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.ExtractDownloadInfo,
                    ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail},
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.CheckLinks,
                    ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail}
            ]
        }, ct);
        await db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == definition.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true), ct);
        return definition.Id;
    }

    public async Task DispatchAsync(CancellationToken ct = default,
        Func<int, int, Task>? onProgress = null, Func<Task>? checkpoint = null)
    {
        List<int> ids;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (tasks.IsShuttingDown) return;
            await RefreshTasksUnderGateAsync(ct);
            var candidates = await ParserTasks.AsNoTracking().Where(t => !t.IsDeleted &&
                    (t.WorkflowRunId == null || !Runs.Any(r => r.Id == t.WorkflowRunId) ||
                     Runs.Any(r => r.Id == t.WorkflowRunId && (r.Status == WorkflowRunStatus.Pending ||
                         r.Status == WorkflowRunStatus.Failed || r.Status == WorkflowRunStatus.Interrupted ||
                         r.Status == WorkflowRunStatus.Cancelled))))
                .OrderBy(t => t.Id).ToListAsync(ct);
            ids = candidates.Where(t => t.WorkflowRunId != null || t.Error != null || IsPending(t.ToDomainModel()))
                .Select(t => t.Id).ToList();
        }
        finally { gate.Semaphore.Release(); }

        if (onProgress != null) await onProgress(0, ids.Count);
        var failures = new List<Exception>();
        for (var i = 0; i < ids.Count; i++)
        {
            // Waiting here cannot block single-post actions behind a paused bulk dispatcher.
            if (checkpoint != null) await checkpoint();
            ct.ThrowIfCancellationRequested();
            await gate.Semaphore.WaitAsync(ct);
            try
            {
                try { await StartOrRetryUnderGateAsync(ids[i], ct); }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    // A failed transaction or retry may leave tracked inserts/changes behind.
                    // Do not let a later post's SaveChanges replay those failed mutations.
                    db.ChangeTracker.Clear();
                    await RecordDispatchFailureUnderGateAsync(ids[i], e.Message, ct);
                    failures.Add(new InvalidOperationException($"Post parser task #{ids[i]} could not be queued: {e.Message}", e));
                }
            }
            finally { gate.Semaphore.Release(); }
            if (onProgress != null) await onProgress(i + 1, ids.Count);
        }
        if (failures.Count > 0) throw new AggregateException("Some post parser tasks could not be queued.", failures);
    }

    private async Task StartOrRetryUnderGateAsync(int id, CancellationToken ct)
    {
        if (tasks.IsShuttingDown) return;
        var task = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
        if (task == null) return;
        var run = task.WorkflowRunId is { } runId ? await Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        // The snapshot may have changed while the dispatcher paused or a row action ran.
        if (run?.Status is WorkflowRunStatus.Success or WorkflowRunStatus.Waiting or WorkflowRunStatus.Running) return;
        if (run?.Status is WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled)
        {
            if (HasUnfinishedRunTask(run.Id)) return;
            await RetryUnderGateAsync(task, run.Status, ct);
            return;
        }
        if (run == null && (task.Error != null || task.WorkflowRunId != null))
        {
            if (task.WorkflowRunId is { } missingRunId && HasUnfinishedRunTask(missingRunId)) return;
            var tracked = await ParserTasks.SingleAsync(t => t.Id == id, ct);
            PostParserTaskService<TDbContext>.Reset(tracked, task.ToDomainModel().Targets, null, []);
            await db.SaveChangesAsync(ct);
            cache.ClearCache();
        }
        await DispatchUnderGateAsync([id], ct);
    }

    private async Task RecordDispatchFailureUnderGateAsync(int id, string message, CancellationToken ct)
    {
        var task = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
        if (task == null) return;
        WorkflowRunStatus? status = null;
        if (task.WorkflowRunId is { } runId)
        {
            // Enqueuing may have succeeded before publishing its UI update failed.
            if (HasUnfinishedRunTask(runId)) return;
            var run = await Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
            status = run?.Status;
            if (status is WorkflowRunStatus.Running or WorkflowRunStatus.Success or WorkflowRunStatus.Waiting) return;
            if (run != null)
            {
                await Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s =>
                    s.SetProperty(r => r.Status, WorkflowRunStatus.Failed).SetProperty(r => r.ErrorMessage, message)
                        .SetProperty(r => r.CompletedAt, DateTime.Now), ct);
                status = WorkflowRunStatus.Failed;
            }
        }
        await ParserTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.Error, message).SetProperty(t => t.CompletedAt, (DateTime?)null), ct);
        task.Error = message;
        task.CompletedAt = null;
        cache.ClearCache();
        await PublishAsync(task, status);
    }

    private bool HasUnfinishedRunTask(int runId) =>
        tasks.Tasks.Any(t => t.Id == $"workflow.run.{runId}" && !t.Task.Status.IsFinished());

    /// <summary>The caller holds the parser gate across its input/reset and this dispatch.</summary>
    internal async Task DispatchUnderGateAsync(IReadOnlyCollection<int>? taskIds, CancellationToken ct = default)
    {
        if (tasks.IsShuttingDown || taskIds is {Count: 0}) return;
        await RefreshTasksUnderGateAsync(ct, taskIds);
        var candidates = ParserTasks.AsNoTracking().Where(t => !t.IsDeleted && t.Error == null && t.WorkflowRunId == null);
        if (taskIds != null) candidates = candidates.Where(t => taskIds.Contains(t.Id));
        var pending = (await candidates
                .ToListAsync(ct)).Where(t => IsPending(t.ToDomainModel())).ToList();
        int? definitionId = null;
        foreach (var snapshot in pending)
        {
            ct.ThrowIfCancellationRequested();
            definitionId ??= await SeedAsync(ct);
            var task = await ParserTasks.SingleAsync(t => t.Id == snapshot.Id, ct);
            if (!await db.Set<WorkflowDefinitionDbModel>().AsNoTracking().AnyAsync(d => d.Id == definitionId && d.Enabled, ct))
            {
                task.Error = "The built-in parsing workflow is disabled. Enable it before parsing this task again.";
                await db.SaveChangesAsync(ct);
                cache.ClearCache();
                await PublishAsync(task, null);
                continue;
            }
            // Validation runs in the engine and is recorded as a failed run, so missing
            // credentials do not silently disappear from this page or block another task.
            var payload = new PostParserInput
            {
                TaskId = task.Id, Revision = task.Revision, Link = string.IsNullOrWhiteSpace(task.Text) ? task.Link : null,
                Text = task.Text, Title = task.Title,
                SourceHint = task.Source == PostParserSource.SoulPlus ? nameof(PostParserSource.SoulPlus) : null
            };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = definitionId.Value, Status = WorkflowRunStatus.Pending,
                StartedAt = DateTime.Now, PayloadJson = JsonSerializer.Serialize(payload, WorkflowJson.Options),
                PayloadSummary = task.Title ?? (task.Text is {Length: > 0} text ? text[..Math.Min(120, text.Length)] : task.Link),
                // Reading has no purchasing side effects. A committed cursor also avoids
                // an unresumable first step after a restart.
                CurrentStepIndex = 0
            };
            Runs.Add(run);
            await db.SaveChangesAsync(ct);
            task.WorkflowRunId = run.Id;
            task.WorkflowDefinitionId = definitionId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            cache.ClearCache();
            await PublishAsync(task, run.Status);
        }

        // This also handles a crash or enqueue failure after the transaction committed.
        var selectedTasks = ParserTasks.Where(t => !t.IsDeleted);
        if (taskIds != null) selectedTasks = selectedTasks.Where(t => taskIds.Contains(t.Id));
        var queued = await Runs.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Pending &&
            selectedTasks.Any(t => t.WorkflowRunId == r.Id)).ToListAsync(ct);
        foreach (var run in queued) await EnqueueAsync(run);
    }

    private Task EnqueueAsync(WorkflowRunDbModel run)
    {
        if (tasks.IsShuttingDown) return Task.CompletedTask;
        var runId = run.Id;
        var definitionId = run.WorkflowDefinitionId;
        return tasks.Enqueue(scheduling.Configure(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named($"Workflow #{definitionId} run #{runId}")
            .Persistent().IgnoreIfExists()
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                try { await scope.ServiceProvider.GetRequiredService<WorkflowRunner<TDbContext>>().ExecuteAsync(runId, args); }
                finally
                {
                    await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<TDbContext>>().RefreshTasksAsync();
                }
            }), definitionId, PostParserWorkflow.Trigger));
    }

    public async Task RefreshTasksAsync(CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try { await RefreshTasksUnderGateAsync(ct); }
        finally { gate.Semaphore.Release(); }
    }

    internal async Task RefreshTasksUnderGateAsync(CancellationToken ct, IReadOnlyCollection<int>? taskIds = null)
    {
        var query = ParserTasks.AsNoTracking().Where(t => t.WorkflowRunId != null && !t.IsDeleted);
        if (taskIds != null) query = query.Where(t => taskIds.Contains(t.Id));
        var linked = await query.ToListAsync(ct);
        var ids = linked.Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await Runs.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var snapshot in linked)
        {
            runs.TryGetValue(snapshot.WorkflowRunId!.Value, out var run);
            // Stopping a queued BTask never enters the runner, so its persisted run has not
            // had a chance to observe cancellation. Reconcile only a terminal, detached task;
            // a missing handler may simply be between committing and enqueueing the run.
            if (run?.Status == WorkflowRunStatus.Pending && tasks.Tasks.FirstOrDefault(t =>
                    t.Id == $"workflow.run.{run.Id}" && !t.HasAttachedExecution &&
                    t.Task.Status is BTaskStatus.Cancelled or BTaskStatus.Error) is { } stopped)
            {
                var terminalStatus = stopped.Task.Status == BTaskStatus.Cancelled
                    ? WorkflowRunStatus.Cancelled : WorkflowRunStatus.Failed;
                var message = stopped.Task.Error ?? (terminalStatus == WorkflowRunStatus.Cancelled
                    ? "The queued parsing task was cancelled." : "The queued parsing task failed before execution.");
                await Runs.Where(r => r.Id == run.Id && r.Status == WorkflowRunStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, terminalStatus)
                        .SetProperty(r => r.CompletedAt, DateTime.Now).SetProperty(r => r.ErrorMessage, message), ct);
                run = await Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == run.Id, ct);
            }
            var error = run?.Status switch
            {
                WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted => run.ErrorMessage ?? "The parsing workflow failed.",
                WorkflowRunStatus.Cancelled => "The parsing workflow was cancelled.",
                null => "The parsing workflow run no longer exists. Parse this task again.",
                _ => null
            };
            if (run?.Status == WorkflowRunStatus.Success && IsPending(snapshot.ToDomainModel()))
                error = "The workflow completed without download information. Parse this task again.";
            // A saved result is published while the workflow is still running. Record completion
            // only after this task's current execution has finished successfully.
            // Workflow timestamps use server-local time, including unspecified values reloaded
            // from SQLite. Parser timestamps are stored and exposed as UTC instants.
            var completedAt = run?.Status == WorkflowRunStatus.Success && error == null
                ? run.CompletedAt?.ToUniversalTime()
                : null;
            if (snapshot.Error == error && snapshot.CompletedAt == completedAt) continue;
            var updated = await ParserTasks.Where(t => t.Id == snapshot.Id && t.Revision == snapshot.Revision &&
                    t.WorkflowRunId == snapshot.WorkflowRunId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Error, error)
                    .SetProperty(t => t.CompletedAt, completedAt), ct);
            if (updated == 0) continue;
            snapshot.Error = error;
            snapshot.CompletedAt = completedAt;
            cache.ClearCache();
            await PublishAsync(snapshot, run?.Status);
        }
    }

    public async Task RetryAsync(int id, CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (tasks.IsShuttingDown) return;
            await RefreshTasksUnderGateAsync(ct, [id]);
            var task = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            if (task.WorkflowRunId is not { } runId)
                throw new InvalidOperationException("This task has no workflow run. Start parsing it first.");
            var status = await Runs.Where(r => r.Id == runId).Select(r => r.Status).SingleAsync(ct);
            await RetryUnderGateAsync(task, status, ct);
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task RetryUnderGateAsync(PostParserTaskDbModel task, WorkflowRunStatus status, CancellationToken ct)
    {
        var runId = task.WorkflowRunId!.Value;
        // A repeated click must neither enqueue another execution nor replace the current one.
        if (status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running) return;
        if (HasUnfinishedRunTask(runId))
            throw new InvalidOperationException("The previous parsing task is still finishing. Retry in a moment.");
        if (status == WorkflowRunStatus.Waiting) await resumer.ResumeAsync(runId, "{}", ct);
        else await resumer.RequeueAsync(runId, ct);
        await ParserTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Error, (string?)null)
            .SetProperty(t => t.CompletedAt, (DateTime?)null), ct);
        cache.ClearCache();
        task.Error = null;
        task.CompletedAt = null;
        await PublishAsync(task, WorkflowRunStatus.Pending);
    }

    public async Task EnsureCurrentAsync(PostParserInput input, int runId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (input.TaskId is not { } id) return;
        if (!await ParserTasks.AsNoTracking().AnyAsync(t => t.Id == id && t.Revision == input.Revision &&
                t.WorkflowRunId == runId && !t.IsDeleted, ct))
            throw new OperationCanceledException("The parsing task was removed or replaced by a newer request.");
    }

    public async Task SaveResultAsync(PostParserInput input, int runId, PostDownloadInfo result, CancellationToken ct)
    {
        if (input.TaskId is not { } id) return;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await EnsureCurrentAsync(input, runId, ct);
            var task = await ParserTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            var domain = task.ToDomainModel();
            domain.Title = string.IsNullOrWhiteSpace(result.Title) ? domain.Title : result.Title;
            domain.Results ??= new();
            domain.Results[PostParseTarget.DownloadInfo] = JsonSerializer.SerializeToNode(new
            {
                result.SchemaVersion, result.IsComplete, result.Warnings, result.Availability, result.Groups,
                title = domain.Title,
                resources = result.Resources.Select(r => new
                {
                    r.Link, r.GroupId, r.Code, r.Password, r.Extraction, r.LinkHealth, DriveKind = AcquisitionDriveKinds.Infer(r.Link)
                }).ToList()
            }, WorkflowJson.Options);
            domain.ParsingState = result.IsComplete ? "complete" : "partial";
            domain.Error = null;
            var results = domain.ToDbModel().Results;
            await ParserTasks.Where(t => t.Id == id && t.Revision == input.Revision && t.WorkflowRunId == runId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, domain.Title)
                    .SetProperty(t => t.Results, results).SetProperty(t => t.Error, (string?)null)
                    .SetProperty(t => t.ParsingState, domain.ParsingState), ct);
            cache.ClearCache();
            await PublishAsync(domain.ToDbModel(), WorkflowRunStatus.Running);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task SaveSnapshotAsync(PostParserInput input, int runId, PostContent content,
        PostAvailabilityAssessment? availability, string state, string? message, CancellationToken ct)
    {
        if (input.TaskId is not { } id) return;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await EnsureCurrentAsync(input, runId, ct);
            var snapshotJson = JsonSerializer.Serialize(content, WorkflowJson.Options);
            var assessmentJson = availability == null ? null : JsonSerializer.Serialize(availability, WorkflowJson.Options);
            await ParserTasks.Where(t => t.Id == id && t.Revision == input.Revision && t.WorkflowRunId == runId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, content.Title)
                    .SetProperty(t => t.ContentSnapshotJson, snapshotJson).SetProperty(t => t.AvailabilityJson, assessmentJson)
                    .SetProperty(t => t.ParsingState, state).SetProperty(t => t.ParsingMessage, message)
                    .SetProperty(t => t.CompletedAt, (DateTime?)null), ct);
            cache.ClearCache();
            await PublishAsync(await ParserTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct), WorkflowRunStatus.Running);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task PurchaseAndResumeAsync(int id, int revision, IReadOnlyList<string> lockUrls,
        decimal maxTotalCost, CancellationToken ct)
    {
        if (lockUrls.Count is 0 or > 100) throw new ArgumentException("Select between 1 and 100 restricted items.");
        if (maxTotalCost < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalCost), "The approved total cannot be negative.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (tasks.IsShuttingDown) throw new InvalidOperationException("The application is shutting down. Try again after restarting.");
            var task = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            if (task.Revision != revision) throw new InvalidOperationException("The post changed. Refresh before purchasing.");
            if (task.WorkflowRunId is not { } runId) throw new InvalidOperationException("Read the post before purchasing.");
            var run = await Runs.SingleAsync(r => r.Id == runId, ct);
            // A double click or another client may have already accepted this request.
            if (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running) return;
            if (tasks.Tasks.Any(t => t.Id == $"workflow.run.{runId}" && !t.Task.Status.IsFinished()))
                throw new InvalidOperationException("The previous operation is still finishing. Retry in a moment.");
            if (run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted))
                throw new InvalidOperationException("This parsing task is not waiting for a purchase.");
            var node = await db.Set<WorkflowActivityDbModel>().AsNoTracking().SingleOrDefaultAsync(a =>
                a.WorkflowDefinitionId == run.WorkflowDefinitionId && a.Order == run.CurrentStepIndex, ct);
            if (node?.Kind != PostParserWorkflow.UnlockContent)
                throw new InvalidOperationException("Refresh or retry this post before purchasing its content.");
            var domain = task.ToDomainModel();
            var content = domain.ContentSnapshot ?? throw new InvalidOperationException("No saved purchase quote exists. Read the post first.");
            if (domain.Availability?.Status != "expired")
                throw new InvalidOperationException("One-click unlocking is only available for a post assessed as possibly expired. Parse it again to use automatic purchasing.");
            var quote = PostParserPurchaseQuote.Create(content, purchaseOptions.Value.AutoBuyThreshold,
                purchaseOptions.Value.MinimumRemainingCoins);
            var eligible = quote.EligibleLockUrls.ToHashSet(StringComparer.Ordinal);
            if (lockUrls.Any(url => !eligible.Contains(url)))
                throw new InvalidOperationException("A selected item is no longer eligible under the quoted price, purchase limit or minimum balance. Refresh the post before purchasing.");
            var selected = lockUrls.ToHashSet(StringComparer.Ordinal);
            var currentTotal = PostParserPurchaseQuote.Offers(content).Where(l => l.Url != null && selected.Contains(l.Url))
                .Sum(l => l.Price!.Value);
            if (currentTotal > maxTotalCost)
                throw new InvalidOperationException("The quoted total has increased since approval. Refresh the post before purchasing.");
            var input = JsonSerializer.Deserialize<PostParserInput>(run.PayloadJson!, WorkflowJson.Options)
                ?? throw new InvalidOperationException("The parsing input is missing.");
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(new PostParserContentItem(input, content) {Availability = domain.Availability});
            run.Status = WorkflowRunStatus.Waiting;
            run.ErrorMessage = null;
            run.CompletedAt = null;
            await db.SaveChangesAsync(ct);
            await resumer.ResumeAsync(runId, JsonSerializer.Serialize(new PostParserPurchaseSignal
                {LockUrls = lockUrls.Distinct().ToList(), EnforceConfiguredLimits = true, MaxTotalCost = maxTotalCost}, WorkflowJson.Options), ct);
            await ParserTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Error, (string?)null)
                .SetProperty(t => t.CompletedAt, (DateTime?)null), ct);
            cache.ClearCache();
            task.Error = null;
            task.CompletedAt = null;
            await PublishAsync(task, WorkflowRunStatus.Pending);
        }
        finally { gate.Semaphore.Release(); }
    }

    private Task PublishAsync(PostParserTaskDbModel task, WorkflowRunStatus? status)
    {
        var domain = task.ToDomainModel();
        domain.WorkflowStatus = status;
        domain.AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold;
        domain.MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins;
        return uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), domain);
    }

    internal static bool IsPending(PostParserTask task) => task.Targets.Count > 0 &&
        (task.ParsingState is not null and not "complete" || task.Results == null || task.Targets.Any(target => !task.Results.ContainsKey(target)));
}
