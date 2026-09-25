using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>
/// The source side of the feed (§7.5): what a reader holding a <c>datasync.read</c> grant reads of this device's
/// definitions. D's node controller validates the query and builds the reader from the grant; this class answers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Head</b> (§7.5.1): the reader-ahead check first, then a Refresh at most 5 s old (after the actor check, never
/// while the actor is unverified), then the sequence heads, attention, the reader's <c>SeenCounter</c> and this
/// device's counterpart link to the reader. It answers even while the actor is unverified or a restore waits, so
/// peers can still verify each other.
/// </para>
/// <para>
/// <b>Manifest</b> (§7.5.2): the reader-ahead check; <c>SourceRestorePending</c> while a restore choice waits and
/// <c>Busy</c> while the actor is unverified; then, under the gate, the actor check and a Refresh that collects what
/// every entity publishes, and the whole snapshot built from exactly that: records, pages, hashes and counts. Pages
/// are precomputed, so <see cref="GetPageAsync"/> never takes the gate.
/// </para>
/// <para>
/// <b>Reader ahead</b> (§5.6, gate fix B1(b)): a reader whose cursor is above <c>LastSeq</c> has seen sequence numbers
/// this database never issued, so it is reported to the actor guard before any Refresh can issue a counter. Once
/// recorded, it is not reported again and is served every kind from 0 (<c>CursorSuperseded</c>) until it reads a
/// manifest with every cursor at or below <c>LastSeq</c>; that manifest is still served from 0, and settles it. A head
/// never settles: a reader whose stale cursor fell below <c>LastSeq</c> only because this device made new changes
/// must still read everything once.
/// </para>
/// </remarks>
public sealed class DataSyncFeedSource : IDataSyncFeedSource
{
    private static readonly Lazy<string> CurrentAppVersion = new(() => AppService.CoreVersion.ToString());

    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncGate _gate;
    private readonly DataSyncActorGuard _guard;
    private readonly DataSyncRefreshCoordinator _coordinator;
    private readonly DataSyncFeedSnapshots _snapshots;
    private readonly DataSyncSeenCounters _seenCounters;
    private readonly IDataSyncFeedPageWriter _writer;
    private readonly DataSyncLimits _limits;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public DataSyncFeedSource(IServiceScopeFactory scopes, DataSyncGate gate, DataSyncActorGuard guard,
        DataSyncRefreshCoordinator coordinator, DataSyncFeedSnapshots snapshots, DataSyncSeenCounters seenCounters,
        IDataSyncFeedPageWriter writer, DataSyncLimits? limits = null, TimeProvider? time = null,
        ILogger<DataSyncFeedSource>? logger = null)
    {
        _scopes = scopes;
        _gate = gate;
        _guard = guard;
        _coordinator = coordinator;
        _snapshots = snapshots;
        _seenCounters = seenCounters;
        _writer = writer;
        _limits = limits ?? DataSyncLimits.Default;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger) NullLogger.Instance;
    }

    /// <summary>How long a manifest waits for the gate before it answers Busy (§7.5.1).</summary>
    public TimeSpan GateTimeout { get; init; } = DataSyncGate.RequestTimeout;

    /// <summary>This build's version, as heads and manifests tell it (§8.12).</summary>
    public string AppVersion { get; init; } = CurrentAppVersion.Value;

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    #region Head (§7.5.1)

    public async Task<DataSyncFeedHead> GetHeadAsync(DataSyncReader reader, DataSyncFeedQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = _scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
        var kinds = SupportedKinds(store);

        // 1. Before any Refresh can issue a counter.
        var check = await CheckReaderAheadAsync(reader, query, store, ct);

        // 2. A Refresh at most 5 s old, after the actor check; skipped (no writes) while the actor is unverified.
        try
        {
            await _coordinator.EnsureRecentAsync(kinds.Select(k => k.Codec.Descriptor.Kind).ToList(), ct);
        }
        catch (DataSyncGateTimeoutException)
        {
            throw DataSyncFeedErrors.Busy();
        }

        var state = await store.GetLocalStateAsync(ct) ?? await CreateStateAsync(store, ct);

        // 3. Fields.
        var maxSeqs = await store.GetKindMaxSeqsAsync(ct);
        var heads = kinds.Select(adapter =>
        {
            var kind = adapter.Codec.Descriptor.Kind;
            var superseded = check.Recorded || DataSyncCursorRules.IsSuperseded(query.Since.GetValueOrDefault(kind),
                DataSyncCursorRules.FloorOf(state, kind), check.LastSeq, false);
            return new DataSyncFeedKindHead(kind, adapter.Codec.Descriptor.SchemaVersion,
                maxSeqs.GetValueOrDefault(kind), superseded, adapter.Codec.ComparisonFormVersion);
        }).ToList();
        var counterpart = await CounterpartAsync(store, reader, ct);

        // 4. and 5.
        var seenCounter = await _seenCounters.GetAsync(query.ReaderActorId, ct);
        var attention = await store.GetAttentionAsync(ct);

        await store.TouchReaderAsync(reader, query, state.LastSeq, UtcNow, ct);
        return new DataSyncFeedHead(state.NodeId, state.LibraryEpoch, state.ActorId, DataSyncContract.Version,
            DataSyncContract.MinimumPeerVersion, AppVersion, state.LastSeq, heads, attention, seenCounter, counterpart);
    }

    /// <summary>
    /// The first head of a device with no synced kind finds no local state, since no Refresh ran: the actor check
    /// creates it (§4.5), under the gate like every check.
    /// </summary>
    private async Task<DataSyncLocalStateDbModel> CreateStateAsync(DataSyncStore store, CancellationToken ct)
    {
        try
        {
            using var lease = await _gate.EnterAsync(GateTimeout, ct);
            await _guard.CheckAsync(lease, ct);
        }
        catch (DataSyncGateTimeoutException)
        {
            throw DataSyncFeedErrors.Busy();
        }

        return await store.GetLocalStateAsync(ct) ??
               throw new InvalidOperationException("The actor check created no local state (§5.6).");
    }

    #endregion

    #region Manifest (§7.5.2)

    public async Task<DataSyncFeedManifest> CreateSnapshotAsync(DataSyncReader reader, DataSyncFeedQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = _scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
        var kinds = SupportedKinds(store).Where(k => query.Since.ContainsKey(k.Codec.Descriptor.Kind)).ToList();

        // 1. The reader-ahead check.
        var check = await CheckReaderAheadAsync(reader, query, store, ct);

        // 2. A restore choice waits, or the actor is still unverified: no snapshot, but no gate wait either.
        ThrowUnlessServing(await store.GetLocalStateAsync(ct));
        _snapshots.Admit(reader.GrantId);

        DataSyncGateLease lease;
        try
        {
            lease = await _gate.EnterAsync(GateTimeout, ct);
        }
        catch (DataSyncGateTimeoutException)
        {
            throw DataSyncFeedErrors.Busy();
        }

        using (lease)
        {
            // 3. The actor check, then the Refresh the snapshot is built from.
            await _guard.CheckAsync(lease, ct);
            ThrowUnlessServing(await store.GetLocalStateAsync(ct));
            var published = await RefreshAsync(lease, kinds.Select(k => k.Codec.Descriptor.Kind).ToList(), ct);
            var state = await store.GetLocalStateAsync(ct) ??
                        throw new InvalidOperationException("The actor check created no local state (§5.6).");
            ThrowUnlessServing(state);

            // 4.–7. Records, pages, hashes and counts, from exactly what this Refresh published.
            var snapshotId = Guid.NewGuid().ToString("N");
            var maxSeqs = await store.GetKindMaxSeqsAsync(ct);
            var feedKinds = new List<DataSyncFeedKind>(kinds.Count);
            var snapshotKinds = new Dictionary<string, DataSyncFeedSnapshotKind>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var adapter in kinds)
            {
                ct.ThrowIfCancellationRequested();
                var kind = adapter.Codec.Descriptor.Kind;
                var floor = DataSyncCursorRules.FloorOf(state, kind);
                var superseded = DataSyncCursorRules.IsSuperseded(query.Since[kind], floor, check.LastSeq,
                    check.Recorded);
                var sinceSeq = superseded ? 0 : query.Since[kind];
                var written = await WriteKindAsync(store, snapshotId, kind, adapter.Codec.Descriptor, sinceSeq,
                    published, ct);
                bytes += written.Bytes;
                if (bytes > _limits.MaxSnapshotBytes) throw DataSyncFeedErrors.SnapshotTooLarge();

                var (live, tombstones) = await store.CountPublishedAsync(kind, ct);
                feedKinds.Add(new DataSyncFeedKind(kind, adapter.Codec.Descriptor.SchemaVersion,
                    maxSeqs.GetValueOrDefault(kind), floor, live, tombstones, written.ContentHash, sinceSeq,
                    written.Records.Count, superseded));
                snapshotKinds[kind] = new DataSyncFeedSnapshotKind(kind, sinceSeq, written.Pages,
                    written.Cursors.Select((cursor, index) => (Cursor: cursor ?? "", Index: index))
                        .ToDictionary(p => p.Cursor, p => p.Index, StringComparer.Ordinal));
            }

            _snapshots.Store(new DataSyncFeedSnapshot(snapshotId, reader.GrantId, reader.NodeId, snapshotKinds, bytes),
                _limits.MaxSnapshotBytesTotal);

            var manifest = new DataSyncFeedManifest(snapshotId, (long) DataSyncFeedSnapshots.Ttl.TotalMilliseconds,
                state.NodeId, state.LibraryEpoch, state.ActorId, DataSyncContract.Version,
                DataSyncContract.MinimumPeerVersion, AppVersion, feedKinds, await CounterpartAsync(store, reader, ct),
                await store.GetAttentionAsync(ct));
            await store.TouchReaderAsync(reader, query, state.LastSeq, UtcNow, ct);

            // B1(b): a recorded reader that read with every cursor ≤ LastSeq was just served everything from 0.
            if (check is {Recorded: true, Ahead: false}) await _guard.NoteReaderInStepAsync(reader.NodeId, ct);

            _logger.LogDebug("Data sync snapshot {Snapshot} for {Reader}: {Kinds} kinds, {Bytes} bytes.", snapshotId,
                reader.NodeId, feedKinds.Count, bytes);
            return manifest;
        }
    }

    /// <summary>
    /// Refresh with <c>collectPublished</c> (§6.6), retried once after the actor check when it finds the actor
    /// changed (§5.6). Skipped means the actor became unverified meanwhile: <c>Busy</c>, retry after 30 s.
    /// </summary>
    private async Task<IReadOnlyDictionary<(string Kind, string LocalKey), DataSyncPublishedEntity>> RefreshAsync(
        DataSyncGateLease lease, IReadOnlyCollection<string> kinds, CancellationToken ct)
    {
        if (kinds.Count == 0) return new Dictionary<(string, string), DataSyncPublishedEntity>();
        for (var attempt = 0;; attempt++)
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<DataSyncRefresher>()
                    .RefreshAsync(lease, kinds, collectPublished: true, ct);
                if (result.Skipped) throw DataSyncFeedErrors.Busy(DataSyncFeedErrors.RetryAfterUnverified);
                _coordinator.NoteRefreshed(kinds);
                return result.Published ?? new Dictionary<(string, string), DataSyncPublishedEntity>();
            }
            catch (DataSyncActorChangedException) when (attempt == 0)
            {
                await _guard.CheckAsync(lease, ct);
            }
        }
    }

    /// <summary>
    /// One kind: the served records with <c>Seq &gt; sinceSeq</c> in Seq order (live Synced entities with what
    /// Refresh published, served tombstones, held entities as <c>HeldAtSource</c>), written as pages and read back.
    /// </summary>
    private async Task<DataSyncFeedWrittenKind> WriteKindAsync(DataSyncStore store, string snapshotId, string kind,
        DataSyncKindDescriptor descriptor, long sinceSeq,
        IReadOnlyDictionary<(string Kind, string LocalKey), DataSyncPublishedEntity> published, CancellationToken ct)
    {
        var rows = await store.GetPublishedChangedSinceAsync(kind, sinceSeq, ct);
        var aliases = await store.GetAliasesByPrimaryAsync(kind, ct);
        var records = rows.Select(row => DataSyncFeedRecords.ToRecord(row, aliases[row.SyncKey], descriptor,
            published.GetValueOrDefault((kind, row.LocalKey)), _limits)).ToList();
        foreach (var row in rows.Where(r => aliases[r.SyncKey].Count() + 1 > _limits.MaxKeysPerEntity))
        {
            _logger.LogWarning("Data sync serves {Kind} {Key} with {Max} of its keys only.", kind, row.SyncKey,
                _limits.MaxKeysPerEntity);
        }

        var pages = _writer.WritePages(snapshotId, kind, sinceSeq, records, _limits);
        return DataSyncFeedPageScanner.Scan(pages, snapshotId, kind, sinceSeq, records);
    }

    private void ThrowUnlessServing(DataSyncLocalStateDbModel? state)
    {
        if (state?.RestoreReason is not null) throw DataSyncFeedErrors.SourceRestorePending();
        if (!_guard.IsVerified) throw DataSyncFeedErrors.Busy(DataSyncFeedErrors.RetryAfterUnverified);
    }

    #endregion

    #region Pages (§7.5.3)

    public Task<byte[]> GetPageAsync(DataSyncReader reader, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_snapshots.GetPage(reader.GrantId, snapshotId, kind, sinceSeq, cursor));
    }

    #endregion

    #region Shared

    /// <summary>The reader-ahead check of §7.5.1 step 1, against <c>LastSeq</c> as it stands before any Refresh.</summary>
    /// <param name="Ahead">Some cursor is above <see cref="LastSeq"/>.</param>
    /// <param name="Recorded">The reader is a recorded reader-ahead that has not settled: serve it every kind from 0.</param>
    private sealed record ReaderCheck(long LastSeq, bool Ahead, bool Recorded);

    private async Task<ReaderCheck> CheckReaderAheadAsync(DataSyncReader reader, DataSyncFeedQuery query,
        DataSyncStore store, CancellationToken ct)
    {
        var lastSeq = (await store.GetLocalStateAsync(ct))?.LastSeq ?? 0;
        var ahead = DataSyncCursorRules.IsReaderAhead(query.Since, lastSeq);
        var recorded = await _guard.IsReaderAheadRecordedAsync(reader.NodeId, ct);
        if (ahead && !recorded)
        {
            await _guard.ReportReaderAheadAsync(reader.NodeId, ct);
            recorded = await _guard.IsReaderAheadRecordedAsync(reader.NodeId, ct);
        }

        return new ReaderCheck(lastSeq, ahead, recorded);
    }

    /// <summary>This device's own link to the reader, told only to that reader (§8.3, N6).</summary>
    private static async Task<DataSyncFeedCounterpart?> CounterpartAsync(DataSyncStore store, DataSyncReader reader,
        CancellationToken ct)
    {
        var link = await store.GetLinkByPeerAsync(reader.NodeId, ct);
        if (link is null) return null;
        var mode = link.Mode switch
        {
            DataSyncLinkMode.Follow => "follow",
            DataSyncLinkMode.TwoWay => "twoWay",
            _ => "off",
        };
        return new DataSyncFeedCounterpart(mode, link.FirstContactCompletedAtUtc is not null,
            DataSyncStoredJson.ReadStrings(link.KindsJson, "KindsJson"));
    }

    /// <summary>The kinds this device publishes: <see cref="DataSyncKindIds.All"/> first, others after them ordinally.</summary>
    private static IReadOnlyList<IDataSyncKind> SupportedKinds(DataSyncStore store)
    {
        var adapters = store.Kinds;
        return DataSyncKindIds.All.Where(adapters.ContainsKey)
            .Concat(adapters.Keys.Except(DataSyncKindIds.All).OrderBy(k => k, StringComparer.Ordinal))
            .Select(k => adapters[k])
            .ToList();
    }

    #endregion
}
