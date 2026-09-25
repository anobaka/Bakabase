using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Echo prevention and normalization drift (spec §6.4) at persistence: an apply's hashes and its revision come from
/// the entity as re-read after the write, never from the payload, so the next Refresh finds nothing to do; a
/// normalization the codec does not model adds this device's counter once and then settles. (The fold cases of the
/// custom property codec, and the full pull, are the merger's and the codec's parts of this class.)
/// </summary>
[TestClass]
public class EchoAndConvergenceTests
{
    private const string PeerActor = "9999999999999999";

    [TestMethod]
    public async Task After_an_apply_recorded_from_the_re_read_Refresh_makes_no_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();

        var (record, reread, vv) = await FastForwardAsync(f, "Genres", ("a", "Action"), ("b", "Drama"));

        Assert.IsTrue(DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec,
            DataSyncEntityForms.Evaluate(f.Kind.Codec, reread, DataSyncOverlay.None, false, null, null), record));
        Assert.AreEqual(record.Vv, vv, "FastForward with resultEqualsRemote takes the remote vector as it is (§2.8)");
        var recorded = await f.RowAsync("1");

        Assert.AreEqual(0, (await f.RefreshAsync()).Changed);
        Assert.AreEqual((recorded.Seq, recorded.VvJson), ((await f.RowAsync("1")).Seq, (await f.RowAsync("1")).VvJson));
    }

    [TestMethod]
    public async Task An_unmodelled_normalization_adds_one_counter_and_then_settles()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        // A service rule the codec does not mirror: labels are trimmed when written.
        f.Kind.NormalizeOnWrite = content =>
        {
            foreach (var child in content["children"]!.AsArray())
                child!["label"] = child["label"]!.GetValue<string>().Trim();
            return content;
        };

        var (record, reread, vv) = await FastForwardAsync(f, "Genre", ("a", "Action"), ("b", "Drama "));

        var self = new DataSyncActorId((await f.StateAsync()).ActorId);
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(record.Vv),
            "the re-read differs from the remote: this device adds a counter of its own (§6.4)");
        Assert.IsTrue(vv[self] > 0);
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "and the next Refresh still finds nothing");

        // The peer fast-forwards to the normalized content; writing it here again changes nothing: one hop.
        var normalized = new DataSyncWireRecord(record.Keys, record.Origin, record.Seq + 1, vv, null, false, 1, null,
            reread.Content, ContentHash.Of(reread.Content), null, 0);
        var again = (await f.Kind.ReadAsync(["1"], default)).Single();
        Assert.IsTrue(DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec,
            DataSyncEntityForms.Evaluate(f.Kind.Codec, again, DataSyncOverlay.None, false, null, null), normalized));
    }

    /// <summary>
    /// A peer record that dominates the local entity is written through the adapter and recorded the way the apply
    /// runner records it: hashes from the re-read entity, the FastForward revision with <c>resultEqualsRemote</c>
    /// computed from the re-read comparison form.
    /// </summary>
    private static async Task<(DataSyncWireRecord Record, LocalEntity Reread, DataSyncVersionVector Vv)> FastForwardAsync(
        DataSyncRefreshFixture f, string name, params (string Id, string Label)[] children)
    {
        var row = await f.Db.DataSyncEntities.SingleAsync(e => e.LocalKey == "1");
        var local = DataSyncVersionVector.ParseStored(row.VvJson);
        var remoteContent = new MemoryDefinition(name, children.Select(c => new MemoryChild(c.Id, c.Label)).ToList()).ToContent();
        var record = new DataSyncWireRecord([row.SyncKey], "peer-node", 5, local.With(new DataSyncActorId(PeerActor), 1),
            new DataSyncEditorRef("peer-node", "PC-1", PeerActor), false, 1, null, remoteContent,
            ContentHash.Of(remoteContent), null, 0);

        await f.Kind.ApplyAsync(new ApplyBatch(f.KindId,
        [
            new UpdateEntityOperation("i1", "1", row.LocalHash, remoteContent, EntityKeys.None, ["b"], []),
        ]), default);

        var reread = (await f.Kind.ReadAsync(["1"], default)).Single();
        var form = DataSyncEntityForms.Evaluate(f.Kind.Codec, reread, DataSyncOverlay.None, false, null, null);
        var state = (await f.Store.GetLocalStateAsync(default))!;
        var vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.FastForward, local, record.Vv,
            DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec, form, record), false,
            new DataSyncActorId(state.ActorId), () => ++state.ActorCounter);
        row.LocalHash = form.LocalHash;
        row.RawHash = (await f.Kind.ReadRawHashesAsync(default))["1"];
        row.SharedHash = form.SharedHash!;
        row.VvJson = vv.ToCanonicalString();
        row.Seq = await f.Store.NextSeqAsync(default);
        await f.Db.SaveChangesAsync();
        return (record, reread, vv);
    }
}
