using System.Collections.Concurrent;
using System.Text;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests.DataSync.Runtime;

// What a runtime test plays or breaks: the peers and their feed, the grant service, the actor guard, the apply runner,
// the page reader, the observer and the row transactions. Everything else is the harness's real composition.

/// <summary>One peer as the feed serves it: its head, a snapshot per manifest, and pages the fake reader decodes.</summary>
internal sealed class FakePeer(string nodeId)
{
    public string NodeId { get; } = nodeId;
    public string ServedNodeId { get; set; } = nodeId;
    public string Epoch { get; set; } = "epoch-1";
    public string ActorId { get; set; } = "0123456789abcdef";
    public int Contract { get; set; } = DataSyncContract.Version;
    public int MinimumPeerContract { get; set; } = DataSyncContract.MinimumPeerVersion;
    public Dictionary<string, long> MaxSeq { get; } = new() { ["extensionGroup"] = 3, ["customProperty"] = 5 };
    public HashSet<string> Superseded { get; } = [];
    public DataSyncFeedCounterpart? Counterpart { get; set; }
    public DataSyncSourceAttention Attention { get; set; } = new(false, 0, 0, false, 0);
    public long? SeenCounter { get; set; }
    public int PagesPerKind { get; set; } = 1;
    public string? BadPageOf { get; set; }

    public Queue<DataSyncPeerException> HeadErrors { get; } = new();
    public Queue<DataSyncPeerException> ManifestErrors { get; } = new();
    public Queue<DataSyncPeerException> PageErrors { get; } = new();

    /// <summary>Awaited at the start of every head: lets a test hold a fetch cycle.</summary>
    public Func<CancellationToken, Task>? BeforeHead { get; set; }

    public ConcurrentQueue<DataSyncFeedQuery> HeadQueries { get; } = new();
    public ConcurrentQueue<DataSyncFeedQuery> ManifestQueries { get; } = new();
    public int Pages;
    public int Manifests;

    public DataSyncFeedHead Head() => new(ServedNodeId, Epoch, ActorId, Contract, MinimumPeerContract, "2.4.0-beta.1",
        MaxSeq.Values.DefaultIfEmpty().Max(),
        MaxSeq.Select(k => new DataSyncFeedKindHead(k.Key, 1, k.Value, Superseded.Contains(k.Key))).ToList(),
        Attention, SeenCounter, Counterpart);

    public DataSyncFeedManifest Manifest(DataSyncFeedQuery query)
    {
        var id = Interlocked.Increment(ref Manifests);
        var kinds = query.Since.Where(s => MaxSeq.ContainsKey(s.Key)).Select(s =>
        {
            var superseded = Superseded.Contains(s.Key);
            return new DataSyncFeedKind(s.Key, 1, MaxSeq[s.Key], 0, 10, 0, "sha256:x", superseded ? 0 : s.Value, 1,
                superseded);
        }).ToList();
        return new DataSyncFeedManifest($"snap-{id}", 120_000, ServedNodeId, Epoch, ActorId, Contract,
            MinimumPeerContract, "2.4.0-beta.1", kinds, Counterpart, Attention);
    }

    /// <summary>Called after each page is counted, before it is served.</summary>
    public Action? OnPage { get; set; }

    /// <summary>
    /// Serves the page bytes instead of the fake reader's (snapshot id, kind, since, cursor): a scripted source for
    /// the real page reader.
    /// </summary>
    public Func<string, string, long, string?, byte[]>? Serve { get; set; }

    public byte[] Page(string snapshotId, string kind, long sinceSeq, string? cursor)
    {
        Interlocked.Increment(ref Pages);
        OnPage?.Invoke();
        if (Serve is { } serve) return serve(snapshotId, kind, sinceSeq, cursor);
        if (kind == BadPageOf) return "bad"u8.ToArray();
        var index = cursor is null ? 1 : int.Parse(cursor[1..]);
        return Encoding.UTF8.GetBytes($"page:{index}:{PagesPerKind}");
    }
}

internal sealed class FakeDataSyncPeerClient : IDataSyncPeerClient
{
    public ConcurrentDictionary<string, FakePeer> Peers { get; } = new();

    public FakePeer Add(string nodeId) => Peers[nodeId] = new FakePeer(nodeId);

    private FakePeer Peer(string nodeId) =>
        Peers.TryGetValue(nodeId, out var peer)
            ? peer
            : throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, "unknown peer");

    public async Task<DataSyncFeedHead> GetHeadAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        if (peer.BeforeHead is { } before) await before(ct);
        peer.HeadQueries.Enqueue(query);
        if (peer.HeadErrors.TryDequeue(out var error)) throw error;
        return peer.Head();
    }

    public Task<DataSyncFeedManifest> GetManifestAsync(string peerNodeId, DataSyncFeedQuery query,
        CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        peer.ManifestQueries.Enqueue(query);
        if (peer.ManifestErrors.TryDequeue(out var error)) throw error;
        return Task.FromResult(peer.Manifest(query));
    }

    public Task<ReadOnlyMemory<byte>> GetPageAsync(string peerNodeId, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        if (peer.PageErrors.TryDequeue(out var error)) throw error;
        return Task.FromResult<ReadOnlyMemory<byte>>(peer.Page(snapshotId, kind, sinceSeq, cursor));
    }
}

/// <summary>Decodes the fake pages (<c>page:{index}:{count}</c>); a staged kind carries no entities.</summary>
internal sealed class FakeKindPageReader : IDataSyncKindPageReader
{
    public HashSet<string> Kinds { get; } = ["extensionGroup", "customProperty"];

    public bool Supports(string kind) => Kinds.Contains(kind);

    public IDataSyncKindPageAssembly Begin(string kind, string snapshotId, DataSyncFeedKind manifestKind,
        bool fullReconciliation) => new Assembly(kind, manifestKind, fullReconciliation);

    private sealed class Assembly(string kind, DataSyncFeedKind manifestKind, bool full) : IDataSyncKindPageAssembly
    {
        private bool _complete;

        public string? Problem { get; private set; }

        public DataSyncPageStep Add(ReadOnlyMemory<byte> page)
        {
            var text = Encoding.UTF8.GetString(page.Span);
            if (!text.StartsWith("page:", StringComparison.Ordinal))
            {
                Problem = "corrupted";
                return new DataSyncPageStep(false, null, false);
            }

            var parts = text.Split(':');
            var index = int.Parse(parts[1]);
            var count = int.Parse(parts[2]);
            _complete = index >= count;
            return new DataSyncPageStep(true, _complete ? null : $"p{index + 1}", _complete);
        }

        public DataSyncStagedKind? Complete()
        {
            if (!_complete)
            {
                Problem = "corrupted";
                return null;
            }

            return new DataSyncStagedKind(kind, 1, true, null, [], manifestKind.MaxSeq, full);
        }
    }
}

internal sealed class FakeDataSyncGrantService : IDataSyncGrantService
{
    public HashSet<string> Outbound { get; } = [];
    public List<DataSyncGrantView> Readers { get; } = [];
    public List<DataSyncPeerCandidate> Peers { get; } = [];
    public List<DataSyncAccessRequestView> Requests { get; } = [];
    public List<DataSyncAccessRequestInput> Sent { get; } = [];
    public bool SharingEnabled { get; set; } = true;
    public RemoteAccessMode RemoteAccessMode { get; set; } = RemoteAccessMode.Enabled;

    /// <summary>What a request answers; by default it waits for approval.</summary>
    public Func<DataSyncAccessRequestInput, DataSyncAccessRequestOutcome> Answer { get; set; } = input =>
        new DataSyncAccessRequestOutcome("awaitingApproval", "req-" + (input.PeerNodeId ?? input.Address),
            input.PeerNodeId ?? "node-by-address", "Peer by address", null);

    /// <summary>Every call that changed access, in order (<c>approve:{id}:{readBack}</c>, <c>revoke:{node}</c>, …).</summary>
    public ConcurrentQueue<string> Changes { get; } = new();

    /// <summary>What an approval answers; by default the request's peer, read back when asked for two-way.</summary>
    public Func<DataSyncAccessRequestView, bool, DataSyncApprovalOutcome>? Approval { get; set; }

    /// <summary>Thrown by the next access-changing call, as the federation side refuses on this device.</summary>
    public DataSyncProblem? Refuse { get; set; }

    public Task<bool> IsSharingEnabledAsync(CancellationToken ct) => Task.FromResult(SharingEnabled);

    public Task SetSharingEnabledAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"sharing:{enabled}:{enablePairedRemoteAccess}");
        SharingEnabled = enabled;
        // Only from Disabled, never touching Enabled or Unrestricted (§7.1.3).
        if (enabled && enablePairedRemoteAccess && RemoteAccessMode == RemoteAccessMode.Disabled)
            RemoteAccessMode = RemoteAccessMode.Enabled;
        return Task.CompletedTask;
    }

    private void ThrowIfRefused()
    {
        if (Refuse is { } problem)
        {
            Refuse = null;
            throw new DataSyncProblemException(problem);
        }
    }

    public Task<RemoteAccessMode> GetRemoteAccessModeAsync(CancellationToken ct) => Task.FromResult(RemoteAccessMode);

    public Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DataSyncPeerCandidate>>(Peers.ToList());

    public Task<DataSyncAccessRequestOutcome> RequestAccessAsync(DataSyncAccessRequestInput input, CancellationToken ct)
    {
        lock (Sent) Sent.Add(input);
        return Task.FromResult(Answer(input));
    }

    /// <summary>
    /// The clock the listing is read at. Set, the listing leaves out every request whose time ran out, as
    /// <c>FederationPeerService.GetDataSyncStatusAsync</c> does: an expired request is never listed as expired, it is
    /// simply gone.
    /// </summary>
    public Func<DateTime>? Now { get; set; }

    public Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DataSyncAccessRequestView>>(Requests
            .Where(r => Now is not { } now || r.ExpiresAt > now()).ToList());

    public Task<DataSyncApprovalOutcome> ApproveAsync(string requestId, bool readBack, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"approve:{requestId}:{readBack}");
        var request = Requests.Single(r => r.RequestId == requestId);
        var outcome = Approval?.Invoke(request, readBack) ?? new DataSyncApprovalOutcome(request.NodeId,
            request.NodeName, request.Intent, readBack && request.Intent == DataSyncRequestIntent.TwoWay, null);
        lock (Readers) Readers.Add(new DataSyncGrantView(request.NodeId, request.NodeName));
        if (outcome.ReadBackGranted) Outbound.Add(request.NodeId);
        return Task.FromResult(outcome);
    }

    public Task RejectAsync(string requestId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"reject:{requestId}");
        return Task.CompletedTask;
    }

    public Task CancelOutgoingAsync(string requestId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"cancel:{requestId}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataSyncGrantView>> GetGrantsAsync(CancellationToken ct)
    {
        lock (Readers) return Task.FromResult<IReadOnlyList<DataSyncGrantView>>(Readers.ToList());
    }

    public Task RevokeAsync(string peerNodeId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"revoke:{peerNodeId}");
        lock (Readers) Readers.RemoveAll(r => r.NodeId == peerNodeId);
        return Task.CompletedTask;
    }

    /// <summary>A code whose expiry comes back without a kind, as a stored time would.</summary>
    public Task<DataSyncInvitationView> CreateInvitationAsync(CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue("invite");
        return Task.FromResult(new DataSyncInvitationView("48213705",
            DateTime.SpecifyKind(new DateTime(2026, 9, 1, 8, 10, 0), DateTimeKind.Unspecified),
            ["http://192.168.1.20:5000"]));
    }

    public Task<bool> HasOutboundGrantAsync(string peerNodeId, CancellationToken ct) =>
        Task.FromResult(Outbound.Contains(peerNodeId));

    public Task ForgetOutboundAsync(string peerNodeId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"forget:{peerNodeId}");
        Outbound.Remove(peerNodeId);
        return Task.CompletedTask;
    }
}

/// <param name="Link">The link row as the runner found it.</param>
/// <param name="Choices">A first sync's Start: what the person chose in its preview.</param>
internal sealed record AutoSyncCall(DataSyncLinkDbModel Link, DataSyncStagedPull? Pull, string TaskId,
    DataSyncTaskAttempt? Attempt, IReadOnlyList<DataSyncFirstSyncChoice>? Choices = null);

/// <summary>
/// The runner's shape without a merge: a committed apply records itself on the link as the real runner does
/// (<see cref="DataSyncLinkColumns.RecordApplied"/>), with no cursor advance.
/// </summary>
internal sealed class FakeApplyRunner(IDataSyncTaskRegistry registry, DataSyncLinkService links, IDataSyncClock clock)
    : IDataSyncApplyRunner
{
    public ConcurrentQueue<AutoSyncCall> AutoSyncs { get; } = new();
    public ConcurrentQueue<(DataSyncRestoreChoice Choice, int? LinkId)> Restores { get; } = new();
    public ConcurrentQueue<int> Undos { get; } = new();

    /// <summary>Awaited by every call: lets a test hold an apply inside its body.</summary>
    public Func<CancellationToken, Task>? Hold { get; set; }

    public Func<AutoSyncCall, DataSyncAutoSyncOutcome> AutoSyncOutcome { get; set; } =
        _ => new DataSyncAutoSyncOutcome(1, null, 0, 0, 1, [], [], DataSyncAutoSyncEnd.Committed);

    public Queue<Exception> UndoErrors { get; } = new();
    public Queue<Exception> AutoSyncErrors { get; } = new();

    /// <summary>Whether the attempt was still current when the runner would have entered the gate.</summary>
    public ConcurrentQueue<bool> AttemptCurrentAtGate { get; } = new();

    public async Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args,
        IReadOnlyList<DataSyncFirstSyncChoice>? choices = null)
    {
        AttemptCurrentAtGate.Enqueue(registry.ShouldRunCurrent());
        if (Hold is { } hold) await hold(args.CancellationToken);
        args.CancellationToken.ThrowIfCancellationRequested();
        var call = new AutoSyncCall((await links.GetAsync(linkId, args.CancellationToken))!, pull, args.Task.Id,
            DataSyncTaskAttempts.Current, choices);
        AutoSyncs.Enqueue(call);
        lock (AutoSyncErrors)
        {
            if (AutoSyncErrors.TryDequeue(out var error)) throw error;
        }

        var outcome = AutoSyncOutcome(call);
        if (outcome is not { Paused: null, End: DataSyncAutoSyncEnd.Committed }) return outcome;
        var firstSync = false;
        await links.MutateAsync(linkId, row =>
        {
            firstSync = row.RecordApplied(pull, new Dictionary<string, long>(), clock.UtcNow);
            return DataSyncLinkWrite.Bookkeeping;
        }, args.CancellationToken);
        return outcome with { FirstSync = firstSync };
    }

    public Task<int?> RunResolutionsAsync(IReadOnlyList<DataSyncResolveInput> resolutions, DataSyncApplyOptions options,
        BTaskArgs args) => Task.FromResult<int?>(1);

    public async Task<int?> RunUndoAsync(int applyLogId, BTaskArgs args)
    {
        if (Hold is { } hold) await hold(args.CancellationToken);
        Undos.Enqueue(applyLogId);
        lock (UndoErrors)
        {
            if (UndoErrors.TryDequeue(out var error)) throw error;
        }

        return applyLogId + 100;
    }

    public async Task<int?> RunRestoreAsync(DataSyncRestoreChoice choice, int? linkId, BTaskArgs args)
    {
        if (Hold is { } hold) await hold(args.CancellationToken);
        Restores.Enqueue((choice, linkId));
        return 1;
    }
}

internal sealed class FakeActorGuard : IDataSyncActorGuard
{
    private bool _verified = true;
    public bool IsVerified { get => _verified && !HasPendingEvidence; set => _verified = value; }
    public bool HasPendingEvidence { get; set; }
    public ConcurrentQueue<(string Peer, string Actor, long Counter)> Evidence { get; } = new();
    public int Checks;

    /// <summary>Awaited by every evidence report, before it is recorded: lets a test hold one as a rotation would.</summary>
    public Func<string, CancellationToken, Task>? BeforeEvidence { get; set; }

    public Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct)
    {
        Interlocked.Increment(ref Checks);
        return Task.FromResult<DataSyncPauseReason?>(null);
    }

    public async Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct)
    {
        if (BeforeEvidence is { } before) await before(peerNodeId, ct);
        Evidence.Enqueue((peerNodeId, actorId, seenCounter));
    }

    public Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct) => Task.CompletedTask;
    public void MarkVerified() => IsVerified = true;
}

internal sealed class RecordingObserver : IDataSyncRuntimeObserver
{
    public ConcurrentQueue<string> Events { get; } = new();

    public Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Record($"changed:{link.Id}:{link.State}");
    public Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct) => Record($"removed:{removed.Id}");
    public Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Record($"paused:{link.Id}:{link.PausedReason}");
    public Task StateChangedAsync(CancellationToken ct) => Record("state");

    public Task ReviewReadyAsync(DataSyncLinkDbModel link, CancellationToken ct) => Record($"review:{link.Id}");

    public Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct) => Record($"applied:{link.Id}:{(firstSync ? "first" : "next")}");

    /// <summary>Called as a task's end is heard, before it is recorded: what a test looks at then.</summary>
    public Action<string>? OnTaskEnded { get; set; }

    public Task TaskEndedAsync(string taskId, CancellationToken ct)
    {
        OnTaskEnded?.Invoke(taskId);
        return Record($"ended:{taskId}");
    }

    public int Count(string prefix) => Events.Count(e => e.StartsWith(prefix, StringComparison.Ordinal));

    private Task Record(string e)
    {
        Events.Enqueue(e);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The database's single writer as the runtime's row transactions meet it: <c>BEGIN IMMEDIATE</c> waits while another
/// transaction holds the write lock. A test holds it as the apply runner's open transaction would
/// (<see cref="Hold"/>) and sees who waits for it.
/// </summary>
internal sealed class FakeRowTransactions : IDataSyncRowTransactions
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _waiting;

    /// <summary>Transactions waiting for the write lock now.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    public async Task<IDataSyncRowTransaction> BeginAsync(IServiceProvider scope, CancellationToken ct)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            await _writeLock.WaitAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        return new Transaction(_writeLock);
    }

    /// <summary>Holds the write lock, as another transaction would, until disposed.</summary>
    public IDisposable Hold()
    {
        if (!_writeLock.Wait(0)) throw new InvalidOperationException("The write lock is already held.");
        return new Held(_writeLock);
    }

    private sealed class Transaction(SemaphoreSlim writeLock) : IDataSyncRowTransaction
    {
        private int _released;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) writeLock.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Held(SemaphoreSlim writeLock) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) writeLock.Release();
        }
    }
}

/// <summary>Federation sessions as the scheduler reads them: the peers whose session is verified now.</summary>
internal sealed class FakePeerSessions : IDataSyncPeerSessions
{
    public ConcurrentDictionary<string, byte> Online { get; } = new(StringComparer.Ordinal);

    public bool IsOnline(string peerNodeId) => Online.ContainsKey(peerNodeId);
}

internal sealed class FakeHostLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;
    public void StopApplication() => _stopping.Cancel();
}
