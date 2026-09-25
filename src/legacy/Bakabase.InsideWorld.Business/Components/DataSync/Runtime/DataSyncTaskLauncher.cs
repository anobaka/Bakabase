using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>What a cancel found (§8.10.1).</summary>
public enum DataSyncTaskCancelOutcome
{
    /// <summary>No task has that id.</summary>
    NotFound = 1,

    /// <summary>It was waiting; it was removed and never runs.</summary>
    Removed = 2,

    /// <summary>It was running; it was asked to stop and rolls back its current chunk.</summary>
    Stopping = 3,

    /// <summary>It had already finished, or was already stopping.</summary>
    AlreadyFinished = 4,
}

/// <summary>
/// Enqueues and cancels data sync's BTasks (§8.10.1). Every one-shot write task goes through
/// <see cref="EnqueueOnceAsync"/> or, for reviews and resolutions, a replacing enqueue, and registers a new attempt
/// (<see cref="IDataSyncTaskRegistry"/>) that its body checks before writing.
/// </summary>
/// <remarks>
/// <b>Why EnqueueOnce</b> (gate B3): <c>BTaskManager</c> keeps a finished non-persistent task in its map until
/// <c>Clean</c>. With a fixed id, <c>IgnoreIfExists</c> would ignore every enqueue after the first, so
/// <c>DataSyncApply</c> would run once per process, and the default <c>Reject</c> would refuse a second restore choice
/// or a retried undo. Nothing is enqueued once the host is stopping: <c>BTaskManager</c> has no shutdown check of its
/// own (F71).
/// </remarks>
public sealed class DataSyncTaskLauncher
{
    private readonly BTaskManager _btm;
    private readonly IDataSyncTaskRegistry _registry;
    private readonly IBakabaseLocalizer _localizer;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly IDataSyncRuntimeObserver _observer;
    private readonly ILogger<DataSyncTaskLauncher> _logger;
    private readonly SemaphoreSlim _enqueueLock = new(1, 1);

    public DataSyncTaskLauncher(BTaskManager btm, IDataSyncTaskRegistry registry, IBakabaseLocalizer localizer,
        IServiceScopeFactory scopes, IServiceProvider services, IDataSyncRuntimeObserver observer,
        ILogger<DataSyncTaskLauncher> logger)
    {
        _btm = btm;
        _registry = registry;
        _localizer = localizer;
        _scopes = scopes;
        _lifetime = services.GetService<IHostApplicationLifetime>();
        _observer = observer;
        _logger = logger;
    }

    /// <summary>True once the host started stopping: nothing is started or enqueued any more (§8.2).</summary>
    public bool IsStopping => _lifetime?.ApplicationStopping.IsCancellationRequested == true;

    /// <summary>
    /// A no-op (null) while a task with this id is NotStarted or active; otherwise cleans a finished one, registers
    /// a new attempt and enqueues the builder <paramref name="build"/> makes for it.
    /// </summary>
    public async Task<DataSyncTaskAttempt?> EnqueueOnceAsync(string taskId,
        Func<DataSyncTaskAttempt, BTaskHandlerBuilder> build)
    {
        if (IsStopping) return null;
        await _enqueueLock.WaitAsync();
        try
        {
            if (IsStopping) return null;
            var existing = _btm.GetTaskViewModel(taskId);
            if (existing is not null)
            {
                if (existing.Status.IsActiveOrPending()) return null;
                await _btm.Clean(taskId);
            }

            var attempt = _registry.Register(taskId);
            var builder = build(attempt);
            if (!string.Equals(builder.Id, taskId, StringComparison.Ordinal))
                throw new InvalidOperationException($"The builder's id {builder.Id} is not {taskId}.");
            await _btm.Enqueue(builder.OnDuplicateId(BTaskDuplicateIdHandling.Reject));
            return attempt;
        }
        finally
        {
            _enqueueLock.Release();
        }
    }

    /// <summary>Enqueues <c>DataSyncApply</c> unless it is already waiting or running (§8.2, §8.10.1).</summary>
    public Task<DataSyncTaskAttempt?> EnqueueApplyAsync() =>
        EnqueueOnceAsync(DataSyncTaskIds.Apply, attempt => WriteTask(DataSyncTaskIds.Apply, persistent: false,
            args => RunInScopeAsync(args, attempt,
                sp => sp.GetRequiredService<DataSyncApplyTask>().RunAsync(args, attempt))));

    /// <summary>The restore choice (§9.5). <paramref name="linkId"/>: a restore suspected through one link only.</summary>
    public Task<DataSyncTaskAttempt?> EnqueueRestoreAsync(DataSyncRestoreChoice choice, int? linkId) =>
        EnqueueOnceAsync(DataSyncTaskIds.Restore, attempt => WriteTask(DataSyncTaskIds.Restore, persistent: true,
            args => RunInScopeAsync(args, attempt, async sp =>
            {
                var logId = await sp.GetRequiredService<IDataSyncApplyRunner>().RunRestoreAsync(choice, linkId, args);
                await sp.GetRequiredService<DataSyncLinkService>().AfterRestoreAsync(linkId, args.CancellationToken);
                await ObserveAppliedAsync(DataSyncHistoryKind.Restore, logId, linkId, args.CancellationToken);
            })));

    /// <summary>Undo of one history entry (§8.11); retried after an Error by enqueueing it again.</summary>
    public Task<DataSyncTaskAttempt?> EnqueueUndoAsync(int applyLogId)
    {
        var taskId = DataSyncTaskIds.Undo(applyLogId);
        return EnqueueOnceAsync(taskId, attempt => WriteTask(taskId, persistent: true,
            args => RunInScopeAsync(args, attempt, async sp =>
            {
                var logId = await sp.GetRequiredService<IDataSyncApplyRunner>().RunUndoAsync(applyLogId, args);
                await ObserveAppliedAsync(DataSyncHistoryKind.Undo, logId, null, args.CancellationToken);
            })));
    }

    /// <summary>
    /// The first-link review or copy once (§8.10.3): replaces a finished task of the same review, and refuses (returns
    /// null) while one is waiting or running. <paramref name="linkId"/>: the link the review belongs to.
    /// </summary>
    public Task<DataSyncTaskAttempt?> EnqueueReviewAsync(string reviewId, IReadOnlyList<DataSyncPlanDecision> decisions,
        DataSyncApplyOptions options, Func<int?, Task>? onApplied = null, int? linkId = null)
    {
        var taskId = DataSyncTaskIds.Review(reviewId);
        return EnqueueOnceAsync(taskId, attempt => WriteTask(taskId, persistent: true,
            args => RunInScopeAsync(args, attempt, async sp =>
            {
                var logId = await sp.GetRequiredService<IDataSyncApplyRunner>()
                    .RunReviewAsync(reviewId, decisions, options, args);
                if (onApplied is not null) await onApplied(logId);
                await ObserveAppliedAsync(DataSyncHistoryKind.FirstLink, logId, linkId, args.CancellationToken);
            })));
    }

    /// <summary>
    /// Inbox resolutions (§9.2), one task per batch. <paramref name="thenApply"/>: the batch let waiting records go
    /// ("Apply all" of a large change), whose re-merge runs in <c>DataSyncApply</c>, so that task is enqueued right
    /// after (§8.2, N13).
    /// </summary>
    public Task<DataSyncTaskAttempt?> EnqueueResolveAsync(string batchId,
        IReadOnlyList<DataSyncResolveInput> resolutions, DataSyncApplyOptions options, bool thenApply = false)
    {
        var taskId = DataSyncTaskIds.Resolve(batchId);
        return EnqueueOnceAsync(taskId, attempt => WriteTask(taskId, persistent: true,
            args => RunInScopeAsync(args, attempt, async sp =>
            {
                var logId = await sp.GetRequiredService<IDataSyncApplyRunner>()
                    .RunResolutionsAsync(resolutions, options, args);
                if (thenApply) await EnqueueApplyAsync();
                await ObserveAppliedAsync(DataSyncHistoryKind.Resolution, logId, null, args.CancellationToken);
            })));
    }

    /// <summary>
    /// Tells the notifier and the hub what a write task applied (§8.10.6, §9.4), after the runner committed. A failure
    /// there is logged and never fails the task: the change is stored.
    /// </summary>
    private async Task ObserveAppliedAsync(DataSyncHistoryKind kind, int? logId, int? linkId, CancellationToken ct)
    {
        try
        {
            await _observer.WriteAppliedAsync(kind, logId, linkId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "A data sync observer failed after {Kind}", kind);
        }
    }

    /// <summary>
    /// Cancel (§8.10.1): sets the attempt's cancel flag <b>before</b> reading the task's status, then removes a waiting
    /// task or stops a running one. The recurring <c>DataSync</c> task has no attempt; it is stopped as any BTask, and
    /// the scheduler respects that stop (§8.2).
    /// </summary>
    public async Task<DataSyncTaskCancelOutcome> CancelAsync(string taskId)
    {
        _registry.RequestCancel(taskId);
        var task = _btm.GetTaskViewModel(taskId);
        if (task is null) return DataSyncTaskCancelOutcome.NotFound;
        switch (task.Status)
        {
            case BTaskStatus.NotStarted:
                await _btm.Clean(taskId);
                return DataSyncTaskCancelOutcome.Removed;
            case var s when s.CanBeStopped():
                await _btm.Stop(taskId);
                return DataSyncTaskCancelOutcome.Stopping;
            default:
                return DataSyncTaskCancelOutcome.AlreadyFinished;
        }
    }

    /// <summary>
    /// <c>ApplyInProgress</c> (§8.10.3): a data sync task that writes definitions is waiting or running.
    /// </summary>
    public bool IsWriteTaskActiveOrPending() =>
        _btm.Tasks.Any(t => DataSyncTaskIds.IsWriteTask(t.Id) && t.Task.Status.IsActiveOrPending());

    /// <summary>The id of a write task that is waiting or running, preferring a running one.</summary>
    public string? GetActiveWriteTaskId() =>
        _btm.Tasks.Where(t => DataSyncTaskIds.IsWriteTask(t.Id) && t.Task.Status.IsActiveOrPending())
            .OrderByDescending(t => t.Task.Status.IsActive())
            .Select(t => t.Id)
            .FirstOrDefault();

    private BTaskHandlerBuilder WriteTask(string taskId, bool persistent, Func<BTaskArgs, Task> run)
    {
        var key = DataSyncTaskIds.NameKey(taskId);
        return BTaskBuilder.Create(taskId)
            .Named(() => _localizer.BTask_Name(key))
            .Describe(() => _localizer.BTask_Description(key))
            .InterruptionMessage(() => _localizer.BTask_MessageOnInterruption(key))
            .ConflictsWith(DataSyncTaskIds.WriteConflictKeys)
            .Persistent(persistent)
            .StartImmediately()
            .Run(run);
    }

    /// <summary>
    /// The shape every write task body shares: a cooperative checkpoint and the attempt check before anything else, a
    /// scope of its own, and the attempt flowing to the runner (<see cref="DataSyncTaskAttemptContext"/>).
    /// <see cref="OperationCanceledException"/> is never wrapped, so a stopped task ends Cancelled (v3.1 M-f).
    /// </summary>
    private async Task RunInScopeAsync(BTaskArgs args, DataSyncTaskAttempt attempt, Func<IServiceProvider, Task> body)
    {
        await args.YieldAsync();
        if (!_registry.ShouldRun(attempt.TaskId, attempt.AttemptId))
        {
            _logger.LogInformation("Data sync task {TaskId} exits without running: its attempt was cancelled or replaced",
                attempt.TaskId);
            return;
        }

        using var _ = DataSyncTaskAttemptContext.Enter(attempt);
        await using var scope = _scopes.CreateAsyncScope();
        await body(scope.ServiceProvider);
    }
}
