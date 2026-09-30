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
/// <para>
/// <b>What this asks of the apply runner [C].</b> Most writers here take no DataSyncGate — the fetch half, grant
/// events, creating, changing, pausing and resuming a link, withdrawing a request, and approving one (whose link row
/// §10.1 gates; here it is not, so an approval never waits behind an apply) — so the gate an apply holds does not keep
/// them from a link row. What orders them with the
/// runner is the database alone, which holds only if the runner reads a link row inside the same write transaction
/// that writes it back (<c>BEGIN IMMEDIATE</c>), and never writes back a row it read before that transaction began.
/// </para>
/// <para>
/// <b>Stops and resets hold the gate.</b> Either releases the link's holds (§8.1, must-fix 28), which rewrites entity
/// rows, and those only a holder of the gate writes (§2.9): an apply holds it while it writes
/// what its one merge decided. A caller that holds the gate passes its <see cref="DataSyncGateHold"/>; otherwise the
/// write enters it itself (<see cref="WriteUnderGateAsync{T}"/>), before the lock and the transaction.
/// </para>
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
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncRuntimeObserver _observer;
    private readonly IDataSyncRowTransactions _transactions;
    private readonly IDataSyncGateEntry _gate;
    private readonly ILogger<DataSyncLinkService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataSyncLinkService(IServiceScopeFactory scopes, IDataSyncClock clock, DataSyncRuntimeState state,
        IDataSyncRuntimeObserver observer, IDataSyncRowTransactions transactions, IDataSyncGateEntry gate,
        ILogger<DataSyncLinkService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _state = state;
        _observer = observer;
        _transactions = transactions;
        _gate = gate;
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

    /// <summary>
    /// <see cref="WriteAsync{T}"/> for a write that stops or resets a link (see the remarks): under the gate — the
    /// caller's <paramref name="held"/>, else entered here, without a limit (the fetch half and grant events run in the
    /// background), before the lock and the transaction.
    /// </summary>
    private async Task<T> WriteUnderGateAsync<T>(DataSyncGateHold? held, Func<IDataSyncStore, Task<T>> write,
        CancellationToken ct)
    {
        using var lease = held is not null
            ? null
            : await _gate.TryEnterAsync(null, ct) ??
              throw new InvalidOperationException("The data sync gate refused a wait without a limit.");
        return await WriteAsync(write, ct);
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
    /// request when the peer does not read this device yet, which mints a reciprocal code (§7.2.4); a code the person
    /// gave is redeemed one way instead.
    /// <paramref name="callerMayCreateAccess"/> false refuses exactly the calls that would send a request or mint a
    /// code (§7.1.5).
    /// </summary>
    public async Task<DataSyncLinkChange> CreateAsync(DataSyncLinkCreate input, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        var mode = input.CopyOnce ? DataSyncLinkMode.Off : input.Mode;
        if (mode == DataSyncLinkMode.Off && !input.CopyOnce)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "mode");
        if (!TryNormalizeKinds(input.Kinds, out var kinds, out var unknownKind))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.UnknownKind, unknownKind);
        if (string.IsNullOrEmpty(input.PeerNodeId) && string.IsNullOrEmpty(input.Address))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "peer");

        var existing = input.PeerNodeId is { } knownId ? await GetByPeerAsync(knownId, ct) : null;
        if (existing is not null && !CanCopyOnceOnto(existing, input.CopyOnce))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkExists, null, existing);

        var hasAccess = input.PeerNodeId is not null && await HasAccessAsync(input.PeerNodeId, ct);
        var peerMayReadUs = input.PeerNodeId is not null && await PeerMayReadUsAsync(input.PeerNodeId, ct);
        var needsRequest = !hasAccess || (mode == DataSyncLinkMode.TwoWay && !peerMayReadUs);

        var peerNodeId = input.PeerNodeId;
        string? peerName = null;
        string? requestId = null;
        var readBackDeclined = false;
        if (needsRequest)
        {
            // A code grants one way only (§7.2.3): it is redeemed as Follow, with no offer to be read back, and a
            // two-way link made with it says the peer does not read this device until it is asked to.
            var (outcome, refused) = await AskAsync(new DataSyncAccessRequestInput(input.PeerNodeId, input.Address,
                input.Code, IntentOf(input.Code is null ? mode : DataSyncLinkMode.Follow)), callerMayCreateAccess, null,
                ct);
            if (refused is not null) return refused;
            peerNodeId = outcome!.PeerNodeId;
            peerName = outcome.PeerName;
            hasAccess |= outcome.Outcome == "granted";
            requestId = outcome.Outcome == "awaitingApproval" ? outcome.RequestId : null;
            readBackDeclined = outcome.ReadBack == "declined" ||
                               (input.Code is not null && mode == DataSyncLinkMode.TwoWay &&
                                !await PeerMayReadUsAsync(outcome.PeerNodeId, ct));

            // Reached by address: only now is the peer known, and it may already have a link.
            existing ??= await GetByPeerAsync(peerNodeId, ct);
            if (existing is not null && !CanCopyOnceOnto(existing, input.CopyOnce))
                return new DataSyncLinkChange(existing, requestId,
                    new DataSyncProblem(DataSyncProblemCode.LinkExists, null));
        }

        peerName ??= await PeerNameAsync(peerNodeId!, ct);
        var now = _clock.UtcNow;
        var created = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId!,
            PeerName = peerName,
            PeerAddress = input.Address ?? existing?.PeerAddress,
            Mode = mode,
            LastMode = mode == DataSyncLinkMode.Off ? DataSyncLinkMode.TwoWay : mode,
            State = hasAccess ? DataSyncLinkState.AwaitingReview : DataSyncLinkState.AwaitingAccess,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            PendingRequestId = requestId,
            ReadBackDeclined = readBackDeclined,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        created.SetKinds(kinds);
        // A copy once onto a stopped link starts over from a fresh row (one link per peer, §8.1): the stopped row goes
        // with its bases and pending records, unless it was turned on or reset meanwhile.
        if (existing is not null)
            return await StartOverAsync(existing.Id, row => row.State == DataSyncLinkState.Stopped, _ => created,
                requestId, new DataSyncProblem(DataSyncProblemCode.LinkExists, null), ct);
        var (row, added) = await AddUniqueAsync(created, null, ct);
        return added
            ? new DataSyncLinkChange(row, requestId, null)
            : new DataSyncLinkChange(row, requestId, new DataSyncProblem(DataSyncProblemCode.LinkExists, null));
    }

    // ---- mode and kinds ----------------------------------------------------------------------------------------

    /// <summary>
    /// Changes a link's mode and kinds (§8.1). Off stops it (bases and pending records kept). Turning a stopped link
    /// on resumes it, and its next pull is a full reconciliation, which re-merges every pending record (§8.4 condition
    /// 4): the items its stop closed come back even when the peer has nothing new. Kinds added to a link are merged
    /// from their start by its next pull. A copy once turned on before its Start becomes that link's first sync: the
    /// copy once's snapshot goes, and the next cycle stages the link's own (<see cref="ForgetCopyOnceReview"/>).
    /// Two-way on a peer that does not read this device sends a request with a reciprocal code (§7.2.4).
    /// </summary>
    /// <param name="gate">For Off: the caller's hold on the gate, if it holds it; otherwise the stop enters it.</param>
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
            return new DataSyncLinkChange(await StopAsync(link.Id, newKinds, gate, ct), null, null);
        }

        if (mode is not (null or DataSyncLinkMode.Follow or DataSyncLinkMode.TwoWay))
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "mode", link);

        var target = mode ?? link.Mode;
        var turningOn = mode is not null && (link.Mode == DataSyncLinkMode.Off || link.State == DataSyncLinkState.Stopped);
        var hasAccess = !turningOn || await HasAccessAsync(link.PeerNodeId, ct);
        var wantsReadBack = target == DataSyncLinkMode.TwoWay && mode == DataSyncLinkMode.TwoWay &&
                            (link.Mode != DataSyncLinkMode.TwoWay || turningOn) &&
                            !await PeerMayReadUsAsync(link.PeerNodeId, ct);
        DataSyncAccessRequestOutcome? outcome = null;
        if (mode is not null && (!hasAccess || wantsReadBack))
        {
            (outcome, var refused) = await AskAsync(new DataSyncAccessRequestInput(link.PeerNodeId, null, null,
                IntentOf(target)), callerMayCreateAccess, link, ct);
            if (refused is not null) return refused;
            hasAccess |= outcome!.Outcome == "granted";
        }

        var now = _clock.UtcNow;
        var requestId = outcome?.Outcome == "awaitingApproval" ? outcome.RequestId : null;
        var updated = await MutateAsync(linkId, row =>
        {
            if (newKinds is not null) row.SetKinds(newKinds);
            if (mode is { } m)
            {
                ForgetCopyOnceReview(row);
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
            _state.SetDue(row.Id);
            return DataSyncLinkWrite.Transition;
        }, ct);
        return new DataSyncLinkChange(updated, requestId, null);
    }

    /// <summary>
    /// Off (§8.1): the store stops the link (its items close <c>LinkStopped</c>, its holds become local-only; bases and
    /// pending records stay) and the row records it, in one write under the gate.
    /// </summary>
    private async Task<DataSyncLinkDbModel?> StopAsync(int linkId, IReadOnlyList<string>? kinds, DataSyncGateHold? gate,
        CancellationToken ct)
    {
        var stopped = await WriteUnderGateAsync(gate, async store =>
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
            if (kinds is not null) row.SetKinds(kinds);
            _state.SetDue(row.Id);
            await WriteRowAsync(store, row, DataSyncLinkWrite.Transition, ct);
            return row;
        }, ct);

        _state.TakePull(linkId);
        _state.DropPreview(linkId);
        if (stopped is not null) await ObserveAsync(o => o.LinkChangedAsync(stopped, ct));
        return stopped;
    }

    /// <summary>
    /// "Try again" for a link that waits for access (§7.2.4): a fresh request to the peer. The approver of a two-way
    /// link whose read-back failed (N14) asks only to read the peer back (Follow): the peer already reads this device.
    /// Reached through <see cref="DataSyncResumeAction.AskAccessAgain"/> on a link in AwaitingAccess.
    /// </summary>
    public async Task<DataSyncLinkChange> RequestAccessAgainAsync(int linkId, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        var link = await GetAsync(linkId, ct);
        if (link is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        if (link.State != DataSyncLinkState.AwaitingAccess)
            return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "notAwaitingAccess", link);
        var (outcome, refused) = await AskAsync(new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null,
            IntentOf(link.Initiator == DataSyncLinkInitiator.ThisDevice ? link.Mode : DataSyncLinkMode.Follow)),
            callerMayCreateAccess, link, ct);
        if (refused is not null) return refused;
        if (outcome!.Outcome == "granted")
        {
            await OnOutboundGrantedAsync(link.PeerNodeId, outcome.ReadBack, ct);
            return new DataSyncLinkChange(await GetAsync(linkId, ct), null, null);
        }

        var requestId = outcome.RequestId;
        var updated = await MutateAsync(linkId, row =>
        {
            // Granted, ended or stopped while the request was out: the row is no longer the one asked for.
            if (row.State != DataSyncLinkState.AwaitingAccess) return DataSyncLinkWrite.None;
            row.PendingRequestId = requestId;
            ClearError(row);
            _state.SetDue(row.Id);
            return DataSyncLinkWrite.Transition;
        }, ct);
        return new DataSyncLinkChange(updated, requestId, null);
    }

    /// <summary>
    /// "[Ask {name} to keep in step]" (§7.2.3): the peer granted this two-way link's access but declined to read this
    /// device back (<c>ReadBackDeclined</c>), so this sends an ordinary two-way request with a reciprocal offer
    /// (§7.2.4). A peer that reads this device by now needs nothing: the note is cleared instead. Otherwise the note
    /// stays until the peer does read this device — its grant for this device (<see cref="OnInboundGrantedAsync"/>),
    /// or the request granted with the read-back started
    /// (<see cref="OnOutboundGrantedAsync(string, string?, CancellationToken)"/>) — so a request that
    /// is rejected, expires or is approved without the read-back leaves it where it was.
    /// </summary>
    private async Task<DataSyncLinkChange> AskToKeepInStepAsync(DataSyncLinkDbModel link, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        if (await PeerMayReadUsAsync(link.PeerNodeId, ct))
        {
            var cleared = await MutateAsync(link.Id, row =>
            {
                if (!row.ReadBackDeclined) return DataSyncLinkWrite.None;
                row.ReadBackDeclined = false;
                return DataSyncLinkWrite.Transition;
            }, ct);
            return new DataSyncLinkChange(cleared, null, null);
        }

        var (outcome, refused) = await AskAsync(new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null,
            DataSyncRequestIntent.TwoWay), callerMayCreateAccess, link, ct);
        if (refused is not null) return refused;
        var requestId = outcome!.Outcome == "awaitingApproval" ? outcome.RequestId : null;
        var updated = await MutateAsync(link.Id, row =>
        {
            NoteReadBack(row, outcome.ReadBack);
            _state.SetDue(row.Id);
            return DataSyncLinkWrite.Transition;
        }, ct);
        return new DataSyncLinkChange(updated, requestId, null);
    }

    /// <summary>
    /// "Ask {name} for access again" on a link whose peer refused this device's credentials (AccessRevoked; the same
    /// state shows AccessMissing): the peer revoked this reader, removed the device or replaced its grant, so what this
    /// device holds for it is dead. It is forgotten first, so only credentials that arrive afterwards — the answer to
    /// this request — can read as access, never the ones the peer revoked; then a new request goes out, two-way when
    /// the link is and the peer does not read this device. The link waits for access with its bases, pending records
    /// and items (the peer's epoch has not changed), and goes back to where it was once granted; a request that ends
    /// without access stops it like any other (§8.1), and turning it on again asks again.
    /// </summary>
    private async Task<DataSyncLinkChange> AskAccessAfterRevokedAsync(DataSyncLinkDbModel link,
        bool callerMayCreateAccess, CancellationToken ct)
    {
        var twoWay = link.Mode == DataSyncLinkMode.TwoWay && !await PeerMayReadUsAsync(link.PeerNodeId, ct);
        var (outcome, refused) = await AskAsync(new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null,
            twoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow), callerMayCreateAccess, link, ct,
            forgetDeadCredentials: true);
        if (refused is not null) return refused;
        if (outcome!.Outcome == "granted")
        {
            // Fresh credentials: the link's next head reads with them and brings it out of AccessRevoked.
            await OnOutboundGrantedAsync(link.PeerNodeId, outcome.ReadBack, ct);
            return new DataSyncLinkChange(await GetAsync(link.Id, ct), null, null);
        }

        var requestId = outcome.RequestId;
        var asked = await MutateAsync(link.Id, row =>
        {
            // Answered again, stopped or paused while the request was out: the row is no longer the one asked for.
            if (!IsAccessLost(row)) return DataSyncLinkWrite.None;
            row.State = DataSyncLinkState.AwaitingAccess;
            row.PendingRequestId = requestId;
            if (outcome.PeerName is { Length: > 0 } name) row.PeerName = name;
            ClearError(row);
            _state.SetDue(row.Id);
            return DataSyncLinkWrite.Transition;
        }, ct);
        return asked is null
            ? DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound)
            : new DataSyncLinkChange(asked, requestId, null);
    }

    /// <summary>
    /// Drops the datasync credentials this device holds for a peer that refused them (a revoked grant), before it asks
    /// the peer for new ones: only credentials the answer brings can then read as access. Nothing is dropped when there
    /// are none, so a request of this device's that is already out stays.
    /// </summary>
    private static async Task<DataSyncProblem?> ForgetDeadCredentialsAsync(IDataSyncGrantService grants,
        string peerNodeId, CancellationToken ct)
    {
        if (!await grants.HasOutboundGrantAsync(peerNodeId, ct)) return null;
        try
        {
            await grants.ForgetOutboundAsync(peerNodeId, ct);
            return null;
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }
    }

    /// <summary>
    /// What a peer said about reading this device back when it answered a two-way request or code (§7.2.3, §7.2.4
    /// step 7): <c>declined</c> puts the note "{name} does not read this device" on a two-way link, <c>started</c>
    /// clears it; nothing said (a Follow request, an older peer) leaves it as it is.
    /// </summary>
    /// <returns>Whether the row changed.</returns>
    private static bool NoteReadBack(DataSyncLinkDbModel row, string? readBack)
    {
        bool declined;
        switch (readBack)
        {
            case "declined" when row.Mode == DataSyncLinkMode.TwoWay:
                declined = true;
                break;
            case "started":
                declined = false;
                break;
            default:
                return false;
        }

        if (row.ReadBackDeclined == declined) return false;
        row.ReadBackDeclined = declined;
        return true;
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
    /// that tripped the breaker, so the next fetch after a resume starts again from the cursor. A peer found reset
    /// also clears the error an earlier call left: the pause says why nothing is pulled (the grant the reset revoked
    /// is not "stopped sharing", §11.6).
    /// </summary>
    public async Task<DataSyncLinkDbModel?> PauseAsync(int linkId, DataSyncPauseReason reason, string? detail,
        CancellationToken ct)
    {
        _state.TakePull(linkId);
        var paused = false;
        var link = await MutateAsync(linkId, row =>
        {
            // A link stopped meanwhile stays stopped: nothing pulls it anyway.
            if (row.State == DataSyncLinkState.Stopped) return DataSyncLinkWrite.None;
            row.State = DataSyncLinkState.Paused;
            row.PausedReason = reason;
            row.PausedDetail = detail;
            if (reason == DataSyncPauseReason.PeerReset) ClearError(row);
            paused = true;
            return DataSyncLinkWrite.Transition;
        }, ct);
        _state.TakePull(linkId);
        if (paused && link is not null) await ObserveAsync(o => o.LinkPausedAsync(link, ct));
        return link;
    }

    /// <summary>
    /// The resume actions of §8.7. Resume re-evaluates from the cursor (a restored peer: from 0); AskAccessAgain asks a
    /// reset peer for access again and runs a
    /// new first contact against its new epoch, and is also "Try again" for a link waiting for access (§7.2.4, N14),
    /// "Ask {name} for access again" for a link whose peer revoked its access (AccessRevoked, §8.1), and "[Ask {name}
    /// to keep in step]" for a two-way link the peer does not read back (§7.2.3); ThisDeviceWins and
    /// TakeTheirs enqueue the restore choice (§9.5); StartAnyway starts an approver that has waited
    /// <see cref="DataSyncSchedule.StartAnywayAfter"/> for its peer's review (§8.3). An action that does not apply to
    /// the link's state is refused with <see cref="DataSyncProblemCode.DecisionsInvalid"/> and changes nothing.
    /// </summary>
    public async Task<DataSyncLinkChange> ResumeAsync(int linkId, DataSyncResumeAction action,
        bool callerMayCreateAccess, CancellationToken ct)
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
                return new DataSyncLinkChange(await UnpauseAsync(linkId, restored, ct), null, null);
            }
            case DataSyncResumeAction.AskAccessAgain:
            {
                if (link.State == DataSyncLinkState.AwaitingAccess)
                    return await RequestAccessAgainAsync(linkId, callerMayCreateAccess, ct);
                if (reason == DataSyncPauseReason.PeerReset && !restored)
                    return await AskAccessAgainAsync(link, callerMayCreateAccess, ct);
                if (IsAccessLost(link)) return await AskAccessAfterRevokedAsync(link, callerMayCreateAccess, ct);
                if (link is { ReadBackDeclined: true, Mode: DataSyncLinkMode.TwoWay } &&
                    link.State is not (DataSyncLinkState.Paused or DataSyncLinkState.Stopped))
                {
                    return await AskToKeepInStepAsync(link, callerMayCreateAccess, ct);
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
                if (link.State != DataSyncLinkState.WaitingForPeerReview || link.HasPeerError())
                    return NotApplicable(link, action);
                // Offered only after the wait (§8.3): earlier, this device's ordinary merge would ask the questions the
                // initiator's review is still asking.
                if (link.GetStartAnywayAt() is { } availableAt && _clock.UtcNow < availableAt)
                    return DataSyncLinkChange.Refused(DataSyncProblemCode.DecisionsInvalid, "tooEarly", link);
                var started = await MutateAsync(linkId, row =>
                {
                    if (row.State != DataSyncLinkState.WaitingForPeerReview) return DataSyncLinkWrite.None;
                    row.State = DataSyncLinkState.Active;
                    _state.SetDue(row.Id);
                    return DataSyncLinkWrite.Transition;
                }, ct);
                return new DataSyncLinkChange(started, null, null);
            }
            default:
                return NotApplicable(link, action);
        }
    }

    private async Task<DataSyncLinkDbModel?> UnpauseAsync(int linkId, bool resetCursors, CancellationToken ct)
    {
        _state.TakePull(linkId);
        var link = await GetAsync(linkId, ct);
        var waitsForAccess = link is { PendingRequestId: not null } && !await HasAccessAsync(link.PeerNodeId, ct);
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

            _state.SetDue(row.Id);
            // What waited while the link was paused (a decision's re-merge, say) applies now, without a pull.
            _state.RequestReMerge(row.Id);
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// B1's "Ask X for access again" (§8.7): the reset revoked every grant, so the credentials this device holds for the
    /// peer are forgotten first — only the answer can then read as access — and a request goes out with the link's
    /// mode. The link then starts over (<see cref="StartOverAsync"/>): its row goes with its bases and pending records
    /// (its items close <c>LinkRemoved</c>), and a fresh row for the same peer, mode and kinds, as this device's link,
    /// waits for access — or for its review, when the peer granted at once — and runs a new first contact against the
    /// new epoch (§8.3). A request that ends without access stops it like any other; withdrawn, it goes with it.
    /// </summary>
    private async Task<DataSyncLinkChange> AskAccessAgainAsync(DataSyncLinkDbModel link, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        var (outcome, refused) = await AskAsync(new DataSyncAccessRequestInput(link.PeerNodeId, link.PeerAddress, null,
            IntentOf(link.Mode == DataSyncLinkMode.Off ? link.LastMode : link.Mode)), callerMayCreateAccess, link, ct,
            forgetDeadCredentials: true);
        if (refused is not null) return refused;
        var requestId = outcome!.Outcome == "awaitingApproval" ? outcome.RequestId : null;
        var now = _clock.UtcNow;
        return await StartOverAsync(link.Id,
            // Resumed, stopped or reset while the request was out: the row is no longer the one asked for.
            row => row is { State: DataSyncLinkState.Paused, PausedReason: DataSyncPauseReason.PeerReset } &&
                   row.PausedDetail != RestoredDetail,
            row =>
            {
                var fresh = new DataSyncLinkDbModel
                {
                    PeerNodeId = row.PeerNodeId,
                    PeerName = outcome.PeerName is { Length: > 0 } name ? name : row.PeerName,
                    PeerAddress = row.PeerAddress,
                    Mode = row.Mode,
                    LastMode = row.LastMode,
                    Initiator = DataSyncLinkInitiator.ThisDevice,
                    KindsJson = row.KindsJson,
                    PendingRequestId = requestId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                fresh.State = outcome.Outcome == "granted" ? fresh.GetResumeState() : DataSyncLinkState.AwaitingAccess;
                NoteReadBack(fresh, outcome.ReadBack);
                return fresh;
            }, requestId, null, ct);
    }

    /// <summary>
    /// A link that starts over (a reset peer asked again, a copy once onto a stopped link): while
    /// <paramref name="stillSo"/> holds for its row, the row is deleted — its bases and pending records with it, its
    /// items closed <c>LinkRemoved</c>, its holds made local-only — and <paramref name="fresh"/> inserted, in one write
    /// under the gate, which it waits for at most 30 s (§10.1) and otherwise answers Busy. A row that moved meanwhile
    /// stays as it is, with <paramref name="moved"/> as the answer's problem.
    /// </summary>
    private async Task<DataSyncLinkChange> StartOverAsync(int linkId, Func<DataSyncLinkDbModel, bool> stillSo,
        Func<DataSyncLinkDbModel, DataSyncLinkDbModel> fresh, string? requestId, DataSyncProblem? moved,
        CancellationToken ct)
    {
        DataSyncLinkDbModel? removed = null;
        DataSyncLinkDbModel? row;
        await using (var gate = await DataSyncGateHold.TryEnterAsync(_gate, DataSyncGateHold.RequestTimeout, ct))
        {
            if (gate is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.Busy);
            row = await WriteAsync(async store =>
            {
                var current = await store.GetLinkAsync(linkId, ct);
                if (current is null || !stillSo(current)) return current;
                removed = current;
                await store.DeleteLinkAsync(linkId, ct);
                return await store.AddLinkAsync(fresh(current), ct);
            }, ct);
        }

        if (row is null) return DataSyncLinkChange.Refused(DataSyncProblemCode.LinkNotFound);
        if (removed is null) return new DataSyncLinkChange(row, requestId, moved);
        await AfterRemovedAsync(removed, ct);
        _state.SetDue(row.Id);
        await ObserveAsync(o => o.LinkChangedAsync(row, ct));
        return new DataSyncLinkChange(row, requestId, null);
    }

    // ---- reset -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reset or Dismiss (§8.1): the link row, its bases and pending records are deleted, its items close
    /// <c>LinkRemoved</c> and its holds become local-only; definitions stay as they are.
    /// </summary>
    /// <param name="gate">The caller's hold on the gate, if it holds it; otherwise the reset enters it.</param>
    public async Task<DataSyncProblem?> ResetAsync(int linkId, CancellationToken ct, DataSyncGateHold? gate = null) =>
        await RemoveAsync(linkId, gate, ct) is null ? new DataSyncProblem(DataSyncProblemCode.LinkNotFound, null) : null;

    private async Task<DataSyncLinkDbModel?> RemoveAsync(int linkId, DataSyncGateHold? gate, CancellationToken ct)
    {
        var removed = await WriteUnderGateAsync(gate, async store =>
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
        _state.TakePull(removed.Id);
        _state.DropPreview(removed.Id);
        _state.ForgetLink(removed.Id);
        await ObserveAsync(o => o.LinkRemovedAsync(removed, ct));
    }

    // ---- scheduling --------------------------------------------------------------------------------------------

    /// <summary>"Sync now" (§8.2): the link, or every link the fetch cycle looks at, is due now.</summary>
    public async Task MarkDueAsync(int? linkId, CancellationToken ct)
    {
        IEnumerable<int> ids = linkId is { } id
            ? [id]
            : (await GetLinksAsync(ct)).Where(l => l.IsFetchable()).Select(l => l.Id).ToList();
        foreach (var target in ids) _state.SetDue(target);
    }

    /// <summary>At the start, every link the fetch cycle looks at is due at <paramref name="dueAtUtc"/> (§8.2).</summary>
    public async Task ScheduleAllAsync(DateTime dueAtUtc, CancellationToken ct)
    {
        foreach (var link in (await GetLinksAsync(ct)).Where(l => l.IsFetchable())) _state.SetDue(link.Id, dueAtUtc);
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

        var guard = sp.GetRequiredService<IDataSyncActorGuard>();
        await guard.CheckAsync(gate.Lease, ct);
        try
        {
            if (guard.IsVerified)
                await sp.GetRequiredService<IDataSyncRefresher>().RefreshAsync(gate.Lease, DataSyncKindIds.All, false, ct);
        }
        catch (DataSyncActorChangedException)
        {
            // An identity reset raced the call (§5.6): nothing was written.
            return new DataSyncProblem(DataSyncProblemCode.Busy, null);
        }

        return await store.GetLocalStateAsync(ct) is null ? NotInitialized : null;
    }

    // ---- grant events ------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="OnOutboundGrantedAsync(string, string?, CancellationToken)"/>, nothing said about reading back.
    /// </summary>
    public Task OnOutboundGrantedAsync(string peerNodeId, CancellationToken ct) =>
        OnOutboundGrantedAsync(peerNodeId, null, ct);

    /// <summary>
    /// Our request or code was granted (§8.2: raised within 5 s by the claim loop). A link waiting for access goes on
    /// to its first contact: AwaitingReview when this device started it, WaitingForPeerReview when the peer did
    /// (§8.1). Any other link is due now, which also brings a link out of AccessRevoked at its next head.
    /// </summary>
    /// <param name="readBack">
    /// What the peer said about reading this device back when it granted a two-way request (the exchange's
    /// <c>readBack</c>, as <see cref="DataSyncAccessRequestOutcome.ReadBack"/>): approved without it
    /// (<c>declined</c>), a two-way link says "{name} does not read this device" (§7.2.4 step 7); <c>started</c> clears
    /// that; null says nothing.
    /// </param>
    public async Task OnOutboundGrantedAsync(string peerNodeId, string? readBack, CancellationToken ct)
    {
        if (await GetByPeerAsync(peerNodeId, ct) is not { } link) return;
        await MutateAsync(link.Id, row =>
        {
            _state.SetDue(row.Id);
            var write = NoteReadBack(row, readBack) ? DataSyncLinkWrite.Transition : DataSyncLinkWrite.None;
            if (row.State != DataSyncLinkState.AwaitingAccess) return write;
            row.State = row.GetResumeState();
            row.PendingRequestId = null;
            ClearError(row);
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// This device granted a peer datasync access (§7.2.3, §7.2.4, §8.1): approved its request, or the peer redeemed a
    /// code. Any grant lets the peer read this device, so "{name} does not read this device" no longer holds. Only a
    /// two-way approval to receive back (<paramref name="twoWay"/>) makes a link: a new one is created as the approver's
    /// (<c>TwoWay</c>, <c>Initiator = Peer</c>), in WaitingForPeerReview, or in AwaitingAccess with the failure recorded
    /// when the read-back did not give this device access (N14). A peer that already has a link updates it instead:
    /// two-way, and a stopped link goes Active (its next pull a full reconciliation, as any stopped link turned on) or
    /// WaitingForPeerReview by its first contact; any other state is kept, and so are bases and pending records — a copy
    /// once waiting for its Start becomes the link's first sync (<see cref="ForgetCopyOnceReview"/>). Every existing
    /// link is due now, so its next head checks the counterpart.
    /// </summary>
    /// <param name="readBackGranted">The read-back gave this device a datasync grant for the peer.</param>
    public async Task<DataSyncLinkDbModel?> OnInboundGrantedAsync(string peerNodeId, bool twoWay,
        bool readBackGranted, string? readBackError, string? peerName, IReadOnlyList<string>? kinds,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        DataSyncLinkWrite UpdateExisting(DataSyncLinkDbModel row)
        {
            _state.SetDue(row.Id);
            var noteCleared = row.ReadBackDeclined;
            row.ReadBackDeclined = false;
            if (!twoWay) return noteCleared ? DataSyncLinkWrite.Transition : DataSyncLinkWrite.Bookkeeping;
            ForgetCopyOnceReview(row);
            row.Mode = DataSyncLinkMode.TwoWay;
            row.LastMode = DataSyncLinkMode.TwoWay;
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
                row.LastErrorDetail = Truncate(readBackError);
            }

            return DataSyncLinkWrite.Transition;
        }

        if (!twoWay)
            return await GetByPeerAsync(peerNodeId, ct) is { } existing
                ? await MutateAsync(existing.Id, UpdateExisting, ct)
                : null;
        if (!TryNormalizeKinds(kinds, out var normalized, out _)) normalized = DataSyncKindIds.All.ToList();
        if (string.IsNullOrEmpty(peerName)) peerName = await PeerNameAsync(peerNodeId, ct);

        var created = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId,
            PeerName = peerName,
            Mode = DataSyncLinkMode.TwoWay,
            LastMode = DataSyncLinkMode.TwoWay,
            State = readBackGranted ? DataSyncLinkState.WaitingForPeerReview : DataSyncLinkState.AwaitingAccess,
            Initiator = DataSyncLinkInitiator.Peer,
            LastErrorCode = readBackGranted ? null : ReadBackFailed,
            LastErrorDetail = readBackGranted ? null : Truncate(readBackError),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        created.SetKinds(normalized);
        return (await AddUniqueAsync(created, UpdateExisting, ct)).Link;
    }

    /// <summary>
    /// This device's request for a link ended without access (§8.1): the link stops as Off stops it — its items close
    /// <c>LinkStopped</c> and its holds become local-only, under the gate — and stays on the map with Dismiss (M5:
    /// nothing the user filed vanishes silently), with its last mode, bases and pending records; the error says why.
    /// </summary>
    public async Task<DataSyncLinkDbModel?> OnRequestEndedAsync(int linkId, string errorCode, CancellationToken ct)
    {
        var (link, how) = await WriteUnderGateAsync(null, async store =>
            await store.GetLinkAsync(linkId, ct) is { } row
                ? await EndRequestAsync(store, row, errorCode, ct)
                : (null, DataSyncLinkWrite.None), ct);
        await AfterRequestEndedAsync(link, how, ct);
        return link;
    }

    /// <summary>
    /// The request a link waits for ended (<see cref="OnRequestEndedAsync"/>), in the caller's write under the gate.
    /// Returns the row as it was written: the row read again after the store stopped it.
    /// </summary>
    private async Task<(DataSyncLinkDbModel? Row, DataSyncLinkWrite How)> EndRequestAsync(IDataSyncStore store,
        DataSyncLinkDbModel row, string errorCode, CancellationToken ct)
    {
        if (row.State != DataSyncLinkState.AwaitingAccess) return (row, DataSyncLinkWrite.None);

        // As Off stops a link (StopAsync): the store closes its items and releases its holds, which nobody can decide
        // any more (§8.1, must-fix 28), and the row records why.
        var lastMode = row.Mode != DataSyncLinkMode.Off ? row.Mode : row.LastMode;
        await store.StopLinkAsync(row.Id, ct);
        var stopped = await store.GetLinkAsync(row.Id, ct);
        if (stopped is null) return (null, DataSyncLinkWrite.None);
        stopped.LastMode = lastMode;
        stopped.Mode = DataSyncLinkMode.Off;
        stopped.State = DataSyncLinkState.Stopped;
        stopped.LastErrorCode = errorCode;
        stopped.LastErrorDetail = null;
        _state.SetDue(stopped.Id);
        await WriteRowAsync(store, stopped, DataSyncLinkWrite.Transition, ct);
        return (stopped, DataSyncLinkWrite.Transition);
    }

    /// <summary>
    /// After <see cref="EndRequestAsync"/> committed: a stopped link's staged pulls go, as when it is turned Off, and
    /// the change is published.
    /// </summary>
    private async Task AfterRequestEndedAsync(DataSyncLinkDbModel? link, DataSyncLinkWrite how, CancellationToken ct)
    {
        if (link is null || how != DataSyncLinkWrite.Transition) return;
        if (link.State == DataSyncLinkState.Stopped)
        {
            _state.TakePull(link.Id);
            _state.DropPreview(link.Id);
        }

        await ObserveAsync(o => o.LinkChangedAsync(link, ct));
    }

    /// <summary>
    /// The person withdrew the request a link waits for. A link made for that request has nothing yet — no first
    /// contact, no cursor, no base, and so no hold — and goes with it. Any other link keeps its state: one turned back
    /// on after a stop, or one asked again after its access was revoked, stops again as an ended request would
    /// (<see cref="AccessCancelled"/>), so its bases, pending records and last mode are kept and its holds are released
    /// (§8.1) — under <paramref name="gate"/>, which the caller took for it (<see cref="WithdrawingStopsAsync"/>).
    /// Without it such a link is left as it is: the fetch cycle ends the request it no longer finds, under the gate.
    /// </summary>
    public async Task OnRequestCancelledAsync(int linkId, CancellationToken ct, DataSyncGateHold? gate = null)
    {
        DataSyncLinkDbModel? removed = null;
        var (ended, how) = await WriteAsync(async store =>
        {
            var row = await store.GetLinkAsync(linkId, ct);
            if (row is not { State: DataSyncLinkState.AwaitingAccess }) return (null, DataSyncLinkWrite.None);
            if (await HasSyncStateAsync(store, row, ct))
            {
                return gate is null
                    ? (null, DataSyncLinkWrite.None)
                    : await EndRequestAsync(store, row, AccessCancelled, ct);
            }

            await store.DeleteLinkAsync(linkId, ct);
            removed = row;
            return ((DataSyncLinkDbModel?) null, DataSyncLinkWrite.None);
        }, ct);

        if (removed is not null) await AfterRemovedAsync(removed, ct);
        await AfterRequestEndedAsync(ended, how, ct);
    }

    /// <summary>
    /// Whether withdrawing the request link <paramref name="linkId"/> waits for stops it
    /// (<see cref="OnRequestCancelledAsync"/>): the stop releases its holds, so the caller enters the gate first.
    /// </summary>
    public async Task<bool> WithdrawingStopsAsync(int linkId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var store = Store(scope);
        return await store.GetLinkAsync(linkId, ct) is { State: DataSyncLinkState.AwaitingAccess } row &&
               await HasSyncStateAsync(store, row, ct);
    }

    /// <summary>Whether a link holds anything a reset would delete: a first contact, a cursor or a base.</summary>
    private static async Task<bool> HasSyncStateAsync(IDataSyncStore store, DataSyncLinkDbModel link,
        CancellationToken ct)
    {
        if (link.FirstContactCompletedAtUtc is not null || link.GetCursors().Count > 0) return true;

        foreach (var kind in DataSyncKindIds.All)
        {
            if ((await store.GetBasesAsync(link.Id, kind, ct)).Count > 0) return true;
        }

        return false;
    }

    // ---- after the apply task ----------------------------------------------------------------------------------

    /// <summary>
    /// After <see cref="IDataSyncApplyRunner.RunAutoSyncAsync"/> committed an apply, which recorded what it did on the
    /// link in its own transaction (<see cref="DataSyncLinkColumns.RecordApplied"/>), or paused the link: tells the
    /// observer what was applied, or the pause, whose pull is dropped.
    /// </summary>
    public async Task AfterAutoSyncAsync(int linkId, DataSyncAutoSyncOutcome outcome, CancellationToken ct)
    {
        if (await GetAsync(linkId, ct) is not { } link) return;
        if (outcome.Paused is not null)
        {
            _state.TakePull(link.Id);
            await ObserveAsync(o => o.LinkPausedAsync(link, ct));
        }
        else if (outcome.End == DataSyncAutoSyncEnd.Committed)
        {
            await ObserveAsync(o => o.AutoSyncAppliedAsync(link, outcome, outcome.FirstSync, ct));
        }
    }

    /// <summary>An attempt on this link failed: the error, and a backoff before the next one.</summary>
    public async Task<DataSyncLinkDbModel?> RecordFailureAsync(int linkId, string code, string? detail,
        int? retryAfterSeconds, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var link = await MutateAsync(linkId, row =>
        {
            row.ConsecutiveFailures++;
            row.LastErrorCode = code;
            row.LastErrorDetail = Truncate(detail);
            return DataSyncLinkWrite.Bookkeeping;
        }, ct);
        if (link is not null)
            _state.RecordAttempt(linkId, now, now + DataSyncSchedule.Backoff(link.ConsecutiveFailures, retryAfterSeconds));
        return link;
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

    /// <summary>The peer revoked this device's access (§8.1), on a link that is neither paused nor stopped.</summary>
    private static bool IsAccessLost(DataSyncLinkDbModel link) =>
        link.LastErrorCode == nameof(DataSyncPeerErrorCode.AccessRevoked) &&
        link.State is not (DataSyncLinkState.Paused or DataSyncLinkState.Stopped);

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

    private async Task<bool> PeerMayReadUsAsync(string peerNodeId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return (await Grants(scope).GetGrantsAsync(ct))
            .Any(g => string.Equals(g.NodeId, peerNodeId, StringComparison.Ordinal));
    }

    private async Task<bool> HasAccessAsync(string peerNodeId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await Grants(scope).HasOutboundGrantAsync(peerNodeId, ct);
    }

    private async Task<string> PeerNameAsync(string peerNodeId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var candidate = (await Grants(scope).GetPeersAsync(false, ct))
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

    private static DataSyncRequestIntent IntentOf(DataSyncLinkMode mode) =>
        mode == DataSyncLinkMode.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow;

    /// <summary>
    /// Sends a datasync request (§7.2.2) — the one send every action shares, each keeping its own row write — or says
    /// why not, before anything changed: two-way needs its switches (<see cref="RefuseTwoWayAsync"/>), and only a caller
    /// who may create access sends anything (§7.1.5). <paramref name="forgetDeadCredentials"/> first drops what the
    /// peer refused (<see cref="ForgetDeadCredentialsAsync"/>). A refusal carries <paramref name="link"/> as it is.
    /// </summary>
    private async Task<(DataSyncAccessRequestOutcome? Outcome, DataSyncLinkChange? Refused)> AskAsync(
        DataSyncAccessRequestInput input, bool callerMayCreateAccess, DataSyncLinkDbModel? link, CancellationToken ct,
        bool forgetDeadCredentials = false)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var grants = Grants(scope);
        if (input.Intent == DataSyncRequestIntent.TwoWay && await RefuseTwoWayAsync(grants, ct) is { } twoWay)
            return (null, DataSyncLinkChange.Refused(twoWay, null, link));
        if (!callerMayCreateAccess)
            return (null, DataSyncLinkChange.Refused(DataSyncProblemCode.NotAllowedOnThisDevice, null, link));
        if (forgetDeadCredentials && await ForgetDeadCredentialsAsync(grants, input.PeerNodeId!, ct) is { } forgetting)
            return (null, new DataSyncLinkChange(link, null, forgetting));

        DataSyncProblem problem;
        try
        {
            var outcome = await grants.RequestAccessAsync(input, ct);
            if (outcome.Outcome != "rejected") return (outcome, null);
            problem = new DataSyncProblem(
                input.Code is null ? DataSyncProblemCode.AccessMissing : DataSyncProblemCode.InvitationInvalid, "rejected");
        }
        catch (DataSyncPeerException e)
        {
            problem = new DataSyncProblem(ProblemOf(e.Code), e.Code.ToCode());
        }
        catch (DataSyncProblemException e)
        {
            // This device refused on the way (sharing or remote access off, a wrong code): the answer as it is.
            problem = e.Problem;
        }

        return (null, new DataSyncLinkChange(link, null, problem));
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

    /// <summary>
    /// Called on a row about to be turned on (Follow or two-way): a copy once still waiting for its Start or its access
    /// (§8.1: <c>Off</c> in AwaitingReview or AwaitingAccess) drops its snapshot, so the link's next cycle stages a first
    /// sync under its new mode. A Start already running reads the link's mode in its own transaction, as it is then.
    /// </summary>
    private void ForgetCopyOnceReview(DataSyncLinkDbModel row)
    {
        if (row.Mode == DataSyncLinkMode.Off &&
            row.State is DataSyncLinkState.AwaitingReview or DataSyncLinkState.AwaitingAccess)
            _state.DropPreview(row.Id);
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
