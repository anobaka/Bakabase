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
/// The body of <c>DataSyncApply</c> (§8.10.1): applies every staged pull and every link whose once flags act without
/// a pull, link by link, through <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/>, and loops until nothing waits.
/// It waits while the actor is unverified (§5.6). A link that fails is recorded and backs off; it is not tried again
/// in the same run, so a failing link can never keep the task spinning.
/// </summary>
public sealed class DataSyncApplyTask
{
    private static readonly TimeSpan VerificationPoll = TimeSpan.FromSeconds(1);

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
            await WaitUntilVerifiedAsync(args);
            if (!_registry.ShouldRun(attempt.TaskId, attempt.AttemptId)) return;

            var work = await NextWorkAsync(done, ct);
            if (work.Count == 0) return;
            foreach (var linkId in work)
            {
                await args.YieldAsync();
                if (!_registry.ShouldRun(attempt.TaskId, attempt.AttemptId)) return;
                var pull = _stagedPulls.Take(linkId);
                if (pull is null) done.Add(linkId);
                await ApplyLinkAsync(linkId, pull, args);
            }
        }
    }

    /// <summary>Links with a staged pull, then links whose pull-independent once flags wait (not while backing off).</summary>
    private async Task<IReadOnlyList<int>> NextWorkAsync(HashSet<int> done, CancellationToken ct)
    {
        var work = _stagedPulls.LinksWaiting().ToList();
        var now = _clock.UtcNow;
        foreach (var link in await _links.GetLinksAsync(ct))
        {
            if (work.Contains(link.Id) || done.Contains(link.Id)) continue;
            if (link.State != DataSyncLinkState.Active) continue;
            if (link.GetOnceFlags().PullIndependent() == DataSyncMergeFlags.None) continue;
            if (link.NextAttemptAtUtc is { } next && next > now && link.ConsecutiveFailures > 0) continue;
            work.Add(link.Id);
        }

        return work;
    }

    private async Task ApplyLinkAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var link = await _links.GetAsync(linkId, ct);
        if (link is null || link.State != DataSyncLinkState.Active || link.Mode == DataSyncLinkMode.Off)
        {
            // Paused, stopped, reset or back to a first contact since the fetch: the pull is dropped, and the next
            // fetch starts again from the cursor (§8.7).
            return;
        }

        var flags = link.GetOnceFlags();
        if (pull is null)
        {
            flags = flags.PullIndependent();
            if (flags == DataSyncMergeFlags.None) return;
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
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Data sync could not apply the pull from {Peer}", link.PeerNodeId);
            await _links.RecordFailureAsync(linkId, DataSyncLinkService.ApplyFailed, e.Message, null, ct);
            return;
        }

        await _links.AfterAutoSyncAsync(context, pull, outcome, IsFullReconciliation(sp, context, pull), ct);
    }

    /// <summary>
    /// Whether the pull reconciled the whole link (§8.8): every kind of the link that this build reads and the peer
    /// serves came from 0. One superseded kind alone is not a full reconciliation of the link.
    /// </summary>
    private bool IsFullReconciliation(IServiceProvider sp, DataSyncLinkContext context, DataSyncStagedPull? pull)
    {
        if (pull is null) return false;
        var reader = sp.GetService<IDataSyncKindPageReader>();
        var served = _state.GetPeerFormVersions(context.LinkId);
        var expected = context.Kinds.Where(k => (reader?.Supports(k) ?? true) && served.ContainsKey(k)).ToList();
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

    private async Task WaitUntilVerifiedAsync(BTaskArgs args)
    {
        while (true)
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                var guard = scope.ServiceProvider.GetService<IDataSyncActorGuard>();
                if (guard is null || guard.IsVerified) return;
            }

            await Task.Delay(VerificationPoll, args.CancellationToken);
            await args.YieldAsync();
        }
    }
}
