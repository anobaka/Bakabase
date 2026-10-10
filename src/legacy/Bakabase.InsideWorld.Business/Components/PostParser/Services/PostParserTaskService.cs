using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public class PostParserTaskService<TDbContext>(TDbContext db,
    FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int> cache,
    PostParserTaskExecutionGate gate, PostParserWorkflowService<TDbContext> workflow,
    BTaskManager tasks, IBOptions<ThirdPartyOptions> options,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub, IBOptions<SoulPlusOptions> purchaseOptions) : IPostParserTaskService where TDbContext : DbContext
{
    private DbSet<PostParserTaskDbModel> ParserTasks => db.Set<PostParserTaskDbModel>();

    public async Task<List<PostParserTask>> GetAll()
    {
        await workflow.RefreshTasksAsync();
        var result = (await ParserTasks.AsNoTracking().ToListAsync()).Select(t => t.ToDomainModel()).ToList();
        var ids = result.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Status);
        foreach (var task in result)
            if (task.WorkflowRunId is { } id && runs.TryGetValue(id, out var status)) task.WorkflowStatus = status;
        foreach (var task in result)
        {
            task.AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold;
            task.MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins;
        }
        return result;
    }

    public async Task<TaskSummary> GetSummary(PostParserSource? source = null)
    {
        var all = await GetAll();
        var activeTasks = tasks.Tasks.Where(t => t.Task.Status is BTaskStatus.NotStarted or BTaskStatus.Running
                or BTaskStatus.Paused or BTaskStatus.Pausing or BTaskStatus.Resuming or BTaskStatus.Cancelling)
            .Select(t => t.Id).ToHashSet();
        return PostParserTaskSummary.Create(all, source, activeTasks);
    }

    public Task AddRange(Dictionary<PostParserSource, List<string>> sourceLinksMap, List<PostParseTarget> targets) =>
        AddInputs(sourceLinksMap, targets, [], null, null);

    public async Task AddInputs(Dictionary<PostParserSource, List<string>> sourceLinksMap,
        List<PostParseTarget> targets, List<string> links, string? text, string? title)
    {
        targets = targets.Count == 0 ? [PostParseTarget.DownloadInfo] : targets.Distinct().ToList();
        if (targets.Any(t => t != PostParseTarget.DownloadInfo))
            throw new InvalidOperationException("Only download-information extraction is currently supported.");
        var inputs = sourceLinksMap.SelectMany(p => p.Value.Select(link => (Source: p.Key, Link: link.Trim())))
            .Concat(links.Select(link => (Source: (PostParserSource)0, Link: link.Trim())))
            .Where(t => t.Link.Length > 0).Distinct().ToList();
        foreach (var input in inputs) PostParserManualTrigger.Validate(new() {Link = input.Link});
        if (inputs.Count == 0 && string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Provide at least one post link or some text.");
        await workflow.RefreshTasksAsync();
        var stoppedRuns = new List<int>();
        await gate.Semaphore.WaitAsync();
        try
        {
            var existing = await ParserTasks.ToListAsync();
            var activeRuns = (await db.Set<WorkflowRunDbModel>().AsNoTracking()
                .Where(r => r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running)
                .Select(r => r.Id).ToListAsync()).ToHashSet();
            var changed = new List<PostParserTaskDbModel>();
            var requested = new List<PostParserTaskDbModel>();
            foreach (var (source, link) in inputs)
            {
                var task = existing.FirstOrDefault(t => t.Source == source && t.Link == link && t.Text == null);
                if (task is {IsDeleted: false} &&
                    (task.WorkflowRunId is { } activeRun && activeRuns.Contains(activeRun) ||
                     task.Error == null && PostParserWorkflowService<TDbContext>.IsPending(task.ToDomainModel()) &&
                     task.ToDomainModel().Targets.Order().SequenceEqual(targets.Order())))
                {
                    requested.Add(task);
                    continue;
                }
                if (task == null)
                {
                    task = new PostParserTaskDbModel {Source = source, Link = link, CreatedAt = DateTime.UtcNow};
                    ParserTasks.Add(task);
                    existing.Add(task);
                }
                Reset(task, targets, title, stoppedRuns);
                changed.Add(task);
                requested.Add(task);
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Pasted text is its own task; do not accidentally merge unrelated posts by title.
                var task = new PostParserTaskDbModel {Source = 0, Link = "", Text = text.Trim(), CreatedAt = DateTime.UtcNow};
                Reset(task, targets, title, stoppedRuns);
                ParserTasks.Add(task);
                changed.Add(task);
                requested.Add(task);
            }
            await CancelRunsUnderGate(stoppedRuns);
            await db.SaveChangesAsync();
            cache.ClearCache();
            foreach (var task in changed) await Publish(task);
            if (options.Value.AutomaticallyParsingPosts)
                await workflow.DispatchUnderGateAsync(requested.Select(t => t.Id).ToList());
        }
        finally { gate.Semaphore.Release(); }
        await StopRuns(stoppedRuns);
    }

    internal static void Reset(PostParserTaskDbModel task, List<PostParseTarget> targets, string? title, List<int> stoppedRuns)
    {
        if (task.WorkflowRunId is { } runId) stoppedRuns.Add(runId);
        task.Revision++;
        task.WorkflowRunId = null;
        task.WorkflowDefinitionId = null;
        task.Results = null;
        task.ContentSnapshotJson = null;
        task.AvailabilityJson = null;
        task.ParsingState = null;
        task.ParsingMessage = null;
        task.Error = null;
        task.CompletedAt = null;
        task.IsDeleted = false;
        task.Targets = Newtonsoft.Json.JsonConvert.SerializeObject(targets);
        if (!string.IsNullOrWhiteSpace(title)) task.Title = title.Trim();
    }

    public async Task Delete(int id) => await DeleteWhere(t => t.Id == id);
    public Task<int> DeleteByLinks(PostParserSource source, List<string> links) =>
        DeleteWhere(t => t.Source == source && links.Contains(t.Link));
    public async Task DeleteAll() => await DeleteWhere(_ => true);

    private async Task<int> DeleteWhere(Func<PostParserTaskDbModel, bool> predicate)
    {
        var stoppedRuns = new List<int>();
        var count = 0;
        await gate.Semaphore.WaitAsync();
        try
        {
            var changed = (await ParserTasks.Where(t => !t.IsDeleted).ToListAsync()).Where(predicate).ToList();
            count = changed.Count;
            foreach (var task in changed)
            {
                task.IsDeleted = true;
                task.Revision++;
                if (task.WorkflowRunId is { } runId) stoppedRuns.Add(runId);
            }
            await CancelRunsUnderGate(stoppedRuns);
            await db.SaveChangesAsync();
            cache.ClearCache();
            foreach (var task in changed) await Publish(task);
        }
        finally { gate.Semaphore.Release(); }
        await StopRuns(stoppedRuns);
        return count;
    }

    public async Task ReParse(int id)
    {
        var stoppedRuns = new List<int>();
        await gate.Semaphore.WaitAsync();
        try
        {
            if (tasks.IsShuttingDown) return;
            await workflow.RefreshTasksUnderGateAsync(CancellationToken.None, [id]);
            var task = await ParserTasks.SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted);
            if (task == null) return;
            var status = task.WorkflowRunId is { } runId
                ? await db.Set<WorkflowRunDbModel>().Where(r => r.Id == runId)
                    .Select(r => (WorkflowRunStatus?)r.Status).SingleOrDefaultAsync()
                : null;
            if (status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running)
            {
                await workflow.DispatchUnderGateAsync([id]);
                return;
            }
            // Starting a new pending input does not create a new revision. A finished/waiting
            // attempt is explicitly replaced, and reserving its new run under this same gate
            // makes concurrent reparse requests observe that reservation instead of resetting it.
            if (task.WorkflowRunId != null || task.Error != null ||
                !PostParserWorkflowService<TDbContext>.IsPending(task.ToDomainModel()))
            {
                Reset(task, task.ToDomainModel().Targets, null, stoppedRuns);
                await CancelRunsUnderGate(stoppedRuns);
                await db.SaveChangesAsync();
                cache.ClearCache();
                await Publish(task);
            }
            await workflow.DispatchUnderGateAsync([id]);
        }
        finally { gate.Semaphore.Release(); }
        await StopRuns(stoppedRuns);
    }

    public Task Retry(int id) => workflow.RetryAsync(id);

    public async Task Put(int id, PostParserTask value)
    {
        await gate.Semaphore.WaitAsync();
        try
        {
            var current = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (current == null || current.IsDeleted || current.Revision != value.Revision || current.WorkflowRunId != value.WorkflowRunId)
                return;
            var model = (value with {Id = id, CreatedAt = current.CreatedAt, CompletedAt = current.CompletedAt}).ToDbModel();
            await ParserTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, model.Title)
                .SetProperty(t => t.Results, model.Results).SetProperty(t => t.Error, model.Error));
            cache.ClearCache();
            await Publish(model);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt, CancellationToken ct)
    {
        await pt.WaitWhilePausedAsync(ct);
        await workflow.DispatchAsync(ct,
            async (completed, total) =>
            {
                if (onProgress != null) await onProgress(total == 0 ? 100 : completed * 100 / total);
                if (onProcessChange != null) await onProcessChange($"{completed}/{total}");
            },
            () => pt.WaitWhilePausedAsync(ct));
    }

    public async Task<Dictionary<string, PostParserTaskStatus>> GetStatusesByLinks(PostParserSource source, List<string> links)
    {
        var all = await GetAll();
        return links.Distinct().ToDictionary(link => link, link =>
        {
            var task = all.FirstOrDefault(t => t.Source == source && t.Link == link);
            if (task == null) return PostParserTaskStatus.None;
            if (task.IsDeleted) return PostParserTaskStatus.Deleted;
            if (task.Error != null) return PostParserTaskStatus.Failed;
            return (task.WorkflowRunId != null && task.WorkflowStatus != WorkflowRunStatus.Success) || PostParserWorkflowService<TDbContext>.IsPending(task) ? PostParserTaskStatus.Pending : PostParserTaskStatus.Complete;
        });
    }

    private Task CancelRunsUnderGate(List<int> ids) => db.Set<WorkflowRunDbModel>()
        .Where(r => ids.Contains(r.Id) && (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting))
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled).SetProperty(r => r.CompletedAt, DateTime.Now));
    private async Task StopRuns(List<int> ids)
    {
        foreach (var id in ids.Distinct()) await tasks.Stop($"workflow.run.{id}");
    }
    private Task Publish(PostParserTaskDbModel task) => uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), task.ToDomainModel());
}
