using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The body of <c>DataSyncApply</c> (§8.10.1): applies every staged pull and every link whose pending records wait
/// for a re-merge, link by link, through
/// <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/>, and loops until nothing waits. It never waits for the actor's
/// verification (§5.6) while holding the write tasks' conflict keys, which would hold the enhancer, resource sync and
/// path-mark sync with it: while the actor is unverified it ends at once, and the scheduler enqueues it again on the
/// tick that verifies. "Pause all" (§8.7 "Other pauses") holds it the same way, read again before each link: what is
/// staged stays staged, and the scheduler enqueues the apply again once sync is unpaused, so a pull that waited behind
/// the enhancer is never applied after the person paused everything. A link that fails is recorded and backs off; it
/// is not tried again in the same run, so a failing link can never keep the task spinning.
/// </summary>
public sealed class DataSyncApplyTask
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataSyncTaskRegistry _registry;
    private readonly DataSyncLinkService _links;
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncClock _clock;
    private readonly ILogger<DataSyncApplyTask> _logger;

    public DataSyncApplyTask(IServiceScopeFactory scopes, IDataSyncTaskRegistry registry, DataSyncLinkService links,
        DataSyncRuntimeState state, IDataSyncClock clock, ILogger<DataSyncApplyTask> logger)
    {
        _scopes = scopes;
        _registry = registry;
        _links = links;
        _state = state;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(BTaskArgs args, DataSyncTaskAttempt attempt)
    {
        var ct = args.CancellationToken;
        var done = new HashSet<int>();
        while (true)
        {
            await args.YieldAsync();
            if (!_registry.ShouldRun(attempt.TaskId, attempt.AttemptId)) return;
            if (!await IsVerifiedAsync())
            {
                _logger.LogInformation("Data sync applies nothing until this device's actor is verified");
                return;
            }

            var work = await NextWorkAsync(done, ct);
            if (work.Count == 0) return;
            foreach (var linkId in work)
            {
                await args.YieldAsync();
                if (!_registry.ShouldRun(attempt.TaskId, attempt.AttemptId)) return;
                if (await IsAllPausedAsync(ct))
                {
                    _logger.LogInformation("Data sync applies nothing while it is paused");
                    return;
                }

                var pull = _state.TakePull(linkId);
                if (pull is null) done.Add(linkId);
                if (!await ApplyLinkAsync(linkId, pull, args)) return;
            }
        }
    }

    /// <summary>
    /// A first sync's Start (§8.3): the snapshot staged for the link, applied through
    /// <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/> with the person's choices. Once it committed the snapshot goes;
    /// otherwise it stays for another Start, and the task ends in an error that says so unless the link paused.
    /// </summary>
    public async Task RunFirstSyncAsync(int linkId, IReadOnlyList<DataSyncFirstSyncChoice> choices, BTaskArgs args)
    {
        if (_state.PeekPreview(linkId) is not { } pull) return;
        await using var scope = _scopes.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IDataSyncApplyRunner>()
            .RunAutoSyncAsync(linkId, pull, args, choices);
        // Not applied either: the next cycle previews the snapshot again, against this device's definitions as they are
        // then (a name match the preview missed, say).
        var notApplied = outcome.End != DataSyncAutoSyncEnd.Committed && outcome.Paused is null or DataSyncPauseReason.AllPaused;
        if (outcome.End == DataSyncAutoSyncEnd.Committed || notApplied) _state.DropPreview(linkId);
        if (notApplied) throw new BTaskException("NotApplied", "Nothing was applied; start the first sync again.");
        await AfterAppliedAsync(scope.ServiceProvider, linkId, outcome, args.CancellationToken);
    }

    /// <summary>Links with a staged pull, then links whose pending re-merge waits (not while backing off).</summary>
    private async Task<IReadOnlyList<int>> NextWorkAsync(HashSet<int> done, CancellationToken ct)
    {
        var work = _state.StagedLinks().ToList();
        var now = _clock.UtcNow;
        foreach (var link in await _links.GetLinksAsync(ct))
        {
            if (work.Contains(link.Id) || done.Contains(link.Id)) continue;
            if (!link.IsRunning() || !_state.IsReMergeRequested(link.Id)) continue;
            if (link.ConsecutiveFailures > 0 && !_state.IsDue(link.Id, now)) continue;
            work.Add(link.Id);
        }

        return work;
    }

    /// <returns>
    /// False when the task stops there: "Pause all" was found pressed once the runner held the gate, or the runner
    /// applied nothing for a reason that holds for every link (the attempt ended, the actor is unverified or kept
    /// changing). What the apply took waits for the next run, which the scheduler enqueues.
    /// </returns>
    private async Task<bool> ApplyLinkAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var link = await _links.GetAsync(linkId, ct);
        if (link is null || !link.IsRunning() || link.Mode == DataSyncLinkMode.Off)
        {
            // Paused, stopped, reset, refused or back to a first contact since the fetch: the pull is dropped, and the
            // next fetch starts again from the cursor (§8.7).
            return true;
        }

        // Any apply of the link re-merges what its pending records wait for (§8.4), so it takes a requested re-merge.
        var reMerge = _state.TakeReMerge(linkId);
        if (pull is null && !reMerge) return true;

        // Taken from the store, a pull that reconciles a kind in full no longer says so there: the apply does (§11.6).
        using var reconciling = pull?.Kinds.Any(k => k.FullReconciliation) == true
            ? _state.BeginFullReconciliation(linkId)
            : null;
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        DataSyncAutoSyncOutcome outcome;
        try
        {
            outcome = await sp.GetRequiredService<IDataSyncApplyRunner>().RunAutoSyncAsync(linkId, pull, args);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A stopped apply rolled back; put the pull back so the next run applies it without a
            // refetch, unless a newer one arrived meanwhile.
            KeepForNextRun(linkId, pull, reMerge);
            throw;
        }
        catch (Exception e)
        {
            // Rolled back (§8.10.2): ApplyFailed and a backoff before the link's next attempt. The pull is dropped and
            // the link is not tried again in this run; the next fetch brings the records again.
            _logger.LogError(e, "Data sync could not apply the pull from {Peer}", link.PeerNodeId);
            await _links.RecordFailureAsync(linkId, DataSyncLinkService.ApplyFailed, e.Message, null, ct);
            return true;
        }

        // The runner re-checks "Pause all" and the link once it holds the gate (§8.10.2). "Pause all" pressed while this
        // apply waited for the gate applied nothing: the pull stays staged, as when this task finds it paused, unless a
        // newer one arrived meanwhile, and a requested re-merge waits too.
        if (outcome.Paused == DataSyncPauseReason.AllPaused)
        {
            KeepForNextRun(linkId, pull, reMerge);
            _logger.LogInformation("Data sync applies nothing while it is paused");
            return false;
        }

        if (outcome is { Paused: null, End: DataSyncAutoSyncEnd.NotApplied })
        {
            // A link stopped or reset while the apply waited: nothing is recorded on it, and the pull goes.
            if (await _links.GetAsync(linkId, ct) is null or { State: DataSyncLinkState.Stopped }) return true;
            // The attempt ended, or the actor was unverified or changed under every try (§5.6): nothing was applied, so
            // nothing is recorded as synced and no first contact is consumed. The pull and a requested re-merge wait
            // for the next run, which the scheduler enqueues.
            KeepForNextRun(linkId, pull, reMerge);
            _logger.LogInformation("Data sync applied nothing from {Peer} now; it applies it on its next run",
                link.PeerNodeId);
            return false;
        }

        await AfterAppliedAsync(sp, linkId, outcome, ct);
        return true;
    }

    private async Task AfterAppliedAsync(IServiceProvider sp, int linkId, DataSyncAutoSyncOutcome outcome,
        CancellationToken ct)
    {
        await _links.AfterAutoSyncAsync(linkId, outcome, ct);
        if (outcome is { Paused: null, End: DataSyncAutoSyncEnd.Committed })
            _state.NoteMerged(linkId, (await sp.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct))?.LastSeq ?? 0);
    }

    /// <summary>Puts a pull back unless a newer one arrived meanwhile, and requests the re-merge again.</summary>
    private void KeepForNextRun(int linkId, DataSyncStagedPull? pull, bool reMerge)
    {
        if (pull is not null && _state.PeekPull(linkId) is null) _state.StagePull(linkId, pull);
        if (reMerge) _state.RequestReMerge(linkId);
    }

    private async Task<bool> IsVerifiedAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IDataSyncActorGuard>().IsVerified;
    }

    /// <summary>"Pause all", read fresh: it may have been pressed while this task waited behind the enhancer.</summary>
    private async Task<bool> IsAllPausedAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct))?.AllPaused ==
               true;
    }
}
