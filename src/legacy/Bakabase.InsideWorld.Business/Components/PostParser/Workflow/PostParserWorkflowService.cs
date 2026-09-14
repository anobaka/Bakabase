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
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.PostParser.Models.Domain;
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
    IHubContext<WebGuiHub, IWebGuiClient> uiHub) : IPostParserWorkflowTaskBridge
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
            Description = "Read a post or pasted text and extract its download links and passwords. This workflow does not download, import or create resources.",
            DescriptionKey = "workflow.recipe.parsePostDownloadInfo.description",
            TriggerKind = PostParserWorkflow.Trigger, Enabled = true,
            Activities =
            [
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.ReadContent,
                    ConfigJson = "{\"useConfiguredSoulPlusPurchaseLimit\":true}", OnItemError = WorkflowActivityErrorBehavior.Fail},
                new WorkflowActivityInputModel {Kind = PostParserWorkflow.ExtractDownloadInfo,
                    ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail}
            ]
        }, ct);
        await db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == definition.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true), ct);
        return definition.Id;
    }

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await RefreshTasksUnderGateAsync(ct);
            var pending = (await ParserTasks.AsNoTracking().Where(t => !t.IsDeleted && t.Error == null && t.WorkflowRunId == null)
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
                    // Reading can safely restart: it re-reads purchase state before considering
                    // the legacy limit. A committed cursor also avoids an unresumable first step.
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
            var queued = await Runs.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Pending &&
                ParserTasks.Any(t => t.WorkflowRunId == r.Id && !t.IsDeleted)).ToListAsync(ct);
            foreach (var run in queued) await EnqueueAsync(run);
        }
        finally { gate.Semaphore.Release(); }
    }

    private Task EnqueueAsync(WorkflowRunDbModel run)
    {
        var runId = run.Id;
        var definitionId = run.WorkflowDefinitionId;
        return tasks.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named($"Workflow #{definitionId} run #{runId}")
            .IgnoreIfExists().ConflictsWith($"workflow.definition.{definitionId}")
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                try { await scope.ServiceProvider.GetRequiredService<WorkflowRunner<TDbContext>>().ExecuteAsync(runId, args); }
                finally
                {
                    await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<TDbContext>>().RefreshTasksAsync();
                }
            }));
    }

    public async Task RefreshTasksAsync(CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try { await RefreshTasksUnderGateAsync(ct); }
        finally { gate.Semaphore.Release(); }
    }

    private async Task RefreshTasksUnderGateAsync(CancellationToken ct)
    {
        var linked = await ParserTasks.AsNoTracking().Where(t => t.WorkflowRunId != null && !t.IsDeleted).ToListAsync(ct);
        var ids = linked.Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await Runs.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var snapshot in linked)
        {
            runs.TryGetValue(snapshot.WorkflowRunId!.Value, out var run);
            var error = run?.Status switch
            {
                WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted => run.ErrorMessage ?? "The parsing workflow failed.",
                WorkflowRunStatus.Cancelled => "The parsing workflow was cancelled.",
                null => "The parsing workflow run no longer exists. Parse this task again.",
                _ => null
            };
            if (run?.Status == WorkflowRunStatus.Success && IsPending(snapshot.ToDomainModel()))
                error = "The workflow completed without download information. Parse this task again.";
            if (snapshot.Error == error) continue;
            await ParserTasks.Where(t => t.Id == snapshot.Id && t.Revision == snapshot.Revision &&
                    t.WorkflowRunId == snapshot.WorkflowRunId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Error, error), ct);
            snapshot.Error = error;
            cache.ClearCache();
            await PublishAsync(snapshot, run?.Status);
        }
    }

    public async Task RetryAsync(int id, CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var task = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            if (task.WorkflowRunId is not { } runId)
                throw new InvalidOperationException("This task has no workflow run. Start parsing it first.");
            if (tasks.Tasks.Any(t => t.Id == $"workflow.run.{runId}" && !t.Task.Status.IsFinished()))
                throw new InvalidOperationException("The previous parsing task is still finishing. Retry in a moment.");
            await resumer.RequeueAsync(runId, ct);
            await ParserTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Error, (string?)null), ct);
            cache.ClearCache();
            task.Error = null;
            await PublishAsync(task, WorkflowRunStatus.Pending);
        }
        finally { gate.Semaphore.Release(); }
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
                title = domain.Title,
                resources = result.Resources.Select(r => new
                {
                    r.Link, r.Code, r.Password, DriveKind = AcquisitionDriveKinds.Infer(r.Link)
                }).ToList()
            }, WorkflowJson.Options);
            domain.Error = null;
            var results = domain.ToDbModel().Results;
            await ParserTasks.Where(t => t.Id == id && t.Revision == input.Revision && t.WorkflowRunId == runId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, domain.Title)
                    .SetProperty(t => t.Results, results).SetProperty(t => t.Error, (string?)null), ct);
            cache.ClearCache();
            await PublishAsync(domain.ToDbModel(), WorkflowRunStatus.Running);
        }
        finally { gate.Semaphore.Release(); }
    }

    private Task PublishAsync(PostParserTaskDbModel task, WorkflowRunStatus? status)
    {
        var domain = task.ToDomainModel();
        domain.WorkflowStatus = status;
        return uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), domain);
    }

    internal static bool IsPending(PostParserTask task) => task.Targets.Count > 0 &&
        (task.Results == null || task.Targets.Any(target => !task.Results.ContainsKey(target)));
}
