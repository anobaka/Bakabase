using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The body of <c>DataSyncApply</c> (§8.10.1): applies every staged pull, every link whose once flags act without a
/// pull and every link whose pending records wait for a re-merge, link by link, through
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
    private readonly IDataSyncStagedPullStore _stagedPulls;
    private readonly DataSyncLinkService _links;
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncClock _clock;
    private readonly ILogger<DataSyncApplyTask> _logger;

    public DataSyncApplyTask(IServiceScopeFactory scopes, IDataSyncTaskRegistry registry,
        IDataSyncStagedPullStore stagedPulls, DataSyncLinkService links, DataSyncRuntimeState state,
        IDataSyncClock clock, ILogger<DataSyncApplyTask> logger)
    {
        _scopes = scopes;
        _registry = registry;
        _stagedPulls = stagedPulls;
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

                var pull = _stagedPulls.Take(linkId);
                if (pull is null) done.Add(linkId);
                if (!await ApplyLinkAsync(linkId, pull, args))
                {
                    _logger.LogInformation("Data sync applies nothing while it is paused");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Links with a staged pull, then links whose pull-independent once flags or pending re-merge wait (not while
    /// backing off).
    /// </summary>
    private async Task<IReadOnlyList<int>> NextWorkAsync(HashSet<int> done, CancellationToken ct)
    {
        var work = _stagedPulls.LinksWaiting().ToList();
        var now = _clock.UtcNow;
        foreach (var link in await _links.GetLinksAsync(ct))
        {
            if (work.Contains(link.Id) || done.Contains(link.Id)) continue;
            if (link.State != DataSyncLinkState.Active) continue;
            if (link.GetOnceFlags().PullIndependent() == DataSyncMergeFlags.None &&
                !_state.IsReMergeRequested(link.Id)) continue;
            if (link.NextAttemptAtUtc is { } next && next > now && link.ConsecutiveFailures > 0) continue;
            work.Add(link.Id);
        }

        return work;
    }

    /// <returns>False when "Pause all" was found pressed once the runner held the gate: the task stops there.</returns>
    private async Task<bool> ApplyLinkAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var link = await _links.GetAsync(linkId, ct);
        if (link is null || link.State != DataSyncLinkState.Active || link.Mode == DataSyncLinkMode.Off)
        {
            // Paused, stopped, reset or back to a first contact since the fetch: the pull is dropped, and the next
            // fetch starts again from the cursor (§8.7).
            return true;
        }

        // Any apply of the link re-merges what its pending records wait for (§8.4), so it takes a requested re-merge.
        var reMerge = _state.TakeReMerge(linkId);
        var flags = link.GetOnceFlags();
        if (pull is null)
        {
            flags = flags.PullIndependent();
            if (flags == DataSyncMergeFlags.None && !reMerge) return true;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var context = await BuildContextAsync(sp, link, flags, ct);
        DataSyncAutoSyncOutcome outcome;
        try
        {
            outcome = await sp.GetRequiredService<IDataSyncApplyRunner>().RunAutoSyncAsync(context, pull, args);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A stopped apply rolled back its current chunk; put the pull back so the next run applies it without a
            // refetch, unless a newer one arrived meanwhile.
            if (pull is not null && _stagedPulls.Peek(linkId) is null) _stagedPulls.Put(linkId, pull);
            if (reMerge) _state.RequestReMerge(linkId);
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Data sync could not apply the pull from {Peer}", link.PeerNodeId);
            await _links.RecordFailureAsync(linkId, DataSyncLinkService.ApplyFailed, e.Message, null, ct);
            return true;
        }

        // The runner re-checks "Pause all" and the link once it holds the gate (§8.10.2). "Pause all" pressed while this
        // apply waited for the gate applied nothing: the pull stays staged, as when this task finds it paused, unless a
        // newer one arrived meanwhile, and a requested re-merge waits too.
        if (outcome.Paused == DataSyncPauseReason.AllPaused)
        {
            if (pull is not null && _stagedPulls.Peek(linkId) is null) _stagedPulls.Put(linkId, pull);
            if (reMerge) _state.RequestReMerge(linkId);
            return false;
        }

        // A link stopped or reset while the apply waited applied nothing either: nothing is recorded on it.
        if (outcome is { Paused: null, ApplyLogId: null, Applied: 0 } &&
            await _links.GetAsync(linkId, ct) is null or { State: DataSyncLinkState.Stopped })
        {
            return true;
        }

        await _links.AfterAutoSyncAsync(context, pull, outcome, IsFullReconciliation(sp, link, context, pull), ct);
        return true;
    }

    /// <summary>
    /// Whether the pull reconciled the whole link (§8.8): every kind the fetch half merges for the link — the kinds
    /// this build reads and the peer serves, less the kinds whose first contact is this device's review (§8.3) — came
    /// from 0. One superseded kind alone is not a full reconciliation of the link; and a kind still waiting for its
    /// review is never in a merge pull, so it must not keep the daily reconciliation from ever being recorded.
    /// </summary>
    private bool IsFullReconciliation(IServiceProvider sp, DataSyncLinkDbModel link, DataSyncLinkContext context,
        DataSyncStagedPull? pull)
    {
        if (pull is null) return false;
        var reader = sp.GetService<IDataSyncKindPageReader>();
        var served = _state.GetPeerFormVersions(context.LinkId);
        IReadOnlyCollection<string> reviewed = link.Initiator == DataSyncLinkInitiator.ThisDevice
            ? context.FirstContactKinds
            : [];
        var expected = context.Kinds
            .Where(k => (reader?.Supports(k) ?? true) && served.ContainsKey(k) && !reviewed.Contains(k))
            .ToList();
        return expected.Count > 0 &&
               expected.All(k => pull.Kinds.Any(s => s.Kind == k && s.FullReconciliation));
    }

    /// <summary>
    /// The merge's view of the link (§2.7). The actor fields are a snapshot taken before the gate: the runner re-reads
    /// them after <see cref="IDataSyncActorGuard.CheckAsync"/>, which may rotate the actor.
    /// </summary>
    private async Task<DataSyncLinkContext> BuildContextAsync(IServiceProvider sp, DataSyncLinkDbModel link,
        DataSyncMergeFlags flags, CancellationToken ct)
    {
        var local = await sp.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct);
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        if (local is not null)
        {
            foreach (var (actor, counter) in ReadRetired(local.RetiredActorsJson)) counters[actor] = counter;
            if (DataSyncActorId.IsValid(local.ActorId)) counters[local.ActorId] = local.ActorCounter;
        }

        var self = DataSyncActorId.IsValid(local?.ActorId) ? new DataSyncActorId(local!.ActorId) : default;
        return new DataSyncLinkContext(link.Id, link.PeerNodeId, link.PeerName, link.Mode, link.GetEffectiveMode(),
            link.GetKinds(), link.GetKindsAwaitingFirstContact(),
            sp.GetService<IDataSyncHostKind>()?.IsHeadless ?? false, self, counters, link.PeerActorId,
            _state.GetPeerFormVersions(link.Id), flags);
    }

    private static IReadOnlyDictionary<string, long> ReadRetired(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, long>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(json, DataSyncJson.Options) ??
                   new Dictionary<string, long>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, long>();
        }
    }

    private async Task<bool> IsVerifiedAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var guard = scope.ServiceProvider.GetService<IDataSyncActorGuard>();
        return guard is null || guard.IsVerified;
    }

    /// <summary>"Pause all", read fresh: it may have been pressed while this task waited behind the enhancer.</summary>
    private async Task<bool> IsAllPausedAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct))?.AllPaused ==
               true;
    }
}
