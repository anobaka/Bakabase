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

/// <summary>How a change to a link row is written. Internal: no API answers it, so it stays out of the SDK constants.</summary>
internal enum DataSyncLinkWrite
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
/// already has a link updates it, decided in the same write as the insert. Every write reads the row fresh and writes
/// it under one in-process lock <b>and</b> inside one write transaction (<see cref="IDataSyncRowTransactions"/>,
/// <c>BEGIN IMMEDIATE</c>): the lock orders this service's own writers (the fetch cycle, grant events, requests, the
/// apply task's bookkeeping); the transaction orders them with every other writer of the row — the apply runner
/// commits cursors, first contact, pauses and consumed flags in its own transactions (§8.10.2), which cannot take the
/// lock. A read-modify-write here therefore never starts from a row another transaction is about to change, and never
/// puts an older row back over what it committed.
/// </summary>
/// <remarks>
/// <b>What this asks of the apply runner [C].</b> Most writers here take no DataSyncGate — the fetch half, grant
/// events, withdrawing a request, and approving one (whose link row §10.1 gates; here it is not, so an approval never
/// waits behind an apply) — so the gate an apply holds does not keep them from a link row. What orders them with the
/// runner is the database alone, which holds only if the runner reads a link row inside the same write transaction
/// that writes it back (<c>BEGIN IMMEDIATE</c>), and never writes back a row it read before that transaction began.
/// </remarks>
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
    private readonly IDataSyncRowTransactions _transactions;
    private readonly ILogger<DataSyncLinkService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataSyncLinkService(IServiceScopeFactory scopes, IDataSyncClock clock, IDataSyncStagedPullStore stagedPulls,
        DataSyncRuntimeState state, IDataSyncRuntimeObserver observer, DataSyncLimits limits,
        IDataSyncRowTransactions transactions, ILogger<DataSyncLinkService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _stagedPulls = stagedPulls;
        _state = state;
        _observer = observer;
        _limits = limits;
        _transactions = transactions;
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
    /// Reads the link fresh, lets <paramref name="mutate"/> change it and says how to write it, and writes it, all in
    /// one write transaction. Null when the link no longer exists. <paramref name="mutate"/> runs under the lock and
    /// inside the transaction, so it only changes the row it is given.
    /// </summary>
    internal async Task<DataSyncLinkDbModel?> MutateAsync(int linkId,
        Func<DataSyncLinkDbModel, DataSyncLinkWrite> mutate, CancellationToken ct)
    {
        var (link, write) = await WriteAsync(async store =>
        {
            var row = await store.GetLinkAsync(linkId, ct);
            if (row is null) return (null, DataSyncLinkWrite.None);
            var how = mutate(row);
            await WriteRowAsync(store, row, how, ct);
            return ((DataSyncLinkDbModel?) row, how);
        }, ct);

        if (link is not null && write == DataSyncLinkWrite.Transition)
            await ObserveAsync(o => o.LinkChangedAsync(link, ct));
        return link;
    }

    /// <summary>
    /// Runs <paramref name="write"/> on a fresh scope's store, under the lock and inside one write transaction, and
    /// commits it. Every read and write of link rows and the local state row here goes through it.
    /// </summary>
    private async Task<T> WriteAsync<T>(Func<IDataSyncStore, Task<T>> write, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await using var transaction = await _transactions.BeginAsync(scope.ServiceProvider, ct);
            var result = await write(Store(scope));
            await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WriteRowAsync(IDataSyncStore store, DataSyncLinkDbModel row, DataSyncLinkWrite how,
        CancellationToken ct)
    {
        if (how == DataSyncLinkWrite.None) return;
        if (how == DataSyncLinkWrite.Transition) row.UpdatedAtUtc = _clock.UtcNow;
        await store.UpdateLinkAsync(row, ct);
    }

    /// <summary>
    /// Inserts <paramref name="link"/> unless its peer already has a link (one link per peer, §8.1, §4.2), checked in
    /// the same write as the insert, so two callers making a link for one peer at once — the approval and the queued
    /// grant event — never both insert. An existing link is changed by <paramref name="updateExisting"/> instead, or
    /// returned as it is.
    /// </summary>
    private async Task<(DataSyncLinkDbModel Link, bool Added)> AddUniqueAsync(DataSyncLinkDbModel link,
        Func<DataSyncLinkDbModel, DataSyncLinkWrite>? updateExisting, CancellationToken ct)
    {
        var (row, added, how) = await WriteAsync(async store =>
        {
            var existing = await store.GetLinkByPeerAsync(link.PeerNodeId, ct);
            if (existing is null) return (await store.AddLinkAsync(link, ct), true, DataSyncLinkWrite.Transition);
            var write = updateExisting?.Invoke(existing) ?? DataSyncLinkWrite.None;
            await WriteRowAsync(store, existing, write, ct);
            return (existing, false, write);
        }, ct);

        if (how == DataSyncLinkWrite.Transition) await ObserveAsync(o => o.LinkChangedAsync(row, ct));
        return (row, added);
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
            // A copy once onto a stopped link reuses its row: one link per peer (§8.1). Its review is a first contact
            // again, so the row's earlier one no longer counts: otherwise a pause and resume, or a peer error, would
            // take the row back to Stopped and the copy once would silently end. Bases and cursors stay.
            var moved = false;
            var reused = await MutateAsync(existing.Id, link =>
            {
                // Turned on or reset meanwhile: it is a link again, not a stopped row.
                if (link.State != DataSyncLinkState.Stopped)
                {
                    moved = true;
                    return DataSyncLinkWrite.None;
                }

                link.Mode = DataSyncLinkMode.Off;
                link.State = state;
                link.Initiator = DataSyncLinkInitiator.ThisDevice;
                link.PausedReason = null;
                link.PausedDetail = null;
                link.SetKinds(kinds);
                link.FirstContactCompletedAtUtc = null;
                link.FirstContactKindsJson = null;
                link.ReviewId = null;
                link.PendingRequestId = requestId;
                link.PeerAddress ??= input.Address;
                link.ReadBackDeclined = readBackDeclined;
                ClearError(link);
                link.NextAttemptAtUtc = now;
                return DataSyncLinkWrite.Transition;
            }, ct);
            if (reused is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
            return moved
                ? new DataSyncLinkChange(reused, requestId, new DataSyncProblem(DataSyncProblemCode.LinkExists, null))
                : new DataSyncLinkChange(reused, requestId, null);
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
        var (row, added) = await AddUniqueAsync(created, null, ct);
        return added
            ? new DataSyncLinkChange(row, requestId, null)
            : new DataSyncLinkChange(row, requestId, new DataSyncProblem(DataSyncProblemCode.LinkExists, null));
    }

    // ---- mode and kinds ----------------------------------------------------------------------------------------

    /// <summary>
    /// Changes a link's mode and kinds (§8.1). Off stops it (bases and pending records kept). Turning a stopped link
    /// on resumes it with no new review unless kinds were added, and its next pull is a full reconciliation, which
    /// re-merges every pending record (§8.4 condition 4): the items its stop closed come back even when the peer has
    /// nothing new. Kinds added to a link run a first contact for those kinds only. Two-way on a peer that does not
    /// read this device sends a request with a reciprocal code (§7.2.4), with <paramref name="gate"/> released around
    /// it.
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
            return new DataSyncLinkChange(await StopAsync(link.Id, newKinds, ct), null, null);
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
                    if (row is { State: DataSyncLinkState.Stopped, FirstContactCompletedAtUtc: not null })
                        row.MarkFullReconciliationDue(now);
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

    /// <summary>
    /// Off (§8.1): the store stops the link (its items close <c>LinkStopped</c>, its holds become local-only; bases and
    /// pending records stay) and the row records it, in one write.
    /// </summary>
    private async Task<DataSyncLinkDbModel?> StopAsync(int linkId, IReadOnlyList<string>? kinds, CancellationToken ct)
    {
        var stopped = await WriteAsync(async store =>
        {
            var row = await store.GetLinkAsync(linkId, ct);
            if (row is null) return null;
            var lastMode = row.Mode != DataSyncLinkMode.Off ? row.Mode : row.LastMode;
            await store.StopLinkAsync(linkId, ct);
            row = await store.GetLinkAsync(linkId, ct);
            if (row is null) return null;
            row.LastMode = lastMode;
            row.Mode = DataSyncLinkMode.Off;
            row.State = DataSyncLinkState.Stopped;
            row.PausedReason = null;
            row.PausedDetail = null;
            row.ReviewId = null;
            if (kinds is not null) row.SetKinds(kinds);
            row.NextAttemptAtUtc = null;
            await WriteRowAsync(store, row, DataSyncLinkWrite.Transition, ct);
            return row;
        }, ct);

        _stagedPulls.Take(linkId);
        DiscardReview(linkId);
        if (stopped is not null) await ObserveAsync(o => o.LinkChangedAsync(stopped, ct));
        return stopped;
    }

    /// <summary>
    /// "Try again" for a link that waits for access (§7.2.4): a fresh request to the peer. The approver of a two-way
    /// link whose read-back failed (N14) asks only to read the peer back (Follow): the peer already reads this device.
    /// Reached through <see cref="DataSyncResumeAction.AskAccessAgain"/> on a link in AwaitingAccess.
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

    /// <summary>
    /// "[Ask {name} to keep in step]" (§7.2.3): the peer granted this two-way link's access but declined to read this
    /// device back (<c>ReadBackDeclined</c>), so this sends an ordinary two-way request with a reciprocal offer
    /// (§7.2.4). A peer that reads this device by now needs nothing: the note is cleared instead.
    /// </summary>
    private async Task<DataSyncLinkChange> AskToKeepInStepAsync(DataSyncLinkDbModel link, bool callerMayCreateAccess,
        CancellationToken ct, DataSyncGateHold? gate)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        if (await PeerMayReadUsAsync(grants, link.PeerNodeId, ct))
        {
            var cleared = await MutateAsync(link.Id, row =>
            {
                if (!row.ReadBackDeclined) return DataSyncLinkWrite.None;
                row.ReadBackDeclined = false;
                return DataSyncLinkWrite.Transition;
            }, ct);
            return new DataSyncLinkChange(cleared, null, null);
        }

        if (await RefuseTwoWayAsync(grants, ct) is { } problem) return DataSyncLinkChange.Refused(problem, null, link);
        if (!callerMayCreateAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice, null, link);

        var sent = await gate.OutsideGateAsync(() => SendRequestAsync(grants,
            new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null, DataSyncRequestIntent.TwoWay), ct),
            ct);
        if (sent.Problem is not null) return new DataSyncLinkChange(link, null, sent.Problem);
        var outcome = sent.Outcome!;
        var requestId = outcome.Outcome == "awaitingApproval" ? outcome.RequestId : null;
        var updated = await MutateAsync(link.Id, row =>
        {
            row.ReadBackDeclined = outcome.ReadBack == "declined";
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
    /// new first contact against its new epoch, and is also "Try again" for a link waiting for access (§7.2.4, N14)
    /// and "[Ask {name} to keep in step]" for a two-way link the peer does not read back (§7.2.3); ThisDeviceWins and
    /// TakeTheirs enqueue the restore choice (§9.5); StartAnyway starts an approver that has waited
    /// <see cref="DataSyncSchedule.StartAnywayAfter"/> for its peer's review (§8.3). An action that does not apply to
    /// the link's state is refused with <see cref="DataSyncProblemCode.DecisionsInvalid"/> and changes nothing.
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
                if (link.State == DataSyncLinkState.AwaitingAccess)
                    return await RequestAccessAgainAsync(linkId, callerMayCreateAccess, ct, gate);
                if (reason == DataSyncPauseReason.PeerReset && !restored)
                    return await AskAccessAgainAsync(link, callerMayCreateAccess, ct, gate);
                if (link is { ReadBackDeclined: true, Mode: DataSyncLinkMode.TwoWay } &&
                    link.State is not (DataSyncLinkState.Paused or DataSyncLinkState.Stopped))
                {
                    return await AskToKeepInStepAsync(link, callerMayCreateAccess, ct, gate);
                }

                return NotApplicable(link, action);
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
                // Offered only after the wait (§8.3): earlier, this device's ordinary merge would ask the questions the
                // initiator's review is still asking.
                if (link.GetStartAnywayAt() is { } availableAt && _clock.UtcNow < availableAt)
                    return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "tooEarly", link);
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
    /// link's mode and sets <c>Initiator = ThisDevice</c>. The link waits for access with its bases, pending records
    /// and items as they are, and with its pause reason kept as the mark that a reset is due
    /// (<see cref="WaitsForResetGrant"/>): only <b>once it is granted</b> is it reset (<see cref="OnOutboundGrantedAsync"/>:
    /// bases deleted, the link's items closed <c>LinkRemoved</c>, a new row with the same peer, mode and kinds), and a
    /// new first contact runs against the new epoch (§8.3, N11). A request that ends without access takes it back to
    /// <c>Paused(PeerReset)</c>, still with everything it had. The known epoch stays until the reset, so a link that
    /// loses the mark on the way (a person's pause, a stop) trips B1 again instead of merging against the new epoch.
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

        var now = _clock.UtcNow;
        var asked = await MutateAsync(link.Id, row =>
        {
            // Resumed, stopped or reset while the request was out: the row is no longer the one asked for.
            if (row is not { State: DataSyncLinkState.Paused, PausedReason: DataSyncPauseReason.PeerReset } ||
                row.PausedDetail == RestoredDetail)
            {
                return DataSyncLinkWrite.None;
            }

            row.State = DataSyncLinkState.AwaitingAccess;
            row.Initiator = DataSyncLinkInitiator.ThisDevice;
            row.PendingRequestId = requestId;
            row.ReadBackDeclined = outcome.ReadBack == "declined";
            if (outcome.PeerName is { Length: > 0 } name) row.PeerName = name;
            ClearError(row);
            row.NextAttemptAtUtc = now;
            return DataSyncLinkWrite.Transition;
        }, ct);
        if (asked is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        if (!granted || !WaitsForResetGrant(asked)) return new DataSyncLinkChange(asked, requestId, null);

        await OnOutboundGrantedAsync(link.PeerNodeId, ct);
        return new DataSyncLinkChange(await GetByPeerAsync(link.PeerNodeId, ct), null, null);
    }

    /// <summary>
    /// A link asked for access again after its peer was reset (§8.7 B1) and not granted yet: it waits for access with
    /// its pause reason kept. The grant resets it; a request that ends takes it back to its pause.
    /// </summary>
    public static bool WaitsForResetGrant(DataSyncLinkDbModel link) =>
        link is { State: DataSyncLinkState.AwaitingAccess, PausedReason: DataSyncPauseReason.PeerReset };

    /// <summary>
    /// The reset B1 waited for (§8.7), in one write: the old row goes with its bases and pending records (its items
    /// close <c>LinkRemoved</c>, its holds become local-only), and a new row for the same peer, mode and kinds runs a
    /// new first contact against the new epoch as this device's link.
    /// </summary>
    private static async Task<DataSyncLinkDbModel> ResetForNewEpochAsync(IDataSyncStore store,
        DataSyncLinkDbModel row, DateTime nowUtc, CancellationToken ct)
    {
        await store.DeleteLinkAsync(row.Id, ct);
        var fresh = new DataSyncLinkDbModel
        {
            PeerNodeId = row.PeerNodeId,
            PeerName = row.PeerName,
            PeerAddress = row.PeerAddress,
            Mode = row.Mode,
            LastMode = row.LastMode,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            KindsJson = row.KindsJson,
            ReadBackDeclined = row.ReadBackDeclined,
            NextAttemptAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        fresh.State = fresh.GetResumeState();
        return await store.AddLinkAsync(fresh, ct);
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
        var removed = await WriteAsync(async store =>
        {
            var row = await store.GetLinkAsync(linkId, ct);
            if (row is not null) await store.DeleteLinkAsync(linkId, ct);
            return row;
        }, ct);
        if (removed is not null) await AfterRemovedAsync(removed, ct);
        return removed;
    }

    private async Task AfterRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct)
    {
        _stagedPulls.Take(removed.Id);
        DiscardReview(removed.Id);
        _state.ForgetLink(removed.Id);
        await ObserveAsync(o => o.LinkRemovedAsync(removed, ct));
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

    /// <summary>
    /// The global switch (§8.7 "Other pauses"): kept in the local state row, which it makes first when it is missing
    /// (<see cref="EnsureLocalStateAsync"/>). The caller holds the gate.
    /// </summary>
    public async Task<DataSyncProblem?> SetAllPausedAsync(bool paused, DataSyncGateHold gate, CancellationToken ct)
    {
        if (await EnsureLocalStateAsync(gate, ct) is { } missing) return missing;
        var (problem, changed) = await WriteAsync(async store =>
        {
            var local = await store.GetLocalStateAsync(ct);
            if (local is null) return (NotInitialized, false);
            if (local.AllPaused == paused) return ((DataSyncProblem?) null, false);
            local.AllPaused = paused;
            local.UpdatedAtUtc = _clock.UtcNow;
            await store.SaveLocalStateAsync(local, ct);
            return (null, true);
        }, ct);

        if (changed && !paused) await MarkDueAsync(null, ct);
        return problem;
    }

    /// <summary>
    /// "Share new definitions automatically" off (§3.6): Refresh then inserts a new local definition as LocalOnly.
    /// Kept in the local state row, made first when it is missing (<see cref="EnsureLocalStateAsync"/>); the caller
    /// holds the gate, so no Refresh or apply rewrites the row meanwhile. On a device's first use that Refresh records
    /// the definitions it already has, so the choice applies to the definitions made after it.
    /// </summary>
    public async Task<DataSyncProblem?> SetNewDefinitionsStayLocalAsync(bool stayLocal, DataSyncGateHold gate,
        CancellationToken ct)
    {
        if (await EnsureLocalStateAsync(gate, ct) is { } missing) return missing;
        return await WriteAsync(async store =>
        {
            var local = await store.GetLocalStateAsync(ct);
            if (local is null) return NotInitialized;
            if (local.NewDefinitionsStayLocal == stayLocal) return null;
            local.NewDefinitionsStayLocal = stayLocal;
            local.UpdatedAtUtc = _clock.UtcNow;
            await store.SaveLocalStateAsync(local, ct);
            return (DataSyncProblem?) null;
        }, ct);
    }

    /// <summary>The local state row could not be made yet (the actor is unverified): no retry clears it before then.</summary>
    private static DataSyncProblem NotInitialized => new(DataSyncProblemCode.DecisionsInvalid, "notInitialized");

    /// <summary>
    /// The local state row appears on the first Refresh (§4.5), and Refresh runs only for a reader, inside an apply
    /// or after an entity setting (§6.6): a device that has no link and nobody reads may not have it when the person
    /// first sets a switch kept there. It is made here as an entity setting makes it: under the gate the caller holds,
    /// the actor check (§5.6), then a Refresh of every kind, each outside any transaction. While the actor is
    /// unverified Refresh writes nothing; a row still missing afterwards answers <see cref="NotInitialized"/>.
    /// </summary>
    private async Task<DataSyncProblem?> EnsureLocalStateAsync(DataSyncGateHold gate, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var store = Store(scope);
        if (await store.GetLocalStateAsync(ct) is not null) return null;

        var guard = sp.GetService<IDataSyncActorGuard>();
        var refresher = sp.GetService<IDataSyncRefresher>();
        for (var attempt = 0;; attempt++)
        {
            try
            {
                if (guard is not null) await guard.CheckAsync(gate.Lease, ct);
                if (refresher is not null && guard is not { IsVerified: false })
                    await refresher.RefreshAsync(gate.Lease, DataSyncKindIds.All, false, ct);
                break;
            }
            catch (DataSyncActorChangedException) when (attempt == 0 && guard is not null)
            {
                // Refresh found the actor rotated under it: check, then once more (§5.6).
            }
        }

        return await store.GetLocalStateAsync(ct) is null ? NotInitialized : null;
    }

    // ---- grant events ------------------------------------------------------------------------------------------

    /// <summary>
    /// Our request or code was granted (§8.2: raised within 5 s by the claim loop). A link waiting for access goes on
    /// to its first contact: AwaitingReview when this device started it, WaitingForPeerReview when the peer did
    /// (§8.1); one that asked a reset peer again is reset now (<see cref="WaitsForResetGrant"/>, §8.7 B1). Any other
    /// link is due now, which also brings a link out of AccessRevoked at its next head.
    /// </summary>
    public async Task OnOutboundGrantedAsync(string peerNodeId, CancellationToken ct)
    {
        var link = await GetByPeerAsync(peerNodeId, ct);
        if (link is null) return;
        var now = _clock.UtcNow;
        DataSyncLinkDbModel? removed = null;
        var (row, how) = await WriteAsync(async store =>
        {
            var current = await store.GetLinkAsync(link.Id, ct);
            if (current is null) return ((DataSyncLinkDbModel?) null, DataSyncLinkWrite.None);
            if (WaitsForResetGrant(current))
            {
                removed = current;
                return (await ResetForNewEpochAsync(store, current, now, ct), DataSyncLinkWrite.Transition);
            }

            current.NextAttemptAtUtc = now;
            var write = DataSyncLinkWrite.Bookkeeping;
            if (current.State == DataSyncLinkState.AwaitingAccess)
            {
                current.State = current.GetResumeState();
                current.PendingRequestId = null;
                ClearError(current);
                write = DataSyncLinkWrite.Transition;
            }

            await WriteRowAsync(store, current, write, ct);
            return (current, write);
        }, ct);

        if (removed is not null) await AfterRemovedAsync(removed, ct);
        if (row is not null && how == DataSyncLinkWrite.Transition) await ObserveAsync(o => o.LinkChangedAsync(row, ct));
    }

    /// <summary>
    /// This device granted a peer datasync access (§7.2.3, §7.2.4, §8.1). Only a two-way grant with read-back makes a
    /// link: a new one is created as the approver's (<c>TwoWay</c>, <c>Initiator = Peer</c>), in
    /// WaitingForPeerReview, or in AwaitingAccess with the failure recorded when the read-back did not give this
    /// device access (N14). A peer that already has a link updates it instead: two-way, and a stopped link goes
    /// Active (its next pull a full reconciliation, as any stopped link turned on) or WaitingForPeerReview by its
    /// first contact; any other state is kept, and so are bases and pending records. Every existing link is due now,
    /// so its next head checks the counterpart. The approval and the queued grant event both call this for one
    /// peer; whichever comes second updates the link the first made.
    /// </summary>
    /// <param name="readBackAttempted">The grant was two-way and this device tried to read the peer back.</param>
    /// <param name="readBackGranted">That read-back gave this device a datasync grant for the peer.</param>
    public async Task<DataSyncLinkDbModel?> OnInboundGrantedAsync(string peerNodeId, DataSyncRequestIntent intent,
        bool readBackAttempted, bool readBackGranted, string? readBackError, string? peerName,
        IReadOnlyList<string>? kinds, CancellationToken ct)
    {
        var twoWay = intent == DataSyncRequestIntent.TwoWay && readBackAttempted;
        var now = _clock.UtcNow;

        DataSyncLinkWrite UpdateExisting(DataSyncLinkDbModel row)
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
                else row.MarkFullReconciliationDue(now);
                row.State = readBackGranted ? row.GetResumeState() : DataSyncLinkState.AwaitingAccess;
                ClearError(row);
            }

            if (!readBackGranted && readBackError is not null)
            {
                row.LastErrorCode = ReadBackFailed;
                row.LastErrorDetail = readBackError;
            }

            return DataSyncLinkWrite.Transition;
        }

        var existing = await GetByPeerAsync(peerNodeId, ct);
        if (existing is not null) return await MutateAsync(existing.Id, UpdateExisting, ct);

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
        return (await AddUniqueAsync(created, UpdateExisting, ct)).Link;
    }

    /// <summary>
    /// A read-back this device set out on when it granted the peer two-way access failed (§7.2.4, N14): the approver's
    /// link still waits for access, and now says why (<see cref="ReadBackFailed"/>, the peer error code as the
    /// detail), so the page offers "Try again". This is how a read-back that runs in the background — a code redeemed
    /// two-way — reaches the link; an approval's own outcome also comes through <see cref="OnInboundGrantedAsync"/>.
    /// Only the approver's link waiting for access with no request of its own out records it, and only while this
    /// device still cannot read the peer.
    /// </summary>
    public async Task OnReadBackFailedAsync(string peerNodeId, string errorCode, CancellationToken ct)
    {
        static bool Waits(DataSyncLinkDbModel row) => row is
        {
            State: DataSyncLinkState.AwaitingAccess, Initiator: DataSyncLinkInitiator.Peer, PendingRequestId: null,
        };

        var link = await GetByPeerAsync(peerNodeId, ct);
        if (link is null || !Waits(link)) return;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            if (await Grants(scope).HasOutboundGrantAsync(peerNodeId, ct)) return;
        }

        await MutateAsync(link.Id, row =>
        {
            if (!Waits(row) || (row.LastErrorCode == ReadBackFailed && row.LastErrorDetail == errorCode))
                return DataSyncLinkWrite.None;
            row.LastErrorCode = ReadBackFailed;
            row.LastErrorDetail = Truncate(errorCode);
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// This device's request for a link ended without access (§8.1): the link stops and stays on the map with Dismiss
    /// (M5: nothing the user filed vanishes silently), with its last mode, bases and pending records. A link that
    /// asked a reset peer again goes back to <c>Paused(PeerReset)</c> instead, with everything it had (§8.7 B1), so
    /// the person can ask again or stop syncing; the error says why.
    /// </summary>
    public Task<DataSyncLinkDbModel?> OnRequestEndedAsync(int linkId, string errorCode, CancellationToken ct) =>
        MutateAsync(linkId, row => EndRequest(row, errorCode), ct);

    private static DataSyncLinkWrite EndRequest(DataSyncLinkDbModel row, string errorCode)
    {
        if (row.State != DataSyncLinkState.AwaitingAccess) return DataSyncLinkWrite.None;
        if (WaitsForResetGrant(row))
        {
            row.State = DataSyncLinkState.Paused;
            row.PendingRequestId = null;
            row.LastErrorCode = errorCode;
            row.LastErrorDetail = null;
            row.NextAttemptAtUtc = null;
            return DataSyncLinkWrite.Transition;
        }

        if (row.Mode != DataSyncLinkMode.Off) row.LastMode = row.Mode;
        row.Mode = DataSyncLinkMode.Off;
        row.State = DataSyncLinkState.Stopped;
        row.LastErrorCode = errorCode;
        row.LastErrorDetail = null;
        row.NextAttemptAtUtc = null;
        return DataSyncLinkWrite.Transition;
    }

    /// <summary>
    /// The person withdrew the request a link waits for. A link made for that request has nothing yet — no first
    /// contact, no cursor, no base — and goes with it. Any other link keeps its state: one turned back on after a stop,
    /// or a copy once onto a stopped link, stops again as an ended request would (<see cref="AccessCancelled"/>), so its
    /// bases, pending records and last mode are kept (§8.1); one that asked a reset peer again goes back to its pause.
    /// </summary>
    public async Task OnRequestCancelledAsync(int linkId, CancellationToken ct)
    {
        DataSyncLinkDbModel? stopped = null;
        var removed = await WriteAsync(async store =>
        {
            var row = await store.GetLinkAsync(linkId, ct);
            if (row is not { State: DataSyncLinkState.AwaitingAccess }) return null;
            if (!WaitsForResetGrant(row) && !await HasSyncStateAsync(store, row, ct))
            {
                await store.DeleteLinkAsync(linkId, ct);
                return row;
            }

            var how = EndRequest(row, AccessCancelled);
            await WriteRowAsync(store, row, how, ct);
            if (how == DataSyncLinkWrite.Transition) stopped = row;
            return null;
        }, ct);

        if (removed is not null) await AfterRemovedAsync(removed, ct);
        if (stopped is not null) await ObserveAsync(o => o.LinkChangedAsync(stopped, ct));
    }

    /// <summary>Whether a link holds anything a reset would delete: a first contact, a cursor or a base.</summary>
    private static async Task<bool> HasSyncStateAsync(IDataSyncStore store, DataSyncLinkDbModel link,
        CancellationToken ct)
    {
        if (link.FirstContactCompletedAtUtc is not null || link.GetFirstContactKinds().Count > 0 ||
            link.GetCursors().Count > 0)
        {
            return true;
        }

        foreach (var kind in DataSyncKindIds.All)
        {
            if ((await store.GetBasesAsync(link.Id, kind, ct)).Count > 0) return true;
        }

        return false;
    }

    // ---- after the apply task ----------------------------------------------------------------------------------

    /// <summary>
    /// After <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/> committed an apply, or paused the link: records the
    /// kinds whose first contact this pull completed (the approver's first pull, or kinds added to a link), a full
    /// reconciliation, and the once flags the apply consumed; tells the observer what was applied and a pause the
    /// runner wrote. An apply that paused consumed nothing: its pull is dropped before the final transaction (§8.7,
    /// §8.10.2), so the flags wait for the apply after the resume, and the person does not have to choose again. Never
    /// called for an apply that failed or applied nothing (<see cref="DataSyncAutoSyncEnd"/>): that one consumed
    /// nothing either, and its failure stands.
    /// </summary>
    /// <param name="firstContactOpen">
    /// The link's first contact was not complete before the apply: when it is afterwards, whoever completed it (the
    /// runner's final transaction, or this), the apply was the first sync (§8.3, §9.4).
    /// </param>
    /// <param name="fullReconciliation">The pull carried every kind of the link from 0 (§8.8).</param>
    public async Task<DataSyncLinkDbModel?> AfterAutoSyncAsync(DataSyncLinkContext context, DataSyncStagedPull? pull,
        DataSyncAutoSyncOutcome outcome, bool firstContactOpen, bool fullReconciliation, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var firstSync = false;
        var link = await MutateAsync(context.LinkId, row =>
        {
            if (outcome.Paused is not null || outcome.End != DataSyncAutoSyncEnd.Committed) return DataSyncLinkWrite.None;
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

            if (pull is null) return write;

            var pulled = pull.Kinds.Select(k => k.Kind).ToHashSet(StringComparer.Ordinal);
            var completed = context.FirstContactKinds.Where(pulled.Contains).ToList();
            if (completed.Count > 0)
            {
                row.SetFirstContactKinds(row.GetFirstContactKinds().Union(completed));
                row.FirstContactCompletedAtUtc ??= now;
                write = DataSyncLinkWrite.Transition;
            }

            firstSync = firstContactOpen && row.FirstContactCompletedAtUtc is not null;

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
        else if (outcome.End == DataSyncAutoSyncEnd.Committed)
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
