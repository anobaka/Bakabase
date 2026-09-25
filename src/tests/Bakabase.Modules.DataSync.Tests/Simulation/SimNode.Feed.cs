using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>What a head told the reader beyond <see cref="DataSyncFeedHead"/>: whether the manifest would wait.</summary>
internal sealed record SimHead(DataSyncFeedHead Head, bool RestorePending, bool Busy);

// The source side of the feed (§7.5), as package C builds it: the reader-ahead check, Refresh, then records
// written to pages by the real writer and read back by the real reader and assembler.
internal sealed partial class SimNode
{
    /// <summary><c>GET head</c> (§7.5.1) for <paramref name="reader"/>, whose link to this node is <paramref name="readerLink"/>.</summary>
    public SimHead Head(SimNode reader, SimLink readerLink)
    {
        var ahead = ReaderAhead(reader, readerLink);
        if (Verified)
        {
            CheckActor();
            Refresh();
        }

        var kinds = SimKinds.All.Select(k => new DataSyncFeedKindHead(k.Kind, k.Codec.Descriptor.SchemaVersion, MaxSeq(k.Kind),
            ahead || Superseded(k.Kind, readerLink.Cursors.GetValueOrDefault(k.Kind)), k.Codec.ComparisonFormVersion)).ToList();
        var counterpart = Links.GetValueOrDefault(reader.NodeId) is { } back
            ? new DataSyncFeedCounterpart(back.Mode switch
                {
                    DataSyncLinkMode.Follow => "follow",
                    DataSyncLinkMode.TwoWay => "twoWay",
                    _ => "off",
                }, back.CompletedKinds.Count > 0, SimKinds.Ids)
            : null;
        var head = new DataSyncFeedHead(NodeId, Epoch, Db.Local.ActorId, DataSyncContract.Version,
            DataSyncContract.MinimumPeerVersion, "1.0.0", LastSeq, kinds, Attention, SeenCounter(reader.Db.Local.ActorId),
            counterpart);
        return new SimHead(head, RestorePending, !Verified);
    }

    /// <summary>
    /// <c>GET manifest</c> plus every page (§7.5.2–§7.5.4): null while a restore choice is pending here
    /// (<c>SourceRestorePending</c>) or while this node's actor is unverified (<c>Busy</c>).
    /// </summary>
    /// <param name="full">The reader asks for a full reconciliation (every kind from 0, §8.8).</param>
    public DataSyncStagedPull? Snapshot(SimNode reader, SimLink readerLink, bool full)
    {
        var ahead = ReaderAhead(reader, readerLink);
        if (RestorePending || !Verified) return null;
        CheckActor();
        if (RestorePending) return null;
        Refresh();

        var kinds = new List<DataSyncStagedKind>();
        var manifestKinds = new List<DataSyncFeedKind>();
        foreach (var kind in SimKinds.All)
        {
            var cursor = full ? 0 : readerLink.Cursors.GetValueOrDefault(kind.Kind);
            var superseded = ahead || Superseded(kind.Kind, cursor);
            var since = superseded ? 0 : cursor;
            if (superseded) _world.Count("cursorSuperseded");
            else if (full && readerLink.Cursors.GetValueOrDefault(kind.Kind) > 0) _world.Count("fullReconciliation");
            var records = new List<DataSyncWireRecord>();
            foreach (var row in Rows.Where(r => r.Kind == kind.Kind && r.HasSideRow && r.Seq > since).OrderBy(r => r.Seq))
            {
                var keys = row.Keys.Select(k => k.Value).ToList();
                if (row.Deleted)
                {
                    if (!row.Served) continue;
                    records.Add(new DataSyncWireRecord(keys, row.Origin, row.Seq, row.Vv, row.LastEditor, true,
                        kind.Codec.Descriptor.SchemaVersion, null, null, null, null, 0));
                    continue;
                }

                if (row.DeletedLocally || row.State != DataSyncEntitySyncState.Synced) continue;
                var publication = DataSyncPublication.Of(kind.Codec, row.Content!, row.Overlay, row.ChildrenLocal, row.OrderKey,
                    row.Unknown);
                var held = row.PublishHeld ? DataSyncHeldReason.PendingDecision : publication.Held;
                records.Add(new DataSyncWireRecord(keys, row.Origin, row.Seq, row.Vv, row.LastEditor, false,
                    kind.Codec.Descriptor.SchemaVersion, kind.HasOrder ? row.OrderKey : null,
                    held is null ? publication.Content : null, held is null ? publication.Hash : null, held, 0));
            }

            var snapshotId = "snap-" + NodeId;
            var written = DataSyncWireWriter.WriteKind(snapshotId, kind.Kind, since, records, Limits);
            var assembler = new DataSyncRecordAssembler(kind.Codec, Limits);
            foreach (var page in written.Pages) assembler.Add(DataSyncWireReader.ReadPage(page, snapshotId, kind.Kind, Limits));
            var live = Rows.Count(r => r.Kind == kind.Kind && r.IsLive && r.HasSideRow && r.State == DataSyncEntitySyncState.Synced);
            var feedKind = new DataSyncFeedKind(kind.Kind, kind.Codec.Descriptor.SchemaVersion, MaxSeq(kind.Kind),
                Db.Local.TombstoneFloors.GetValueOrDefault(kind.Kind), live,
                Rows.Count(r => r.Kind == kind.Kind && r.Deleted && r.Served), written.ContentHash, since,
                written.Records.Count, superseded);
            var staged = assembler.Complete(feedKind, since == 0);
            if (assembler.Problem is { } problem) throw new InvalidOperationException($"{Name}'s feed: {problem}");
            kinds.Add(staged);
            manifestKinds.Add(feedKind);
        }

        var manifest = new DataSyncFeedManifest("snap-" + NodeId, 120_000, NodeId, Epoch, Db.Local.ActorId,
            DataSyncContract.Version, DataSyncContract.MinimumPeerVersion, "1.0.0", manifestKinds, null, Attention);
        return new DataSyncStagedPull(NodeId, Name, manifest, kinds, Now);
    }

    /// <summary>The highest Seq of a kind, served or not (<c>MaxSeq</c>).</summary>
    public long MaxSeq(string kind) => Rows.Where(r => r.Kind == kind).Select(r => r.Seq).DefaultIfEmpty(0).Max();

    /// <summary>A cursor below the kind's tombstone floor or above <c>LastSeq</c> is served from 0 (§7.5.5).</summary>
    private bool Superseded(string kind, long since) =>
        (since > 0 && since < Db.Local.TombstoneFloors.GetValueOrDefault(kind)) || since > LastSeq;

    /// <summary>
    /// §7.5.1 step 1: a reader whose cursor is above <c>LastSeq</c> has seen sequence numbers this database never
    /// issued. Reported once (before any Refresh can issue a counter); the reader stays recorded until it has read
    /// with every cursor at or below <c>LastSeq</c>. True while it is a recorded reader-ahead.
    /// </summary>
    private bool ReaderAhead(SimNode reader, SimLink readerLink)
    {
        var ahead = readerLink.Cursors.Values.Any(c => c > LastSeq);
        var recorded = Db.Local.ReadersAhead.TryGetValue(reader.NodeId, out var settled) && !settled;
        if (!ahead)
        {
            if (recorded) Db.Local.ReadersAhead[reader.NodeId] = true;
            return false;
        }

        if (!recorded)
        {
            Db.Local.ReadersAhead[reader.NodeId] = false;
            _world.Log($"{Name}: reader {reader.Name} is ahead (cursor above LastSeq {LastSeq})");
            ReportEvidence(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Reader, reader.NodeId), null, null);
        }

        return true;
    }

    /// <summary>§7.5.1 step 4: the highest counter of the reader's actor in any vector this source stores.</summary>
    private long? SeenCounter(string readerActorId)
    {
        long? highest = null;
        void See(Bakabase.Modules.DataSync.Identity.DataSyncVersionVector? vv)
        {
            if (vv is not null && vv.Counters.TryGetValue(readerActorId, out var c) && (highest is null || c > highest))
                highest = c;
        }

        foreach (var row in Rows) See(row.Vv);
        foreach (var b in Links.Values.SelectMany(l => l.Bases.Values))
        {
            See(b.Vv);
            See(b.Pending?.Record.Vv);
        }

        return highest;
    }
}
