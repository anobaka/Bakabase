using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncFeedFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The TestKit's in-process peer client (spec §13.7): one provider reads another provider's real feed source, the
/// head and manifest as federation JSON and the pages as their real bytes, and meets the source's refusals as the
/// federation peer client maps them (§7.6).
/// </summary>
[TestClass]
public class InProcessPeerClientTests
{
    private const string Kind = DataSyncKindIds.CustomProperty;

    private static async Task<(DataSyncFeedFixture A, DataSyncFeedFixture B)> TwoHostsAsync()
    {
        var a = await CreateAsync(configure: s => s.AddSingleton<IDataSyncPeerClient>(new InProcessPeerClient()));
        var b = await CreateAsync(configure: s => s.AddSingleton<IDataSyncPeerClient>(new InProcessPeerClient()));
        await InProcessPeerClient.WireAsync(a.Services, b.Services);
        return (a, b);
    }

    [TestMethod]
    public async Task A_device_reads_another_providers_feed_through_its_real_page_bytes()
    {
        var (a, b) = await TwoHostsAsync();
        b.Kind.Add("1", "Genre", ("x", "Rock"), ("y", "Jazz"));
        var client = InProcessPeerClient.ClientOf(a.Services);
        var peer = b.R.Identity.Device;
        var self = a.R.Identity.Device;

        var probe = await client.ProbeAsync(peer.NodeId, default);
        Assert.AreEqual((peer.NodeId, peer.Name, true, (int?) 1, (bool?) true, "online"), (probe.NodeId, probe.Name,
            probe.HasAccess, probe.ContractVersion, probe.SharesDefinitions, probe.ConnectionState));

        var head = await client.GetHeadAsync(peer.NodeId, Query((Kind, 0)), default);
        Assert.AreEqual(peer.NodeId, head.NodeId);
        Assert.AreEqual(head.Seq, head.Kinds.Single().MaxSeq);

        var manifest = await client.GetManifestAsync(peer.NodeId, Query((Kind, 0)), default);
        var kind = manifest.Kinds.Single();
        var page = await client.GetPageAsync(peer.NodeId, manifest.SnapshotId, Kind, kind.SinceSeq, null, default);
        var record = Reassemble([page.ToArray()]).Single();
        Assert.IsTrue(JsonNode.DeepEquals(b.Published("1"), record["content"]));
        CollectionAssert.AreEqual(b.Snapshots.Find("inproc-" + self.NodeId)!.Kinds[Kind].Pages[0], page.ToArray(),
            "the source's own bytes, copied");

        var reader = (await b.Store.GetReadersAsync(default)).Single();
        Assert.AreEqual((self.NodeId, self.Name), (reader.NodeId, reader.Name), "the source sees this device as its reader");
        CollectionAssert.AreEqual(new[] {"probe", "head", "manifest", "page"}, client.Calls.Select(c => c.Operation).ToArray());
        Assert.AreEqual((manifest.SnapshotId, Kind, (long?) kind.SinceSeq, (string?) null),
            (client.Calls[3].SnapshotId, client.Calls[3].Kind, client.Calls[3].SinceSeq, client.Calls[3].Cursor));

        // Both ways: the other provider reads this one.
        a.Kind.Add("9", "Mood");
        var back = await InProcessPeerClient.ClientOf(b.Services).GetHeadAsync(self.NodeId, Query((Kind, 0)), default);
        Assert.AreEqual(self.NodeId, back.NodeId);
        Assert.IsTrue(back.Seq > 0);
    }

    [TestMethod]
    public async Task The_sources_refusals_arrive_as_the_federation_peer_client_maps_them()
    {
        var (a, b) = await TwoHostsAsync();
        b.Kind.Add("1", "Genre");
        var client = InProcessPeerClient.ClientOf(a.Services);
        var peer = b.R.Identity.Device.NodeId;

        await client.GetManifestAsync(peer, Query((Kind, 0)), default);
        var busy = await RefusedAsync(() => client.GetManifestAsync(peer, Query((Kind, 0)), default));
        Assert.AreEqual((DataSyncPeerErrorCode.Busy, (int?) 10), (busy.Code, busy.RetryAfterSeconds), "429 is Busy");

        var expired = await RefusedAsync(() => client.GetPageAsync(peer, "0123456789abcdef", Kind, 0, null, default));
        Assert.AreEqual(DataSyncPeerErrorCode.SnapshotExpired, expired.Code);

        b.Clock.Advance(DataSyncFeedSnapshots.ManifestInterval);
        var state = await b.StateAsync();
        var restoring = await RefusedAsync(() =>
            client.GetManifestAsync(peer, Query((Kind, state.LastSeq + 1)), default));
        Assert.AreEqual((DataSyncPeerErrorCode.PeerRestorePending, (int?) 3600),
            (restoring.Code, restoring.RetryAfterSeconds));

        foreach (var (feed, code) in new (DataSyncFeedException, DataSyncPeerErrorCode)[]
                 {
                     (DataSyncFeedErrors.Busy(30), DataSyncPeerErrorCode.Busy),
                     (DataSyncFeedErrors.TooManySnapshots(4), DataSyncPeerErrorCode.Busy),
                     (DataSyncFeedErrors.SnapshotTooLarge(), DataSyncPeerErrorCode.TooLarge),
                     (DataSyncFeedErrors.SnapshotExpired(), DataSyncPeerErrorCode.SnapshotExpired),
                     (DataSyncFeedErrors.SnapshotMismatch(), DataSyncPeerErrorCode.SnapshotExpired),
                     (DataSyncFeedErrors.SourceRestorePending(), DataSyncPeerErrorCode.PeerRestorePending),
                     (DataSyncFeedErrors.UnknownKind(), DataSyncPeerErrorCode.InvalidResponse),
                     (new DataSyncFeedException("CursorSuperseded", 409), DataSyncPeerErrorCode.CursorSuperseded),
                     (new DataSyncFeedException("DataSyncSharingDisabled", 403), DataSyncPeerErrorCode.PeerSharingOff),
                     (new DataSyncFeedException("RemoteAccessDisabled", 403), DataSyncPeerErrorCode.PeerRemoteAccessOff),
                     (new DataSyncFeedException("GrantRevoked", 401), DataSyncPeerErrorCode.AccessRevoked),
                     (new DataSyncFeedException("NodeNotAuthorized", 403), DataSyncPeerErrorCode.AccessMissing),
                 })
        {
            var mapped = InProcessPeerClient.Map(feed);
            Assert.AreEqual((code, feed.RetryAfterSeconds), (mapped.Code, mapped.RetryAfterSeconds), feed.Code);
        }
    }

    [TestMethod]
    public async Task A_test_can_take_a_peer_offline_revoke_access_or_inject_any_refusal()
    {
        var (a, b) = await TwoHostsAsync();
        var client = InProcessPeerClient.ClientOf(a.Services);
        var peer = b.R.Identity.Device.NodeId;

        client.SetReachable(peer, false);
        Assert.AreEqual(DataSyncPeerErrorCode.Unreachable,
            (await RefusedAsync(() => client.GetHeadAsync(peer, Query((Kind, 0)), default))).Code);
        Assert.AreEqual("offline", (await client.ProbeAsync(peer, default)).ConnectionState);
        client.SetReachable(peer, true);

        client.Fault = (operation, _) => operation == "head"
            ? new DataSyncPeerException(DataSyncPeerErrorCode.PeerSharingOff)
            : null;
        Assert.AreEqual(DataSyncPeerErrorCode.PeerSharingOff,
            (await RefusedAsync(() => client.GetHeadAsync(peer, Query((Kind, 0)), default))).Code);
        client.Fault = null;
        await client.GetHeadAsync(peer, Query((Kind, 0)), default);

        client.Revoke(peer);
        Assert.AreEqual(DataSyncPeerErrorCode.AccessRevoked,
            (await RefusedAsync(() => client.GetManifestAsync(peer, Query((Kind, 0)), default))).Code);
        Assert.IsFalse((await client.ProbeAsync(peer, default)).HasAccess);

        client.Disconnect(peer);
        Assert.AreEqual(DataSyncPeerErrorCode.AccessMissing,
            (await RefusedAsync(() => client.GetHeadAsync(peer, Query((Kind, 0)), default))).Code);
        Assert.IsFalse((await client.ProbeAsync(peer, default)).HasAccess);
    }

    private static async Task<DataSyncPeerException> RefusedAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (DataSyncPeerException e)
        {
            return e;
        }

        Assert.Fail("The peer answered instead of refusing.");
        return null!;
    }
}
