using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// <c>RecordApply</c> (§8.10.2, §6.4) for one entity: after the adapter wrote it, the entity is re-read and its side
/// row takes its hashes from the re-read content, its vector from <see cref="DataSyncRevisionRules"/> (through
/// <see cref="DataSyncRecordApply.Revise"/>), its Seq from <c>NextSeq</c> and its last editor (the peer's for an exact
/// fast-forward or create, else this device). Keys go through the identity store only. Each write is recorded for the
/// history entry: its change list, its pre-image, the entity as touched.
/// </summary>
internal sealed class DataSyncEntityWrites(DataSyncApplySession s, DataSyncApplyRecorder recorder)
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _rawHashes = new(StringComparer.Ordinal);

    public DataSyncApplyRecorder Recorder => recorder;

    /// <summary>
    /// The adapter wrote <paramref name="kind"/>: its raw hashes are read again when next needed. The kind was marked
    /// touched before the write (<see cref="DataSyncApplySession.Writer"/>).
    /// </summary>
    public void Written(string kind) => _rawHashes.Remove(kind);

    public async Task<LocalEntity> ReReadAsync(string kind, string localKey, CancellationToken ct) =>
        (await s.Adapter(kind).ReadAsync([localKey], ct)).SingleOrDefault() ??
        throw new InvalidOperationException($"{kind}/{localKey} is gone right after it was written.");

    private async Task<string?> RawHashAsync(string kind, string localKey, CancellationToken ct)
    {
        if (!_rawHashes.TryGetValue(kind, out var hashes))
            _rawHashes[kind] = hashes = await s.Adapter(kind).ReadRawHashesAsync(ct);
        return hashes.GetValueOrDefault(localKey);
    }

    /// <summary>The peer's comparison-form hash of <paramref name="remote"/> with this build's codec (§3.4).</summary>
    public string? RemoteSharedHash(string kind, DataSyncWireRecord? remote) =>
        remote is null || remote.Deleted
            ? null
            : DataSyncPublication.SharedHashOfRecord(s.Adapter(kind).Codec, remote, s.Limits);

    /// <summary>
    /// A create (row N3, a review's Create, KeepBoth, RestoreHere, a revive): the side row takes the record's keys
    /// (§5.1) — or revives the tombstone that owns the first of them — and the revision's vector.
    /// </summary>
    /// <param name="localVv">
    /// The vector the revision starts from: empty for a create; the tombstone's for undo's re-create, whose
    /// <c>Undo</c> revision must include it (§8.11).
    /// </param>
    public async Task<DataSyncEntityDbModel> RecordCreateAsync(string kind, string newLocalKey, EntityKeys keys,
        string originNodeId, DataSyncRevisionDecision decision, DataSyncWireRecord? remote, int? linkId,
        bool createdBySync, CancellationToken ct, DataSyncVersionVector? localVv = null)
    {
        var codec = s.Adapter(kind).Codec;
        var reRead = await ReReadAsync(kind, newLocalKey, ct);
        var typed = codec.ReadLocal(reRead.Content);
        var childrenLocal = decision.ChildrenLocal ?? false;
        var applied = DataSyncRecordApply.Revise(codec, decision, localVv ?? DataSyncVersionVector.Empty, null, typed,
            DataSyncOverlay.None, RemoteSharedHash(kind, remote), remote?.EditedBy, s.Self, s.SelfActor, s.NextCounter);
        var form = DataSyncEntityForms.Evaluate(codec, reRead, DataSyncOverlay.None, childrenLocal, decision.OrderKey,
            decision.Unknown);
        var row = new DataSyncEntityDbModel
        {
            Kind = kind,
            LocalKey = newLocalKey,
            OriginNodeId = originNodeId,
            Fingerprint = reRead.Fingerprint,
            LocalHash = form.LocalHash,
            RawHash = await RawHashAsync(kind, newLocalKey, ct),
            SharedHash = form.SharedHash ?? DataSyncEntityForms.NoSharedHash,
            VvJson = applied.Vv.ToCanonicalString(),
            OrderKey = codec.Descriptor.HasOrder ? decision.OrderKey : null,
            State = DataSyncEntitySyncState.Synced,
            UnknownJson = WriteUnknown(decision.Unknown),
            ChildrenLocal = childrenLocal,
            CreatedBySync = createdBySync,
            Unreadable = reRead.Unreadable,
        };
        SetEditor(row, applied.LastEditor);
        row = await s.Identity.CreateAsync(row, keys, recorder.Identity, ct);
        // A revive keeps the tombstone's row: its content columns were taken from the template above.
        row.Unreadable = reRead.Unreadable;

        var name = codec.NameOf(typed);
        recorder.PreImages.Add(new DataSyncEntityPreImage(kind, row.Id, row.LocalKey, name, DataSyncPreImageActions.Created,
            linkId, (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList(), row.LocalHash));
        recorder.Changes(new DataSyncEntityChanges(kind, row.LocalKey, [], []));
        Touch(row);
        return row;
    }

    /// <summary>
    /// A revision of a live entity (an update, a bind with a merge revision, a revision without a content change such
    /// as a new order key or a hold): aliases first (a retirement joins the tombstone's history, §5.3), then the vector
    /// from the post-retirement row, hashes and flags from the re-read content.
    /// </summary>
    /// <param name="before">The entity's <c>ReadLocal</c> content before the apply, for its change list.</param>
    /// <param name="decision">Null: no revision (keys or a hold only).</param>
    public async Task<DataSyncEntityDbModel> RecordLiveAsync(string kind, string localKey, object before,
        DataSyncRevisionDecision? decision, DataSyncWireRecord? remote, DataSyncOverlayChange? overlayChange,
        EntityKeys aliases, int? linkId, CancellationToken ct)
    {
        var codec = s.Adapter(kind).Codec;
        var row = await s.LiveRowAsync(kind, localKey, ct);
        var sharedBefore = row.SharedHash;
        var childrenLocalBefore = row.ChildrenLocal;
        if (aliases.All.Count > 0) await s.Identity.AddAliasesAsync(row, aliases.All, recorder.Identity, ct);

        var reRead = await ReReadAsync(kind, localKey, ct);
        var typed = codec.ReadLocal(reRead.Content);
        var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
        if (overlayChange is not null)
        {
            overlay = overlay with
            {
                HeldChildren = overlay.HeldChildren.Where(h => !overlayChange.Release.Contains(h))
                    .Concat(overlayChange.Hold.Where(h => !overlay.HeldChildren.Contains(h))).ToList(),
            };
            row.OverlayJson = DataSyncStoredJson.WriteOverlay(overlay);
        }

        var childrenLocal = decision?.ChildrenLocal ?? row.ChildrenLocal;
        var orderKey = decision is not null && codec.Descriptor.HasOrder ? decision.OrderKey : row.OrderKey;
        var unknown = decision is not null ? decision.Unknown : DataSyncEntityForms.ReadUnknown(row.UnknownJson);
        if (decision is not null)
        {
            var applied = DataSyncRecordApply.Revise(codec, decision, DataSyncVersionVector.ParseStored(row.VvJson),
                sharedBefore, typed, overlay, RemoteSharedHash(kind, remote), remote?.EditedBy, s.Self, s.SelfActor,
                s.NextCounter);
            row.VvJson = applied.Vv.ToCanonicalString();
            SetEditor(row, applied.LastEditor);
            recorder.Touched.Add((kind, new SyncKey(row.SyncKey)));
        }

        var form = DataSyncEntityForms.Evaluate(codec, reRead, overlay, childrenLocal, orderKey, unknown);
        row.LocalHash = form.LocalHash;
        row.RawHash = await RawHashAsync(kind, localKey, ct);
        row.SharedHash = form.SharedHash ?? DataSyncEntityForms.NoSharedHash;
        row.OrderKey = orderKey;
        row.UnknownJson = WriteUnknown(unknown);
        row.ChildrenLocal = childrenLocal;
        row.Unreadable = reRead.Unreadable;
        // A bind alone got its Seq from the identity store; a revision or a hold changes what readers receive.
        if (decision is not null || overlayChange is not null) row.Seq = await s.Store.NextSeqAsync(ct);
        row.UpdatedAtUtc = s.Now;

        var changes = DataSyncChangeLists.Between(codec, localKey, before, childrenLocalBefore, typed, childrenLocal);
        recorder.Changes(changes);
        if (!changes.IsEmpty)
        {
            recorder.PreImages.Add(new DataSyncEntityPreImage(kind, row.Id, localKey, codec.NameOf(typed),
                DataSyncPreImageActions.Updated, linkId, (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList(),
                row.LocalHash, changes));
            recorder.ChangedDefinitions.Add((kind, localKey));
        }

        return row;
    }

    /// <summary>
    /// A deletion the merge or a decision applied (row K1, DeleteHere): the side row becomes a tombstone with the
    /// revision's vector (§6.3), served when the entity was Synced. The whole content and raw row are kept for undo.
    /// </summary>
    public async Task<DataSyncEntityDbModel> RecordDeleteAsync(string kind, string localKey, object before,
        JsonObject? raw, DataSyncRevisionDecision decision, DataSyncWireRecord? remote, int? linkId, CancellationToken ct)
    {
        var codec = s.Adapter(kind).Codec;
        var row = await s.LiveRowAsync(kind, localKey, ct);
        var keys = (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList();
        var applied = DataSyncRecordApply.Revise(codec, decision, DataSyncVersionVector.ParseStored(row.VvJson),
            row.SharedHash, null, DataSyncStoredJson.ReadOverlay(row.OverlayJson), null, remote?.EditedBy, s.Self,
            s.SelfActor, s.NextCounter);
        await s.Identity.TombstoneAsync(row, new DataSyncTombstoneWrite(applied.Vv, DataSyncTombstoneKind.Deleted,
            row.State == DataSyncEntitySyncState.Synced, applied.LastEditor), ct);
        recorder.PreImages.Add(new DataSyncEntityPreImage(kind, row.Id, localKey, codec.NameOf(before),
            DataSyncPreImageActions.Deleted, linkId, keys, null, Content: codec.Write(before), Row: raw));
        recorder.Changes(new DataSyncEntityChanges(kind, localKey, [], []));
        recorder.ChangedDefinitions.Add((kind, localKey));
        Touch(row);
        return row;
    }

    /// <summary>A tombstone takes a revision (row T1 absorbing a peer's deletion, KeepDeleted, RestoreWins).</summary>
    public async Task<DataSyncEntityDbModel?> RecordTombstoneRevisionAsync(string kind, SyncKey key,
        DataSyncRevisionKind revision, DataSyncVersionVector? remoteVv, DataSyncEditorRef? remoteEditor,
        CancellationToken ct)
    {
        var row = await s.OwnerAsync(kind, key.Value, ct);
        if (row is not { DeletedAtUtc: not null }) return null;
        var local = DataSyncVersionVector.ParseStored(row.VvJson);
        var vv = DataSyncRevisionRules.Next(revision, local, remoteVv, false, false, s.SelfActor, s.NextCounter);
        if (vv == local) return row;
        row.VvJson = vv.ToCanonicalString();
        SetEditor(row, remoteEditor is not null && remoteVv is { } r && vv == r ? remoteEditor : s.Self);
        row.Seq = await s.Store.NextSeqAsync(ct);
        row.UpdatedAtUtc = s.Now;
        Touch(row);
        return row;
    }

    public void Touch(DataSyncEntityDbModel row) => recorder.Touched.Add((row.Kind, new SyncKey(row.SyncKey)));

    public static void SetEditor(DataSyncEntityDbModel row, DataSyncEditorRef editor)
    {
        row.LastActorId = editor.ActorId;
        row.LastEditorNodeId = editor.NodeId;
        row.LastEditorName = editor.Name;
    }

    public static string? WriteUnknown(JsonObject? unknown) =>
        unknown is { Count: > 0 } ? unknown.ToJsonString() : null;
}
