using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>A link as an action left it, or why the action was refused before anything changed.</summary>
/// <param name="RequestId">The datasync request the action sent, if any.</param>
public sealed record DataSyncLinkChange(DataSyncLinkDbModel? Link, string? RequestId, DataSyncProblem? Problem)
{
    public static DataSyncLinkChange Refused(DataSyncProblemCode code, string? detail = null,
        DataSyncLinkDbModel? link = null) => new(link, null, new DataSyncProblem(code, detail));
}

/// <summary>A link this device initiates (§8.1 "Create (initiator, this device)").</summary>
/// <param name="Mode">Follow or TwoWay; ignored for a copy once, whose mode stays Off.</param>
/// <param name="Kinds">Null or empty: every kind.</param>
public sealed record DataSyncLinkCreate(string? PeerNodeId, string? Address, string? Code, DataSyncLinkMode Mode,
    IReadOnlyList<string>? Kinds, bool CopyOnce = false);

/// <summary>How a change to a link row is written.</summary>
public enum DataSyncLinkWrite
{
    /// <summary>Nothing changed; nothing is written.</summary>
    None = 0,

    /// <summary>Scheduling and peer facts only: written, without <c>UpdatedAtUtc</c> and without a status event.</summary>
    Bookkeeping = 1,

    /// <summary>State, mode or kinds changed (§8.1): writes <c>UpdatedAtUtc</c> and publishes the status.</summary>
    Transition = 2,
}

/// <summary>
/// The link state machine (§8.1) and the resume actions (§8.7) [E]. One link per peer: a grant for a peer that
/// already has a link updates it. Every write goes through <see cref="MutateAsync"/>, which reads the row fresh and
/// writes it under one in-process lock, so the fetch cycle, the apply task, grant events and requests never write a
/// link from a stale copy.
/// </summary>
public sealed class DataSyncLinkService
{
    public const string AccessRejected = "AccessRejected";
    public const string AccessExpired = "AccessExpired";
    public const string AccessCancelled = "AccessCancelled";
    public const string ReadBackFailed = "ReadBackFailed";
    public const string ApplyFailed = "ApplyFailed";
    public const string FetchFailed = "FetchFailed";

    /// <summary><see cref="DataSyncLinkDbModel.PausedDetail"/> of B1b: the peer looks restored from a backup (§8.7).</summary>
    public const string RestoredDetail = "restored";

    private readonly IServiceScopeFactory _scopes;
    private readonly IDataSyncClock _clock;
    private readonly IDataSyncStagedPullStore _stagedPulls;
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncRuntimeObserver _observer;
    private readonly DataSyncLimits _limits;
    private readonly ILogger<DataSyncLinkService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataSyncLinkService(IServiceScopeFactory scopes, IDataSyncClock clock, IDataSyncStagedPullStore stagedPulls,
        DataSyncRuntimeState state, IDataSyncRuntimeObserver observer, DataSyncLimits limits,
        ILogger<DataSyncLinkService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _stagedPulls = stagedPulls;
        _state = state;
        _observer = observer;
        _limits = limits;
        _logger = logger;
    }

    // ---- reads -------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<DataSyncLinkDbModel>> GetLinksAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await Store(scope).GetLinksAsync(ct);
    }

    public async Task<DataSyncLinkDbModel?> GetAsync(int linkId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await Store(scope).GetLinkAsync(linkId, ct);
    }

    public async Task<DataSyncLinkDbModel?> GetByPeerAsync(string peerNodeId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await Store(scope).GetLinkByPeerAsync(peerNodeId, ct);
    }

    // ---- the write primitive -----------------------------------------------------------------------------------

    /// <summary>
    /// Reads the link fresh, lets <paramref name="mutate"/> change it and says how to write it, and writes it. Null
    /// when the link no longer exists.
    /// </summary>
    public async Task<DataSyncLinkDbModel?> MutateAsync(int linkId, Func<DataSyncLinkDbModel, DataSyncLinkWrite> mutate,
        CancellationToken ct)
    {
        DataSyncLinkDbModel? link;
        DataSyncLinkWrite write;
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = Store(scope);
            link = await store.GetLinkAsync(linkId, ct);
            if (link is null) return null;
            write = mutate(link);
            if (write == DataSyncLinkWrite.None) return link;
            if (write == DataSyncLinkWrite.Transition) link.UpdatedAtUtc = _clock.UtcNow;
            await store.UpdateLinkAsync(link, ct);
        }
        finally
        {
            _lock.Release();
        }

        if (write == DataSyncLinkWrite.Transition) await ObserveAsync(o => o.LinkChangedAsync(link, ct));
        return link;
    }

    // ---- create (initiator) ------------------------------------------------------------------------------------

    /// <summary>
    /// A link this device starts, or a copy once (§8.1): with an outbound datasync grant it goes straight to
    /// AwaitingReview; otherwise it sends a request (§7.2.2) and waits in AwaitingAccess. A two-way link also sends a
    /// request when the peer does not read this device yet, which mints a reciprocal code (§7.2.4).
    /// <paramref name="callerMayCreateAccess"/> false refuses exactly the calls that would send a request or mint a
    /// code (§7.1.5). With <paramref name="gate"/>, the caller holds the DataSyncGate and the request goes out with it
    /// released (§10.1).
    /// </summary>
    public async Task<DataSyncLinkChange> CreateAsync(DataSyncLinkCreate input, bool callerMayCreateAccess,
        CancellationToken ct, DataSyncGateHold? gate = null)
    {
        var mode = input.CopyOnce ? DataSyncLinkMode.Off : input.Mode;
        if (mode == DataSyncLinkMode.Off && !input.CopyOnce)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "mode");
        if (!TryNormalizeKinds(input.Kinds, out var kinds, out var unknownKind))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.UnknownKind, unknownKind);
        if (string.IsNullOrEmpty(input.PeerNodeId) && string.IsNullOrEmpty(input.Address))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "peer");

        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);

        var existing = input.PeerNodeId is { } knownId ? await GetByPeerAsync(knownId, ct) : null;
        if (existing is not null && !CanCopyOnceOnto(existing, input.CopyOnce))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkExists, null, existing);

        var hasAccess = input.PeerNodeId is not null && await grants.HasOutboundGrantAsync(input.PeerNodeId, ct);
        var peerMayReadUs = input.PeerNodeId is not null && await PeerMayReadUsAsync(grants, input.PeerNodeId, ct);
        var needsRequest = !hasAccess || (mode == DataSyncLinkMode.TwoWay && !peerMayReadUs);
        if (needsRequest && mode == DataSyncLinkMode.TwoWay && await RefuseTwoWayAsync(grants, ct) is { } twoWayProblem)
            return DataSyncLinkChange.Refused(twoWayProblem);
        if (needsRequest && !callerMayCreateAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice);

        var peerNodeId = input.PeerNodeId;
        string? peerName = null;
        string? requestId = null;
        var readBackDeclined = false;
        if (needsRequest)
        {
            var intent = mode == DataSyncLinkMode.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow;
            var sent = await gate.OutsideGateAsync(() => SendRequestAsync(grants,
                new DataSyncAccessRequestInput(input.PeerNodeId, input.Address, input.Code, intent), ct), ct);
            if (sent.Problem is not null) return DataSyncLinkChange.Refused(sent.Problem.Code, sent.Problem.Detail);
            var outcome = sent.Outcome!;
            peerNodeId = outcome.PeerNodeId;
            peerName = outcome.PeerName;
            hasAccess |= outcome.Outcome == "granted";
            requestId = outcome.Outcome == "awaitingApproval" ? outcome.RequestId : null;
            readBackDeclined = outcome.ReadBack == "declined";

            // Reached by address: only now is the peer known, and it may already have a link.
            existing ??= await GetByPeerAsync(peerNodeId, ct);
            if (existing is not null && !CanCopyOnceOnto(existing, input.CopyOnce))
                return new DataSyncLinkChange(existing, requestId,
                    new DataSyncProblem(DataSyncProblemCode.LinkExists, null));
        }

        peerName ??= await PeerNameAsync(grants, peerNodeId!, ct);
        var now = _clock.UtcNow;
        var state = hasAccess ? DataSyncLinkState.AwaitingReview : DataSyncLinkState.AwaitingAccess;

        if (existing is not null)
        {
            // A copy once onto a stopped link reuses its row: one link per peer (§8.1).
            var reused = await MutateAsync(existing.Id, link =>
            {
                link.Mode = DataSyncLinkMode.Off;
                link.State = state;
                link.Initiator = DataSyncLinkInitiator.ThisDevice;
                link.PausedReason = null;
                link.PausedDetail = null;
                link.SetKinds(kinds);
                link.PendingRequestId = requestId;
                link.PeerAddress ??= input.Address;
                link.ReadBackDeclined = readBackDeclined;
                ClearError(link);
                link.NextAttemptAtUtc = now;
                return DataSyncLinkWrite.Transition;
            }, ct);
            return new DataSyncLinkChange(reused, requestId, null);
        }

        var created = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId!,
            PeerName = peerName,
            PeerAddress = input.Address,
            Mode = mode,
            LastMode = mode == DataSyncLinkMode.Off ? DataSyncLinkMode.TwoWay : mode,
            State = state,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            PendingRequestId = requestId,
            ReadBackDeclined = readBackDeclined,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        created.SetKinds(kinds);
        created = await AddAsync(created, ct);
        return new DataSyncLinkChange(created, requestId, null);
    }

    // ---- mode and kinds ----------------------------------------------------------------------------------------

    /// <summary>
    /// Changes a link's mode and kinds (§8.1). Off stops it (bases and pending records kept). Turning a stopped link
    /// on resumes it with no new review unless kinds were added; kinds added to a link run a first contact for those
    /// kinds only. Two-way on a peer that does not read this device sends a request with a reciprocal code (§7.2.4),
    /// with <paramref name="gate"/> released around it.
    /// </summary>
    public async Task<DataSyncLinkChange> UpdateAsync(int linkId, DataSyncLinkMode? mode, IReadOnlyList<string>? kinds,
        bool callerMayCreateAccess, CancellationToken ct, DataSyncGateHold? gate = null)
    {
        var link = await GetAsync(linkId, ct);
        if (link is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        List<string>? newKinds = null;
        if (kinds is not null)
        {
            if (!TryNormalizeKinds(kinds, out var normalized, out var unknownKind))
                return DataSyncLinkChange.Refused(DataSyncProblemCode.UnknownKind, unknownKind, link);
            newKinds = normalized;
        }

        if (mode == DataSyncLinkMode.Off)
        {
            if (link.Mode == DataSyncLinkMode.Off && link.State == DataSyncLinkState.Stopped && newKinds is null)
                return new DataSyncLinkChange(link, null, null);
            return new DataSyncLinkChange(await StopAsync(link, newKinds, ct), null, null);
        }

        if (mode is not (null or DataSyncLinkMode.Follow or DataSyncLinkMode.TwoWay))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "mode", link);

        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        var target = mode ?? link.Mode;
        var turningOn = mode is not null && (link.Mode == DataSyncLinkMode.Off || link.State == DataSyncLinkState.Stopped);
        var hasAccess = !turningOn || await grants.HasOutboundGrantAsync(link.PeerNodeId, ct);
        var wantsReadBack = target == DataSyncLinkMode.TwoWay && mode == DataSyncLinkMode.TwoWay &&
                            (link.Mode != DataSyncLinkMode.TwoWay || turningOn) &&
                            !await PeerMayReadUsAsync(grants, link.PeerNodeId, ct);
        var needsRequest = mode is not null && (!hasAccess || wantsReadBack);

        if (needsRequest && target == DataSyncLinkMode.TwoWay && await RefuseTwoWayAsync(grants, ct) is { } problem)
            return DataSyncLinkChange.Refused(problem, null, link);
        if (needsRequest && !callerMayCreateAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice, null, link);

        DataSyncAccessRequestOutcome? outcome = null;
        if (needsRequest)
        {
            var intent = target == DataSyncLinkMode.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow;
            var sent = await gate.OutsideGateAsync(() => SendRequestAsync(grants,
                new DataSyncAccessRequestInput(link.PeerNodeId, null, null, intent), ct), ct);
            if (sent.Problem is not null) return new DataSyncLinkChange(link, null, sent.Problem);
            outcome = sent.Outcome!;
            hasAccess |= outcome.Outcome == "granted";
        }

        var now = _clock.UtcNow;
        var requestId = outcome?.Outcome == "awaitingApproval" ? outcome.RequestId : null;
        var updated = await MutateAsync(linkId, row =>
        {
            if (newKinds is not null) row.SetKinds(newKinds);
            if (mode is { } m)
            {
                row.Mode = m;
                row.LastMode = m;
                if (turningOn)
                {
                    row.PausedReason = null;
                    row.PausedDetail = null;
                    row.State = hasAccess ? row.GetResumeState() : DataSyncLinkState.AwaitingAccess;
                    row.PendingRequestId = hasAccess ? null : requestId;
                    ClearError(row);
                }
            }

            if (outcome is not null) row.ReadBackDeclined = outcome.ReadBack == "declined";
            row.NextAttemptAtUtc = now;
            return DataSyncLinkWrite.Transition;
        }, ct);
        return new DataSyncLinkChange(updated, requestId, null);
    }

    private async Task<DataSyncLinkDbModel?> StopAsync(DataSyncLinkDbModel link, IReadOnlyList<string>? kinds,
        CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await Store(scope).StopLinkAsync(link.Id, ct);
        }
        finally
        {
            _lock.Release();
        }

        _stagedPulls.Take(link.Id);
        DiscardReview(link.Id);
        return await MutateAsync(link.Id, row =>
        {
            if (row.Mode != DataSyncLinkMode.Off) row.LastMode = row.Mode;
            row.Mode = DataSyncLinkMode.Off;
            row.State = DataSyncLinkState.Stopped;
            row.PausedReason = null;
            row.PausedDetail = null;
            row.ReviewId = null;
            if (kinds is not null) row.SetKinds(kinds);
            row.NextAttemptAtUtc = null;
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// "Try again" for a link that waits for access (§7.2.4): a fresh request to the peer. The approver of a two-way
    /// link whose read-back failed asks only to read the peer back (Follow): the peer already reads this device.
    /// </summary>
    public async Task<DataSyncLinkChange> RequestAccessAgainAsync(int linkId, bool callerMayCreateAccess,
        CancellationToken ct, DataSyncGateHold? gate = null)
    {
        var link = await GetAsync(linkId, ct);
        if (link is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        if (link.State != DataSyncLinkState.AwaitingAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "notAwaitingAccess", link);
        var intent = link.Initiator == DataSyncLinkInitiator.ThisDevice && link.Mode == DataSyncLinkMode.TwoWay
            ? DataSyncRequestIntent.TwoWay
            : DataSyncRequestIntent.Follow;

        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        if (intent == DataSyncRequestIntent.TwoWay && await RefuseTwoWayAsync(grants, ct) is { } problem)
            return DataSyncLinkChange.Refused(problem, null, link);
        if (!callerMayCreateAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice, null, link);

        var sent = await gate.OutsideGateAsync(() => SendRequestAsync(grants,
            new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null, intent), ct), ct);
        if (sent.Problem is not null) return new DataSyncLinkChange(link, null, sent.Problem);
        var outcome = sent.Outcome!;
        if (outcome.Outcome == "granted")
        {
            await OnOutboundGrantedAsync(link.PeerNodeId, ct);
            return new DataSyncLinkChange(await GetAsync(linkId, ct), null, null);
        }

        var requestId = outcome.RequestId;
        var updated = await MutateAsync(linkId, row =>
        {
            row.PendingRequestId = requestId;
            ClearError(row);
            row.NextAttemptAtUtc = _clock.UtcNow;
            return DataSyncLinkWrite.Transition;
        }, ct);
        return new DataSyncLinkChange(updated, requestId, null);
    }

    // ---- pause and resume --------------------------------------------------------------------------------------

    /// <summary>The person pauses a link (<see cref="DataSyncPauseReason.ByUser"/>). A stopped or paused link is left as it is.</summary>
    public async Task<DataSyncLinkChange> PauseByUserAsync(int linkId, CancellationToken ct)
    {
        var link = await GetAsync(linkId, ct);
        if (link is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        if (link.State is DataSyncLinkState.Stopped or DataSyncLinkState.Paused)
            return new DataSyncLinkChange(link, null, null);
        return new DataSyncLinkChange(await PauseAsync(linkId, DataSyncPauseReason.ByUser, null, ct), null, null);
    }

    /// <summary>
    /// Pauses a link (§8.7): writes the reason and a short machine detail, never an error; drops the staged pull
    /// that tripped the breaker, so the next fetch after a resume starts again from the cursor.
    /// </summary>
    public async Task<DataSyncLinkDbModel?> PauseAsync(int linkId, DataSyncPauseReason reason, string? detail,
        CancellationToken ct)
    {
        _stagedPulls.Take(linkId);
        var paused = false;
        var link = await MutateAsync(linkId, row =>
        {
            // A link stopped meanwhile stays stopped: nothing pulls it anyway.
            if (row.State == DataSyncLinkState.Stopped) return DataSyncLinkWrite.None;
            row.State = DataSyncLinkState.Paused;
            row.PausedReason = reason;
            row.PausedDetail = detail;
            paused = true;
            return DataSyncLinkWrite.Transition;
        }, ct);
        _stagedPulls.Take(linkId);
        if (paused && link is not null) await ObserveAsync(o => o.LinkPausedAsync(link, ct));
        return link;
    }

    /// <summary>
    /// The resume actions of §8.7. Resume re-evaluates from the cursor (a restored peer: from 0); ApplyAsUsual and
    /// ReviewDeletions set a once flag for the next apply; AskAccessAgain asks a reset peer for access again and runs a
    /// new first contact against its new epoch; ThisDeviceWins and TakeTheirs enqueue the restore choice (§9.5);
    /// StartAnyway starts an approver that waits for its peer's review (§8.3). An action that does not apply to the
    /// link's state is refused with <see cref="DataSyncProblemCode.DecisionsInvalid"/> and changes nothing.
    /// </summary>
    public async Task<DataSyncLinkChange> ResumeAsync(int linkId, DataSyncResumeAction action,
        bool callerMayCreateAccess, CancellationToken ct, DataSyncGateHold? gate = null)
    {
        var link = await GetAsync(linkId, ct);
        if (link is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        var reason = link.State == DataSyncLinkState.Paused ? link.PausedReason : null;
        var restored = reason == DataSyncPauseReason.PeerReset && link.PausedDetail == RestoredDetail;

        switch (action)
        {
            case DataSyncResumeAction.Resume:
            {
                if (reason is null) return NotApplicable(link, action);
                if (reason is DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected)
                    return NotApplicable(link, action);
                if (reason == DataSyncPauseReason.PeerReset && !restored) return NotApplicable(link, action);
                if (reason == DataSyncPauseReason.TooManyDecisions)
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var open = await Store(scope).GetOpenItemsAsync(linkId, ct);
                    if (open.Count >= _limits.MaxOpenInboxItemsPerLink)
                        return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "tooManyDecisions",
                            link);
                }

                return new DataSyncLinkChange(await UnpauseAsync(linkId, restored, DataSyncMergeFlags.None, ct), null,
                    null);
            }
            case DataSyncResumeAction.ApplyAsUsual:
            case DataSyncResumeAction.ReviewDeletions:
            {
                if (reason is not (DataSyncPauseReason.MassDeletion or DataSyncPauseReason.KindEmptied))
                    return NotApplicable(link, action);
                var flag = action == DataSyncResumeAction.ApplyAsUsual
                    ? DataSyncMergeFlags.None with { SkipDeletionBreaker = true }
                    : DataSyncMergeFlags.None with { DeletionsAsItems = true };
                return new DataSyncLinkChange(await UnpauseAsync(linkId, false, flag, ct), null, null);
            }
            case DataSyncResumeAction.AskAccessAgain:
            {
                if (reason != DataSyncPauseReason.PeerReset || restored) return NotApplicable(link, action);
                return await AskAccessAgainAsync(link, callerMayCreateAccess, ct, gate);
            }
            case DataSyncResumeAction.ThisDeviceWins:
            case DataSyncResumeAction.TakeTheirs:
            {
                if (reason is not (DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected))
                    return NotApplicable(link, action);
                var choice = action == DataSyncResumeAction.ThisDeviceWins
                    ? DataSyncRestoreChoice.ThisDeviceWins
                    : DataSyncRestoreChoice.OthersWin;
                await using var scope = _scopes.CreateAsyncScope();
                var launcher = scope.ServiceProvider.GetRequiredService<DataSyncTaskLauncher>();
                var linkScope = reason == DataSyncPauseReason.LocalRestoreSuspected ? link.Id : (int?) null;
                if (await launcher.EnqueueRestoreAsync(choice, linkScope) is null)
                    return DataSyncLinkChange.Refused(DataSyncProblemCode.ApplyInProgress, null, link);
                return new DataSyncLinkChange(link, null, null);
            }
            case DataSyncResumeAction.StartAnyway:
            {
                if (link.State != DataSyncLinkState.WaitingForPeerReview) return NotApplicable(link, action);
                var started = await MutateAsync(linkId, row =>
                {
                    if (row.State != DataSyncLinkState.WaitingForPeerReview) return DataSyncLinkWrite.None;
                    row.State = DataSyncLinkState.Active;
                    row.NextAttemptAtUtc = _clock.UtcNow;
                    return DataSyncLinkWrite.Transition;
                }, ct);
                return new DataSyncLinkChange(started, null, null);
            }
            default:
                return NotApplicable(link, action);
        }
    }

    private async Task<DataSyncLinkDbModel?> UnpauseAsync(int linkId, bool resetCursors, DataSyncMergeFlags onceFlag,
        CancellationToken ct)
    {
        _stagedPulls.Take(linkId);
        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        var link = await GetAsync(linkId, ct);
        var waitsForAccess = link is { PendingRequestId: not null } &&
                             !await grants.HasOutboundGrantAsync(link.PeerNodeId, ct);
        return await MutateAsync(linkId, row =>
        {
            if (row.State != DataSyncLinkState.Paused) return DataSyncLinkWrite.None;
            row.State = waitsForAccess ? DataSyncLinkState.AwaitingAccess : row.GetResumeState();
            row.PausedReason = null;
            row.PausedDetail = null;
            if (resetCursors)
            {
                // B1b: the next pull is a full reconciliation from 0 (§5.6, §8.8).
                row.SetCursors(new Dictionary<string, long>());
                row.LastFullReconciliationAtUtc = null;
            }

            if (onceFlag != DataSyncMergeFlags.None)
            {
                var flags = row.GetOnceFlags();
                row.SetOnceFlags(new DataSyncMergeFlags(
                    DeletionsAsItems: flags.DeletionsAsItems || onceFlag.DeletionsAsItems,
                    SkipDeletionBreaker: flags.SkipDeletionBreaker || onceFlag.SkipDeletionBreaker,
                    SkipLargeChange: flags.SkipLargeChange || onceFlag.SkipLargeChange,
                    ChildDeletions: flags.ChildDeletions));
            }

            row.NextAttemptAtUtc = _clock.UtcNow;
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// B1's "Ask X for access again" (§8.7): the reset revoked every grant, so this sends a new request with the
    /// link's mode and sets <c>Initiator = ThisDevice</c>. The old epoch's bases are useless against the new one, so
    /// the link is reset (bases deleted, the link's items closed <c>LinkRemoved</c>) and recreated with the same peer,
    /// mode and kinds, waiting for access; once granted it runs a new first contact (§8.3).
    /// </summary>
    private async Task<DataSyncLinkChange> AskAccessAgainAsync(DataSyncLinkDbModel link, bool callerMayCreateAccess,
        CancellationToken ct, DataSyncGateHold? gate)
    {
        var mode = link.Mode == DataSyncLinkMode.Off ? link.LastMode : link.Mode;
        var intent = mode == DataSyncLinkMode.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow;
        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        if (intent == DataSyncRequestIntent.TwoWay && await RefuseTwoWayAsync(grants, ct) is { } problem)
            return DataSyncLinkChange.Refused(problem, null, link);
        if (!callerMayCreateAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice, null, link);

        var sent = await gate.OutsideGateAsync(() => SendRequestAsync(grants,
            new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null, intent), ct), ct);
        if (sent.Problem is not null) return new DataSyncLinkChange(link, null, sent.Problem);
        var outcome = sent.Outcome!;
        var granted = outcome.Outcome == "granted";
        var requestId = outcome.Outcome == "awaitingApproval" ? outcome.RequestId : null;

        var removed = await RemoveAsync(link.Id, ct);
        if (removed is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        var now = _clock.UtcNow;
        var fresh = new DataSyncLinkDbModel
        {
            PeerNodeId = link.PeerNodeId,
            PeerName = outcome.PeerName is { Length: > 0 } name ? name : link.PeerName,
            PeerAddress = link.PeerAddress,
            Mode = link.Mode == DataSyncLinkMode.Off ? DataSyncLinkMode.Off : mode,
            LastMode = link.LastMode,
            State = granted ? DataSyncLinkState.AwaitingReview : DataSyncLinkState.AwaitingAccess,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            KindsJson = link.KindsJson,
            PendingRequestId = requestId,
            ReadBackDeclined = outcome.ReadBack == "declined",
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        return new DataSyncLinkChange(await AddAsync(fresh, ct), requestId, null);
    }

    // ---- reset -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reset or Dismiss (§8.1): the link row, its bases and pending records are deleted, its items close
    /// <c>LinkRemoved</c> and its holds become local-only; definitions stay as they are.
    /// </summary>
    public async Task<DataSyncProblem?> ResetAsync(int linkId, CancellationToken ct) =>
        await RemoveAsync(linkId, ct) is null ? new DataSyncProblem(DataSyncProblemCode.LinkNotFound, null) : null;

    private async Task<DataSyncLinkDbModel?> RemoveAsync(int linkId, CancellationToken ct)
    {
        DataSyncLinkDbModel? removed;
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = Store(scope);
            removed = await store.GetLinkAsync(linkId, ct);
            if (removed is null) return null;
            await store.DeleteLinkAsync(linkId, ct);
        }
        finally
        {
            _lock.Release();
        }

        _stagedPulls.Take(linkId);
        DiscardReview(linkId);
        _state.ForgetLink(linkId);
        await ObserveAsync(o => o.LinkRemovedAsync(removed, ct));
        return removed;
    }

    // ---- scheduling --------------------------------------------------------------------------------------------

    /// <summary>"Sync now" (§8.2): the link, or every link the fetch cycle looks at, is due now.</summary>
    public async Task MarkDueAsync(int? linkId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        IEnumerable<int> ids = linkId is { } id
            ? new[] { id }
            : (await GetLinksAsync(ct)).Where(l => l.IsFetchable()).Select(l => l.Id).ToList();
        foreach (var target in ids)
        {
            await MutateAsync(target, row =>
            {
                row.NextAttemptAtUtc = now;
                return DataSyncLinkWrite.Bookkeeping;
            }, ct);
        }
    }

    /// <summary>
    /// Peers were discovered (<c>GetPeersAsync(discover: true)</c> saw them) or a federation session to them came
    /// online (§8.2): their links are due now.
    /// </summary>
    public async Task MarkPeersDueAsync(IEnumerable<string> peerNodeIds, CancellationToken ct)
    {
        var peers = peerNodeIds.ToHashSet(StringComparer.Ordinal);
        var now = _clock.UtcNow;
        foreach (var link in (await GetLinksAsync(ct)).Where(l => l.IsFetchable() && peers.Contains(l.PeerNodeId)))
        {
            await MutateAsync(link.Id, row =>
            {
                if (row.NextAttemptAtUtc is { } next && next <= now) return DataSyncLinkWrite.None;
                row.NextAttemptAtUtc = now;
                return DataSyncLinkWrite.Bookkeeping;
            }, ct);
        }
    }

    /// <summary>At the start, every link the fetch cycle looks at is due at <paramref name="dueAtUtc"/> (§8.2).</summary>
    public async Task ScheduleAllAsync(DateTime dueAtUtc, CancellationToken ct)
    {
        foreach (var link in (await GetLinksAsync(ct)).Where(l => l.IsFetchable()))
        {
            await MutateAsync(link.Id, row =>
            {
                row.NextAttemptAtUtc = dueAtUtc;
                return DataSyncLinkWrite.Bookkeeping;
            }, ct);
        }
    }

    /// <summary>The global switch (§8.7 "Other pauses"): kept in the local state row.</summary>
    public async Task<DataSyncProblem?> SetAllPausedAsync(bool paused, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = Store(scope);
            var local = await store.GetLocalStateAsync(ct);
            if (local is null) return new DataSyncProblem(DataSyncProblemCode.Busy, "notInitialized");
            if (local.AllPaused == paused) return null;
            local.AllPaused = paused;
            local.UpdatedAtUtc = _clock.UtcNow;
            await store.SaveLocalStateAsync(local, ct);
        }
        finally
        {
            _lock.Release();
        }

        if (!paused) await MarkDueAsync(null, ct);
        return null;
    }

    /// <summary>
    /// "Share new definitions automatically" off (§3.6): Refresh then inserts a new local definition as LocalOnly.
    /// Kept in the local state row; the caller holds the gate, so no Refresh or apply rewrites the row meanwhile.
    /// </summary>
    public async Task<DataSyncProblem?> SetNewDefinitionsStayLocalAsync(bool stayLocal, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = Store(scope);
            var local = await store.GetLocalStateAsync(ct);
            if (local is null) return new DataSyncProblem(DataSyncProblemCode.Busy, "notInitialized");
            if (local.NewDefinitionsStayLocal == stayLocal) return null;
            local.NewDefinitionsStayLocal = stayLocal;
            local.UpdatedAtUtc = _clock.UtcNow;
            await store.SaveLocalStateAsync(local, ct);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---- grant events ------------------------------------------------------------------------------------------

    /// <summary>
    /// Our request or code was granted (§8.2: raised within 5 s by the claim loop). A link waiting for access goes on
    /// to its first contact: AwaitingReview when this device started it, WaitingForPeerReview when the peer did
    /// (§8.1). Any other link is due now, which also brings a link out of AccessRevoked at its next head.
    /// </summary>
    public async Task OnOutboundGrantedAsync(string peerNodeId, CancellationToken ct)
    {
        var link = await GetByPeerAsync(peerNodeId, ct);
        if (link is null) return;
        var now = _clock.UtcNow;
        await MutateAsync(link.Id, row =>
        {
            row.NextAttemptAtUtc = now;
            if (row.State != DataSyncLinkState.AwaitingAccess) return DataSyncLinkWrite.Bookkeeping;
            row.State = row.GetResumeState();
            row.PendingRequestId = null;
            ClearError(row);
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// This device granted a peer datasync access (§7.2.3, §7.2.4, §8.1). Only a two-way grant with read-back makes a
    /// link: a new one is created as the approver's (<c>TwoWay</c>, <c>Initiator = Peer</c>), in
    /// WaitingForPeerReview, or in AwaitingAccess with the failure recorded when the read-back did not give this
    /// device access (N14). A peer that already has a link updates it instead: two-way, and a stopped link goes
    /// Active or WaitingForPeerReview by its first contact; any other state is kept, and so are bases and pending
    /// records. Every existing link is due now, so its next head checks the counterpart.
    /// </summary>
    /// <param name="readBackAttempted">The grant was two-way and this device tried to read the peer back.</param>
    /// <param name="readBackGranted">That read-back gave this device a datasync grant for the peer.</param>
    public async Task<DataSyncLinkDbModel?> OnInboundGrantedAsync(string peerNodeId, DataSyncRequestIntent intent,
        bool readBackAttempted, bool readBackGranted, string? readBackError, string? peerName,
        IReadOnlyList<string>? kinds, CancellationToken ct)
    {
        var twoWay = intent == DataSyncRequestIntent.TwoWay && readBackAttempted;
        var now = _clock.UtcNow;
        var existing = await GetByPeerAsync(peerNodeId, ct);
        if (existing is not null)
        {
            return await MutateAsync(existing.Id, row =>
            {
                row.NextAttemptAtUtc = now;
                if (!twoWay) return DataSyncLinkWrite.Bookkeeping;
                row.Mode = DataSyncLinkMode.TwoWay;
                row.LastMode = DataSyncLinkMode.TwoWay;
                row.ReadBackDeclined = false;
                if (row.State == DataSyncLinkState.Stopped)
                {
                    // The peer asked for two-way, so its review comes first unless this link's first contact is done.
                    if (row.FirstContactCompletedAtUtc is null) row.Initiator = DataSyncLinkInitiator.Peer;
                    row.State = readBackGranted ? row.GetResumeState() : DataSyncLinkState.AwaitingAccess;
                    ClearError(row);
                }

                if (!readBackGranted && readBackError is not null)
                {
                    row.LastErrorCode = ReadBackFailed;
                    row.LastErrorDetail = readBackError;
                }

                return DataSyncLinkWrite.Transition;
            }, ct);
        }

        if (!twoWay) return null;
        if (!TryNormalizeKinds(kinds, out var normalized, out _)) normalized = DataSyncKindIds.All.ToList();
        if (string.IsNullOrEmpty(peerName))
        {
            await using var scope = _scopes.CreateAsyncScope();
            peerName = await PeerNameAsync(Grants(scope), peerNodeId, ct);
        }

        var created = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId,
            PeerName = peerName,
            Mode = DataSyncLinkMode.TwoWay,
            LastMode = DataSyncLinkMode.TwoWay,
            State = readBackGranted ? DataSyncLinkState.WaitingForPeerReview : DataSyncLinkState.AwaitingAccess,
            Initiator = DataSyncLinkInitiator.Peer,
            // A read-back still in flight is not a failure: the OutboundGranted event follows it (§8.2).
            LastErrorCode = readBackGranted || readBackError is null ? null : ReadBackFailed,
            LastErrorDetail = readBackGranted ? null : readBackError,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        created.SetKinds(normalized);
        return await AddAsync(created, ct);
    }

    /// <summary>
    /// This device's request for a link it started ended without access (§8.1): the link stops and stays on the map
    /// with Dismiss (M5: nothing the user filed vanishes silently).
    /// </summary>
    public Task<DataSyncLinkDbModel?> OnRequestEndedAsync(int linkId, string errorCode, CancellationToken ct) =>
        MutateAsync(linkId, row =>
        {
            if (row.State != DataSyncLinkState.AwaitingAccess) return DataSyncLinkWrite.None;
            if (row.Mode != DataSyncLinkMode.Off) row.LastMode = row.Mode;
            row.Mode = DataSyncLinkMode.Off;
            row.State = DataSyncLinkState.Stopped;
            row.LastErrorCode = errorCode;
            row.LastErrorDetail = null;
            row.NextAttemptAtUtc = null;
            return DataSyncLinkWrite.Transition;
        }, ct);

    // ---- after the apply task ----------------------------------------------------------------------------------

    /// <summary>
    /// After <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/> returned: records the kinds whose first contact this
    /// pull completed (the approver's first pull, or kinds added to a link), a full reconciliation, and the once flags
    /// the apply consumed; tells the observer what was applied and a pause the runner wrote.
    /// </summary>
    /// <param name="fullReconciliation">The pull carried every kind of the link from 0 (§8.8).</param>
    public async Task<DataSyncLinkDbModel?> AfterAutoSyncAsync(DataSyncLinkContext context, DataSyncStagedPull? pull,
        DataSyncAutoSyncOutcome outcome, bool fullReconciliation, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var firstSync = false;
        var link = await MutateAsync(context.LinkId, row =>
        {
            var write = DataSyncLinkWrite.None;
            if (context.LinkFlags != DataSyncMergeFlags.None)
            {
                var left = row.GetOnceFlags().Without(context.LinkFlags);
                if (left != row.GetOnceFlags())
                {
                    row.SetOnceFlags(left);
                    write = DataSyncLinkWrite.Bookkeeping;
                }
            }

            if (outcome.Paused is not null || pull is null) return write;

            var pulled = pull.Kinds.Select(k => k.Kind).ToHashSet(StringComparer.Ordinal);
            var completed = context.FirstContactKinds.Where(pulled.Contains).ToList();
            if (completed.Count > 0)
            {
                row.SetFirstContactKinds(row.GetFirstContactKinds().Union(completed));
                if (row.FirstContactCompletedAtUtc is null)
                {
                    row.FirstContactCompletedAtUtc = now;
                    firstSync = true;
                }

                write = DataSyncLinkWrite.Transition;
            }

            if (fullReconciliation)
            {
                row.LastFullReconciliationAtUtc = now;
                write = write == DataSyncLinkWrite.None ? DataSyncLinkWrite.Bookkeeping : write;
            }

            row.LastSyncedAtUtc = now;
            row.ConsecutiveFailures = 0;
            if (row.LastErrorCode == ApplyFailed) ClearError(row);
            return write == DataSyncLinkWrite.None ? DataSyncLinkWrite.Bookkeeping : write;
        }, ct);

        if (link is null) return null;
        if (outcome.Paused is not null)
        {
            _stagedPulls.Take(link.Id);
            await ObserveAsync(o => o.LinkPausedAsync(link, ct));
        }
        else
        {
            await ObserveAsync(o => o.AutoSyncAppliedAsync(link, outcome, firstSync, ct));
        }

        return link;
    }

    /// <summary>An apply of this link failed outside the runner's own handling: an error and a backoff.</summary>
    public Task<DataSyncLinkDbModel?> RecordFailureAsync(int linkId, string code, string? detail,
        int? retryAfterSeconds, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return MutateAsync(linkId, row =>
        {
            row.ConsecutiveFailures++;
            row.LastErrorCode = code;
            row.LastErrorDetail = Truncate(detail);
            row.LastAttemptAtUtc = now;
            row.NextAttemptAtUtc = now + DataSyncSchedule.Backoff(row.ConsecutiveFailures, retryAfterSeconds);
            return DataSyncLinkWrite.Bookkeeping;
        }, ct);
    }

    /// <summary>After a restore choice ran (§9.5): the links it resumed are due now.</summary>
    public async Task AfterRestoreAsync(int? linkId, CancellationToken ct)
    {
        await MarkDueAsync(linkId, ct);
        foreach (var link in await GetLinksAsync(ct))
        {
            if (linkId is null || link.Id == linkId) await ObserveAsync(o => o.LinkChangedAsync(link, ct));
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<DataSyncLinkDbModel> AddAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        DataSyncLinkDbModel added;
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            added = await Store(scope).AddLinkAsync(link, ct);
        }
        finally
        {
            _lock.Release();
        }

        await ObserveAsync(o => o.LinkChangedAsync(added, ct));
        return added;
    }

    private static bool CanCopyOnceOnto(DataSyncLinkDbModel existing, bool copyOnce) =>
        copyOnce && existing.State == DataSyncLinkState.Stopped;

    private DataSyncLinkChange NotApplicable(DataSyncLinkDbModel link, DataSyncResumeAction action) =>
        DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid,
            "notApplicable:" + System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(action.ToString()), link);

    private static void ClearError(DataSyncLinkDbModel link)
    {
        link.LastErrorCode = null;
        link.LastErrorDetail = null;
        link.ConsecutiveFailures = 0;
    }

    internal static string? Truncate(string? detail) =>
        detail is { Length: > 512 } ? detail[..512] : detail;

    private static bool TryNormalizeKinds(IReadOnlyList<string>? kinds, out List<string> normalized,
        out string? unknownKind)
    {
        unknownKind = kinds?.FirstOrDefault(k => !DataSyncKindIds.All.Contains(k));
        normalized = kinds is not { Count: > 0 }
            ? DataSyncKindIds.All.ToList()
            : DataSyncKindIds.All.Where(kinds.Contains).ToList();
        return unknownKind is null;
    }

    private static async Task<bool> PeerMayReadUsAsync(IDataSyncGrantService grants, string peerNodeId,
        CancellationToken ct) =>
        (await grants.GetGrantsAsync(ct)).Any(g => string.Equals(g.NodeId, peerNodeId, StringComparison.Ordinal));

    private static async Task<string> PeerNameAsync(IDataSyncGrantService grants, string peerNodeId,
        CancellationToken ct)
    {
        var candidate = (await grants.GetPeersAsync(false, ct))
            .FirstOrDefault(p => string.Equals(p.NodeId, peerNodeId, StringComparison.Ordinal));
        return candidate?.Name is { Length: > 0 } name ? name : peerNodeId;
    }

    /// <summary>
    /// A two-way link lets the peer read this device, which needs definitions sharing on and remote access not
    /// Disabled (§7.2.4 step 1). The UI offers to turn both on first.
    /// </summary>
    private static async Task<DataSyncProblemCode?> RefuseTwoWayAsync(IDataSyncGrantService grants,
        CancellationToken ct)
    {
        if (!await grants.IsSharingEnabledAsync(ct)) return DataSyncProblemCode.SharingOff;
        if (await grants.GetRemoteAccessModeAsync(ct) == RemoteAccessMode.Disabled)
            return DataSyncProblemCode.RemoteAccessOff;
        return null;
    }

    private sealed record SentRequest(DataSyncAccessRequestOutcome? Outcome, DataSyncProblem? Problem);

    private static async Task<SentRequest> SendRequestAsync(IDataSyncGrantService grants,
        DataSyncAccessRequestInput input, CancellationToken ct)
    {
        try
        {
            var outcome = await grants.RequestAccessAsync(input, ct);
            if (outcome.Outcome == "rejected")
                return new SentRequest(null, new DataSyncProblem(
                    input.Code is null ? DataSyncProblemCode.AccessMissing : DataSyncProblemCode.InvitationInvalid,
                    "rejected"));
            return new SentRequest(outcome, null);
        }
        catch (DataSyncPeerException e)
        {
            return new SentRequest(null, new DataSyncProblem(ProblemOf(e.Code), e.Code.ToCode()));
        }
        catch (DataSyncProblemException e)
        {
            // This device refused on the way (sharing or remote access off, a wrong code): the answer as it is.
            return new SentRequest(null, e.Problem);
        }
    }

    /// <summary>How a peer failure reads to the person who asked for the action (§10.1).</summary>
    public static DataSyncProblemCode ProblemOf(DataSyncPeerErrorCode code) => code switch
    {
        DataSyncPeerErrorCode.AccessMissing => DataSyncProblemCode.AccessMissing,
        DataSyncPeerErrorCode.AccessRevoked => DataSyncProblemCode.AccessRevoked,
        DataSyncPeerErrorCode.PeerSharingOff => DataSyncProblemCode.PeerSharingOff,
        DataSyncPeerErrorCode.PeerTooOld => DataSyncProblemCode.PeerTooOld,
        DataSyncPeerErrorCode.ThisTooOld => DataSyncProblemCode.ThisTooOld,
        DataSyncPeerErrorCode.PeerReset or DataSyncPeerErrorCode.IdentityConflict => DataSyncProblemCode.PeerReset,
        DataSyncPeerErrorCode.Busy => DataSyncProblemCode.Busy,
        _ => DataSyncProblemCode.PeerUnreachable,
    };

    private void DiscardReview(int linkId)
    {
        using var scope = _scopes.CreateScope();
        var reviews = scope.ServiceProvider.GetService<IDataSyncReviewStore>();
        if (reviews?.GetForLink(linkId) is { } review) reviews.Discard(review.ReviewId);
    }

    private async Task ObserveAsync(Func<IDataSyncRuntimeObserver, Task> call)
    {
        try
        {
            await call(_observer);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "A data sync observer failed");
        }
    }

    private static IDataSyncStore Store(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IDataSyncStore>();

    private static IDataSyncGrantService Grants(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IDataSyncGrantService>();
}
