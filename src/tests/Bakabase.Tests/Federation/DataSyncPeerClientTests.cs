using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Bakabase.Tests.RemoteAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// The receiver side of the feed (§7.6) between real nodes over HTTP, through the Service's gates and the real feed
/// controller, with a scripted feed source at the source: what a reader declares arrives as it was declared, pages
/// arrive as raw bytes, every refusal reads as the data sync error of §7.6, one fetch per peer at a time, and a
/// device pulls only from a node it holds a direct grant for (§7.7, D03).
/// </summary>
[TestClass]
public sealed class DataSyncPeerClientTests
{
    private const string Actor = "0123456789abcdef";

    [TestMethod]
    public async Task WhatTheReaderDeclaresArrivesAndTheSourcesAnswersComeBackAsTheySaidThem()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);
        var query = new DataSyncFeedQuery("twoWay",
            new Dictionary<string, long> { ["extensionGroup"] = 0, ["customProperty"] = 131 }, Actor, "needsYou:3");

        var head = await desk.PeerClient.GetHeadAsync("node-nas", query, default);
        Assert.AreEqual(("node-nas", 131L, "customProperty", 131L), (head.NodeId, head.Seq, head.Kinds[0].Kind, head.Kinds[0].MaxSeq));
        Assert.AreEqual((2, true, "twoWay"), (head.Attention.OpenDecisions, head.Counterpart!.FirstContactCompleted, head.Counterpart.Mode));
        var manifest = await desk.PeerClient.GetManifestAsync("node-nas", query, default);
        Assert.AreEqual(("snapshot-1", ScriptedFeed.EmptyHash), (manifest.SnapshotId, manifest.Kinds.Single().ContentHash));
        var first = await desk.PeerClient.GetPageAsync("node-nas", "snapshot-1", "customProperty", 131, null, default);
        var second = await desk.PeerClient.GetPageAsync("node-nas", "snapshot-1", "customProperty", 131, "p2", default);
        CollectionAssert.AreEqual(feed.PageBytes("customProperty", null), first.ToArray());
        CollectionAssert.AreEqual(feed.PageBytes("customProperty", "p2"), second.ToArray());

        // The source read what was declared, from this device, under the grant it holds.
        var grant = (await nas.Grants.GetGrantsAsync(default)).Single();
        Assert.AreEqual("node-desk", grant.NodeId);
        var calls = feed.Calls.ToArray();
        CollectionAssert.AreEqual(new[] { "head", "manifest", "changes", "changes" }, calls.Select(c => c.Call).ToArray());
        Assert.IsTrue(calls.All(c => c.Reader is { NodeId: "node-desk", Name: "Desk" }));
        Assert.AreEqual(1, calls.Select(c => c.Reader.GrantId).Distinct().Count());
        foreach (var declared in calls.Take(2).Select(c => c.Query!))
        {
            Assert.AreEqual(("twoWay", Actor, "needsYou:3"), (declared.Mode, declared.ReaderActorId, declared.ReaderState));
            Assert.AreEqual((131L, 0L), (declared.Since["customProperty"], declared.Since["extensionGroup"]));
        }
        Assert.AreEqual(("snapshot-1", "customProperty", 131L, "p2"),
            (calls[3].SnapshotId, calls[3].Kind, calls[3].SinceSeq, calls[3].Cursor));

        // A reader that declares nothing sends no values at all.
        await desk.PeerClient.GetHeadAsync("node-nas", new DataSyncFeedQuery(null, new Dictionary<string, long>(), null, null), default);
        var bare = feed.Calls.Last().Query!;
        Assert.AreEqual(((string?)null, 0, (string?)null, (string?)null), (bare.Mode, bare.Since.Count, bare.ReaderActorId, bare.ReaderState));
    }

    /// <summary>F61: a 16-level multilevel property nests deeper than FederationJson reads; its page arrives as bytes.</summary>
    [TestMethod]
    public async Task ADeepMultilevelPageArrivesAsTheBytesTheSourceSent()
    {
        var feed = new ScriptedFeed("node-nas");
        var page = DeepPage(16);
        feed.Pages[("customProperty", null)] = page;
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);

        var read = await desk.PeerClient.GetPageAsync("node-nas", "snapshot-1", "customProperty", 0, null, default);

        CollectionAssert.AreEqual(page, read.ToArray());
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<JsonElement>(read.Span, FederationJson.Options),
            "FederationJson stops at depth 32.");
        using var parsed = JsonDocument.Parse(read, new JsonDocumentOptions { MaxDepth = DataSyncLimits.Default.MaxJsonDepth });
        var node = parsed.RootElement.GetProperty("records")[0].GetProperty("content").GetProperty("nodes")[0];
        var levels = 1;
        while (node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0)
        {
            node = children[0];
            levels++;
        }
        Assert.AreEqual(16, levels);
    }

    /// <summary>What the source's feed refuses, with its own retry advice, through the real controller (§7.5, §7.6).</summary>
    [TestMethod]
    public async Task TheSourcesRefusalsReadAsTheirDataSyncErrorsWithTheirRetryAdvice()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);
        var client = desk.PeerClient;
        var query = Query();

        (string Code, int Status, bool Retryable, int? RetryAfter, bool Page, DataSyncPeerErrorCode Expected)[] rows =
        [
            ("Busy", 503, true, 30, false, DataSyncPeerErrorCode.Busy),
            ("TooManySnapshots", 429, true, 7, false, DataSyncPeerErrorCode.Busy),
            ("SnapshotTooLarge", 413, false, null, false, DataSyncPeerErrorCode.TooLarge),
            ("SourceRestorePending", 409, true, 3600, false, DataSyncPeerErrorCode.PeerRestorePending),
            ("SnapshotExpired", 410, false, null, true, DataSyncPeerErrorCode.SnapshotExpired),
            ("SnapshotMismatch", 409, false, null, true, DataSyncPeerErrorCode.SnapshotExpired),
            ("CursorSuperseded", 409, false, null, true, DataSyncPeerErrorCode.CursorSuperseded),
            ("UnknownKind", 404, false, null, true, DataSyncPeerErrorCode.InvalidResponse)
        ];
        foreach (var row in rows)
        {
            feed.Errors.Enqueue(new DataSyncFeedException(row.Code, row.Status, row.Retryable, row.RetryAfter));
            var error = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() => row.Page
                ? client.GetPageAsync("node-nas", "snapshot-1", "customProperty", 0, "p2", default)
                : client.GetManifestAsync("node-nas", query, default));
            Assert.AreEqual((row.Expected, row.RetryAfter, row.Code), (error.Code, error.RetryAfterSeconds, error.Message),
                row.Code);
        }

        // Each refusal answered one call; the reader is not stuck behind any of them.
        Assert.AreEqual("snapshot-1", (await client.GetManifestAsync("node-nas", query, default)).SnapshotId);

        // A refusal on the way to a session comes from info or the handshake, as whoever holds the address wrote it:
        // only a code this build could have sent is passed on, never text of the answerer's choosing.
        foreach (var (code, detail) in new[] { ("RemoteAccessDisabled", "RemoteAccessDisabled"),
                     ("Evil\nInjected " + new string('x', 5000), "http403"), (new string('A', 65), "http403") })
        {
            nas.Answer = (context, _) =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new { code });
            };
            var refused = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
                desk.NewPeerClient().GetHeadAsync("node-nas", query, default));
            Assert.AreEqual(detail, refused.Message);
            Assert.AreEqual(detail == "http403"
                ? DataSyncPeerErrorCode.AccessRevoked
                : DataSyncPeerErrorCode.PeerRemoteAccessOff, refused.Code);
        }
        nas.Answer = null;
    }

    /// <summary>What the source's gate says before its feed is reached, and a source whose build has no feed yet.</summary>
    [TestMethod]
    public async Task TheSourcesGateAndBuildReadAsTheirDataSyncErrors()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await using var stub = await DataSyncNodeHost.StartAsync("node-stub", "Stub");
        await PairAsync(desk, nas);
        await PairAsync(desk, stub);
        var query = Query();
        // What data sync reads, and the federation code it read it from.
        async Task<(DataSyncPeerErrorCode, string)> Head(FederationDataSyncPeerClient client, string node = "node-nas")
        {
            var error = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() => client.GetHeadAsync(node, query, default));
            return (error.Code, error.Message);
        }

        Assert.AreEqual((DataSyncPeerErrorCode.PeerTooOld, "NotImplemented"), await Head(desk.PeerClient, "node-stub"),
            "A build whose feed is not there yet.");
        Assert.AreEqual("node-nas", (await desk.PeerClient.GetHeadAsync("node-nas", query, default)).NodeId);

        // Definitions sharing off: at the feed route with a verified session, and at info or the handshake without one.
        await nas.Grants.SetSharingEnabledAsync(false, false, default);
        Assert.AreEqual((DataSyncPeerErrorCode.PeerSharingOff, "DataSyncSharingDisabled"), await Head(desk.PeerClient));
        Assert.AreEqual((DataSyncPeerErrorCode.PeerSharingOff, "SharingDisabled"), await Head(desk.NewPeerClient()),
            "Nothing shared: info is refused.");
        await nas.Peers.SetSharingAsync(true);
        Assert.AreEqual((DataSyncPeerErrorCode.PeerSharingOff, "DataSyncSharingDisabled"), await Head(desk.NewPeerClient()),
            "Only the library shared: the handshake is refused.");
        await nas.Peers.SetSharingAsync(false);
        await nas.Grants.SetSharingEnabledAsync(true, false, default);

        nas.Remote.Mode = RemoteAccessMode.Disabled;
        Assert.AreEqual((DataSyncPeerErrorCode.PeerRemoteAccessOff, "RemoteAccessDisabled"), await Head(desk.PeerClient));
        nas.Remote.Mode = RemoteAccessMode.Enabled;
        Assert.AreEqual("node-nas", (await desk.PeerClient.GetHeadAsync("node-nas", query, default)).NodeId);

        // The source stops this device from reading it: revoked there, whether the session was verified or not.
        await nas.Grants.RevokeAsync("node-desk", default);
        Assert.AreEqual((DataSyncPeerErrorCode.AccessRevoked, "GrantRevoked"), await Head(desk.PeerClient));
        Assert.AreEqual((DataSyncPeerErrorCode.AccessRevoked, "GrantRevoked"), await Head(desk.NewPeerClient()));
        // This device drops its own credentials: nothing leaves it.
        var calls = feed.Calls.Count;
        await desk.Grants.ForgetOutboundAsync("node-nas", default);
        Assert.AreEqual((DataSyncPeerErrorCode.AccessMissing, "NodeNotAuthorized"), await Head(desk.PeerClient));
        Assert.AreEqual((DataSyncPeerErrorCode.AccessMissing, "NodeNotAuthorized"), await Head(desk.PeerClient, "node-unknown"));
        Assert.AreEqual(calls, feed.Calls.Count);
    }

    [TestMethod]
    public async Task AReplacedLibraryReadsAsAResetAndAVanishedSourceAsUnreachable()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var pc = await DataSyncNodeHost.StartAsync("node-pc", "PC", feed: new ScriptedFeed("node-pc"));
        var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: new ScriptedFeed("node-nas"));
        var nasStopped = false;
        try
        {
            await PairAsync(desk, nas);
            await PairAsync(desk, pc);

            // G22b: the rotation turns definitions sharing off and revokes the grant; turned back on, info shows the
            // new epoch and the reader stops there, before it signs anything.
            await pc.Peers.RotateLibraryEpochAsync();
            await pc.Grants.SetSharingEnabledAsync(true, false, default);
            var reset = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
                desk.NewPeerClient().GetHeadAsync("node-pc", Query(), default));
            Assert.AreEqual((DataSyncPeerErrorCode.PeerReset, "LibraryEpochChanged"), (reset.Code, reset.Message));

            await desk.PeerClient.GetHeadAsync("node-nas", Query(), default);
            nasStopped = true;
            await nas.DisposeAsync();
            var gone = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
                desk.PeerClient.GetHeadAsync("node-nas", Query(), default));
            Assert.AreEqual((DataSyncPeerErrorCode.Unreachable, "NodeUnreachable"), (gone.Code, gone.Message),
                "A verified session, then nobody there.");
            gone = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
                desk.NewPeerClient().GetHeadAsync("node-nas", Query(), default));
            Assert.AreEqual((DataSyncPeerErrorCode.Unreachable, "NodeUnreachable"), (gone.Code, gone.Message),
                "Nobody answers info.");
        }
        finally
        {
            if (!nasStopped) await nas.DisposeAsync();
        }
    }

    /// <summary>
    /// Answers this device cannot act on: another node's head, a malformed manifest, a page over the budget, and a
    /// source that does not answer in time. The caller's own cancellation stays a cancellation.
    /// </summary>
    [TestMethod]
    public async Task AnAnswerThisDeviceCannotReadIsRefusedAndASilentSourceTimesOut()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);
        var client = desk.PeerClient;
        async Task<DataSyncPeerException> Refused(Func<Task> call) =>
            await Assert.ThrowsExactlyAsync<DataSyncPeerException>(call);

        // Its session proved it is the NAS; a head that says it is another node's feed is not read as that node's.
        feed.HeadOverride = head => head with { NodeId = "node-pc" };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetHeadAsync("node-nas", Query(), default))).Code);
        feed.HeadOverride = head => head with { Kinds = [head.Kinds[0], head.Kinds[0]] };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetHeadAsync("node-nas", Query(), default))).Code);
        feed.HeadOverride = head => head with { ActorId = "not-an-actor" };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetHeadAsync("node-nas", Query(), default))).Code);
        feed.HeadOverride = null;
        feed.ManifestOverride = manifest => manifest with { Kinds = [manifest.Kinds[0] with { ContentHash = "sha256:nope" }] };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetManifestAsync("node-nas", Query(), default))).Code);
        feed.ManifestOverride = manifest => manifest with { SnapshotId = "../other" };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetManifestAsync("node-nas", Query(), default))).Code);
        feed.ManifestOverride = manifest => manifest with { Attention = null! };
        Assert.AreEqual(DataSyncPeerErrorCode.InvalidResponse, (await Refused(() => client.GetManifestAsync("node-nas", Query(), default))).Code);
        feed.ManifestOverride = null;

        feed.Pages[("customProperty", "p9")] = new byte[FederationHttpClient.MaxControlResponseBytes + 1];
        Assert.AreEqual(DataSyncPeerErrorCode.TooLarge, (await Refused(() =>
            client.GetPageAsync("node-nas", "snapshot-1", "customProperty", 0, "p9", default))).Code);

        feed.HoldHeads = true;
        var slow = desk.NewPeerClient(deadline: TimeSpan.FromMilliseconds(500));
        var timeout = await Refused(() => slow.GetHeadAsync("node-nas", Query(), default));
        Assert.AreEqual((DataSyncPeerErrorCode.Unreachable, "timeout"), (timeout.Code, timeout.Message));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetHeadAsync("node-nas", Query(), cancel.Token));
        feed.HoldHeads = false;
        Assert.AreEqual("node-nas", (await client.GetHeadAsync("node-nas", Query(), default)).NodeId);
    }

    /// <summary>
    /// One exchange per peer at a time (§7.6): a second call for the same peer waits for the first, and past its wait
    /// answers Busy without reaching the source; another peer is never held up.
    /// </summary>
    [TestMethod]
    public async Task OneExchangePerPeerAtATime()
    {
        var feed = new ScriptedFeed("node-nas");
        var other = new ScriptedFeed("node-pc");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await using var pc = await DataSyncNodeHost.StartAsync("node-pc", "PC", feed: other);
        await PairAsync(desk, nas);
        await PairAsync(desk, pc);

        // Past the wait: Busy, and the source never sees the second call.
        var impatient = desk.NewPeerClient(fetchWait: TimeSpan.FromMilliseconds(200));
        feed.HoldHeads = true;
        var held = impatient.GetHeadAsync("node-nas", Query(), default);
        await feed.WaitForInFlightAsync(1);
        var busy = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            impatient.GetManifestAsync("node-nas", Query(), default));
        Assert.AreEqual((DataSyncPeerErrorCode.Busy, "fetchInProgress"), (busy.Code, busy.Message));
        Assert.AreEqual("node-pc", (await impatient.GetHeadAsync("node-pc", Query(), default)).NodeId,
            "Another peer is not held up.");
        feed.HoldHeads = false;
        await held;
        Assert.IsFalse(feed.Calls.Any(c => c.Call == "manifest"));

        // Within the wait: the second call goes once the first is answered, never beside it.
        var patient = desk.NewPeerClient(fetchWait: TimeSpan.FromSeconds(30));
        feed.HoldHeads = true;
        var first = patient.GetHeadAsync("node-nas", Query(), default);
        await feed.WaitForInFlightAsync(1);
        var second = patient.GetManifestAsync("node-nas", Query(), default);
        await Task.Delay(300);
        Assert.IsFalse(second.IsCompleted);
        Assert.IsFalse(feed.Calls.Any(c => c.Call == "manifest"), "The second call waits at this device.");
        feed.HoldHeads = false;
        await first;
        Assert.AreEqual("snapshot-1", (await second).SnapshotId);
        Assert.AreEqual(1, feed.MaxInFlight);
    }

    /// <summary>
    /// One fetch per peer at a time (§7.6, must-fix 6): a fetch holds the peer from its head to its last page, so no
    /// other fetch's manifest comes in between and discards at the source the snapshot it reads. Its own calls never
    /// wait; a second fetch, or a lone call, waits until it is released, and past the wait answers Busy without
    /// reaching the source.
    /// </summary>
    [TestMethod]
    public async Task AFetchHoldsThePeerFromItsHeadToItsLastPage()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await using var pc = await DataSyncNodeHost.StartAsync("node-pc", "PC", feed: new ScriptedFeed("node-pc"));
        await PairAsync(desk, nas);
        await PairAsync(desk, pc);
        var client = desk.NewPeerClient(fetchWait: TimeSpan.FromSeconds(30));
        var manifestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readPages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each fetch runs in a flow of its own, as the cycle and a review's "Fetch again" do.
        async Task Cycle()
        {
            await using var fetch = await client.AcquireFetchAsync("node-nas", default);
            await client.GetHeadAsync("node-nas", Query(), default);
            var manifest = await client.GetManifestAsync("node-nas", Query(), default);
            manifestRead.SetResult();
            await readPages.Task;
            await client.GetPageAsync("node-nas", manifest.SnapshotId, "customProperty", 0, null, default);
            // Taken again in the same flow, it waits for nothing.
            await using (await client.AcquireFetchAsync("node-nas", default))
                await client.GetPageAsync("node-nas", manifest.SnapshotId, "customProperty", 0, "p2", default);
        }
        async Task Refetch()
        {
            await manifestRead.Task;
            await using var fetch = await client.AcquireFetchAsync("node-nas", default);
            await client.GetManifestAsync("node-nas", Query(), default);
        }
        async Task Lone()
        {
            await manifestRead.Task;
            await client.GetHeadAsync("node-nas", Query(), default);
        }

        var cycle = Cycle();
        var refetch = Refetch();
        var lone = Lone();
        await manifestRead.Task;
        await Task.Delay(300);
        Assert.IsFalse(refetch.IsCompleted || lone.IsCompleted, "Both wait for the fetch that holds the peer.");
        Assert.AreEqual("node-pc", (await client.GetHeadAsync("node-pc", Query(), default)).NodeId,
            "Another peer is not held up.");
        readPages.SetResult();
        await Task.WhenAll(cycle, refetch, lone);

        var calls = feed.Calls.Select(c => c.Call).ToArray();
        CollectionAssert.AreEqual(new[] { "head", "manifest", "changes", "changes" }, calls[..4],
            "Nothing came between the cycle's manifest and its pages.");
        CollectionAssert.AreEquivalent(new[] { "manifest", "head" }, calls[4..]);
        Assert.AreEqual(1, feed.MaxInFlight);

        // Past the wait: Busy for a second fetch and a lone call alike, and the source never hears of them.
        var impatient = desk.NewPeerClient(fetchWait: TimeSpan.FromMilliseconds(200));
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hold()
        {
            await using var fetch = await impatient.AcquireFetchAsync("node-nas", default);
            holding.SetResult();
            await release.Task;
        }
        var holder = Hold();
        await holding.Task;
        var before = feed.Calls.Count;
        foreach (var call in new Func<Task>[]
                 {
                     () => impatient.AcquireFetchAsync("node-nas", default),
                     () => impatient.GetManifestAsync("node-nas", Query(), default)
                 })
        {
            var busy = await Assert.ThrowsExactlyAsync<DataSyncPeerException>(call);
            Assert.AreEqual((DataSyncPeerErrorCode.Busy, "fetchInProgress"), (busy.Code, busy.Message));
        }
        Assert.AreEqual(before, feed.Calls.Count);
        release.SetResult();
        await holder;
        await using (await impatient.AcquireFetchAsync("node-nas", default))
            Assert.AreEqual("snapshot-1", (await impatient.GetManifestAsync("node-nas", Query(), default)).SnapshotId,
                "Released, the peer is free at once.");
        Assert.AreEqual(DataSyncPeerErrorCode.AccessMissing, (await Assert.ThrowsExactlyAsync<DataSyncPeerException>(
            () => impatient.AcquireFetchAsync("../node-nas", default))).Code);
    }

    /// <summary>
    /// §7.6: a source that accepts connections but does not answer info or the handshake in time is unreachable, not a
    /// device this one lost access to, and the probe still answers. Only this device dropping its own grant mid-read
    /// reads as access missing.
    /// </summary>
    [TestMethod]
    public async Task ASourceThatDoesNotAnswerInTimeIsUnreachableAndOnlyADroppedGrantIsAccessMissing()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);

        // Fresh sessions ask info first, which the silent source holds past the client's own deadline for it.
        nas.Answer = DataSyncNodeHost.Silent;
        var head = Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            desk.NewPeerClient().GetHeadAsync("node-nas", Query(), default));
        var probe = desk.NewPeerClient().ProbeAsync("node-nas", default);
        var slow = await head;
        Assert.AreEqual((DataSyncPeerErrorCode.Unreachable, "timeout"), (slow.Code, slow.Message));
        Assert.AreEqual(new DataSyncPeerProbe("node-nas", "NAS", nas.Address, true, null, null, "Offline"), await probe);
        nas.Answer = null;
        Assert.AreEqual(0, feed.Calls.Count);
        Assert.IsTrue(await desk.Grants.HasOutboundGrantAsync("node-nas", default));

        // This device drops its grant while a head is on its way: the read ends as access missing.
        var client = desk.PeerClient;
        Assert.AreEqual("node-nas", (await client.GetHeadAsync("node-nas", Query(), default)).NodeId);
        feed.HoldHeads = true;
        var dropped = Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            client.GetHeadAsync("node-nas", Query(), default));
        await feed.WaitForInFlightAsync(1);
        await desk.Grants.ForgetOutboundAsync("node-nas", default);
        var missing = await dropped;
        Assert.AreEqual((DataSyncPeerErrorCode.AccessMissing, "accessRemoved"), (missing.Code, missing.Message));
        feed.HoldHeads = false;
    }

    [TestMethod]
    public async Task AProbeSaysWhatThisDeviceKnowsAndWhatThePeerSaysAboutDataSync()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: new ScriptedFeed("node-nas"));
        await PairAsync(desk, nas);

        var readable = await desk.PeerClient.ProbeAsync("node-nas", default);
        Assert.AreEqual(new DataSyncPeerProbe("node-nas", "NAS", nas.Address, true, 1, true, "Online"), readable);
        await nas.Grants.SetSharingEnabledAsync(false, false, default);
        Assert.AreEqual(new DataSyncPeerProbe("node-nas", "NAS", nas.Address, true, null, false, "Unauthorized"),
            await desk.NewPeerClient().ProbeAsync("node-nas", default));
        await nas.Grants.SetSharingEnabledAsync(true, false, default);

        // Known, but not readable (any more): what its public info says.
        await desk.Grants.ForgetOutboundAsync("node-nas", default);
        Assert.AreEqual(new DataSyncPeerProbe("node-nas", "NAS", nas.Address, false, 1, true, "Online"),
            await desk.PeerClient.ProbeAsync("node-nas", default));
        // The NAS knows the desk only as a reader of its own, with no address to ask.
        Assert.AreEqual(new DataSyncPeerProbe("node-desk", "Desk", null, false, null, null, "Unknown"),
            await nas.PeerClient.ProbeAsync("node-desk", default));
        Assert.AreEqual(DataSyncPeerErrorCode.AccessMissing, (await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            desk.PeerClient.ProbeAsync("node-somebody", default))).Code);
    }

    // ---- The hub relay rule (§7.7, D03) --------------------------------------------------------------------------

    /// <summary>
    /// The desk reads the NAS, and the NAS reads the PC. The desk can still never read the PC through the NAS, nor
    /// with a library grant it holds for the PC: nothing is sent, and the PC's feed is never asked.
    /// </summary>
    [TestMethod]
    public async Task ADevicePullsOnlyFromANodeItHoldsADirectDefinitionsGrantFor()
    {
        var nasFeed = new ScriptedFeed("node-nas");
        var pcFeed = new ScriptedFeed("node-pc");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: nasFeed);
        await using var pc = await DataSyncNodeHost.StartAsync("node-pc", "PC", feed: pcFeed);
        await PairAsync(desk, nas);
        await PairAsync(nas, pc);
        Assert.AreEqual("node-pc", (await nas.PeerClient.GetHeadAsync("node-pc", Query(), default)).NodeId);
        pcFeed.Calls.Clear();

        var head = await desk.PeerClient.GetHeadAsync("node-nas", Query(), default);
        Assert.AreEqual("node-nas", head.NodeId);
        Assert.AreEqual("node-desk", nasFeed.Calls.Single().Reader.NodeId);
        Assert.AreEqual(DataSyncPeerErrorCode.AccessMissing, (await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            desk.PeerClient.GetHeadAsync("node-pc", Query(), default))).Code);

        // A library grant for the PC does not count.
        await pc.Peers.SetSharingAsync(true);
        var code = await pc.Peers.IssueInvitationAsync();
        Assert.AreEqual("granted", (await desk.Services.GetRequiredService<NodePairingClient>()
            .ConnectAsync(pc.Address, code.Code)).Outcome);
        Assert.AreEqual(DataSyncPeerErrorCode.AccessMissing, (await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() =>
            desk.PeerClient.GetManifestAsync("node-pc", Query(), default))).Code);
        Assert.AreEqual(0, pcFeed.Calls.Count);
        Assert.AreEqual(1, nasFeed.Calls.Count);
    }

    /// <summary>The source reads for the grant's subject only: nothing in a request names another reader (§7.7).</summary>
    [TestMethod]
    public async Task TheReaderIsTheGrantsSubjectWhateverTheRequestSays()
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);
        var session = await desk.Sessions.GetAsync("node-nas", FederationScopes.DataSyncRead);

        using var response = await desk.Transport.SendAsync(session, HttpMethod.Get,
            "/federation/v1/export/datasync/head?reader=node-pc&node=node-pc&nodeId=node-pc&onBehalfOf=node-pc");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var reader = feed.Calls.Single().Reader;
        Assert.AreEqual(("node-desk", session.GrantId, "Desk"), (reader.NodeId, reader.GrantId, reader.Name));
    }

    // ---- The feed endpoint -----------------------------------------------------------------------------------------

    /// <summary>
    /// Every query value is checked before the feed source sees it (§7.5), and refused in the federation error shape
    /// a reader maps, whatever is wrong with it — model binding's own included.
    /// </summary>
    [TestMethod]
    [DataRow("head?mode=both")]
    [DataRow("head?since=customProperty:abc")]
    [DataRow("head?since=customProperty:9007199254740993")]
    [DataRow("head?since=Custom:1")]
    [DataRow("head?since=customProperty:1,customProperty:2")]
    [DataRow("head?actor=0123456789ABCDEF")]
    [DataRow("head?state=needs%20you")]
    [DataRow("manifest?since=a:1")]
    [DataRow("changes?kind=customProperty&since=0")]
    [DataRow("changes?snapshot=s.1&kind=customProperty&since=0")]
    [DataRow("changes?snapshot=s&kind=Custom&since=0")]
    [DataRow("changes?snapshot=s&kind=customProperty")]
    [DataRow("changes?snapshot=s&kind=customProperty&since=abc")]
    [DataRow("changes?snapshot=s&kind=customProperty&since=-1")]
    [DataRow("changes?snapshot=s&kind=customProperty&since=0&cursor=p%2F2")]
    public async Task TheFeedRefusesAMalformedQueryInTheFederationShape(string pathAndQuery)
    {
        var feed = new ScriptedFeed("node-nas");
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS", feed: feed);
        await PairAsync(desk, nas);
        var session = await desk.Sessions.GetAsync("node-nas", FederationScopes.DataSyncRead);

        using var response = await desk.Transport.SendAsync(session, HttpMethod.Get,
            "/federation/v1/export/datasync/" + pathAndQuery);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(("InvalidFeedQuery", false),
            (body.RootElement.GetProperty("code").GetString(), body.RootElement.GetProperty("retryable").GetBoolean()));
        Assert.AreEqual(0, feed.Calls.Count);
    }

    // ---- The mapping itself ----------------------------------------------------------------------------------------

    /// <summary>Every row of §7.6's table, and codes this build does not know, read by their status.</summary>
    [TestMethod]
    [DataRow("NodeUnreachable", 503, DataSyncPeerErrorCode.Unreachable)]
    [DataRow("NodeNotAuthorized", 403, DataSyncPeerErrorCode.AccessMissing)]
    [DataRow("GrantRevoked", 401, DataSyncPeerErrorCode.AccessRevoked)]
    [DataRow("InvalidNodeSignature", 401, DataSyncPeerErrorCode.AccessRevoked)]
    [DataRow("ScopeNotGranted", 403, DataSyncPeerErrorCode.AccessRevoked)]
    [DataRow("DataSyncSharingDisabled", 403, DataSyncPeerErrorCode.PeerSharingOff)]
    [DataRow("SharingDisabled", 403, DataSyncPeerErrorCode.PeerSharingOff)]
    [DataRow("RemoteAccessDisabled", 403, DataSyncPeerErrorCode.PeerRemoteAccessOff)]
    [DataRow("NodeRouteForbidden", 403, DataSyncPeerErrorCode.PeerTooOld)]
    [DataRow("ProtocolUnsupported", 409, DataSyncPeerErrorCode.PeerTooOld)]
    [DataRow("NotImplemented", 501, DataSyncPeerErrorCode.PeerTooOld)]
    [DataRow("LibraryEpochChanged", 409, DataSyncPeerErrorCode.PeerReset)]
    [DataRow("IdentityConflict", 409, DataSyncPeerErrorCode.PeerReset)]
    [DataRow("SourceRestorePending", 409, DataSyncPeerErrorCode.PeerRestorePending)]
    [DataRow("SnapshotExpired", 410, DataSyncPeerErrorCode.SnapshotExpired)]
    [DataRow("SnapshotMismatch", 409, DataSyncPeerErrorCode.SnapshotExpired)]
    [DataRow("CursorSuperseded", 409, DataSyncPeerErrorCode.CursorSuperseded)]
    [DataRow("Busy", 503, DataSyncPeerErrorCode.Busy)]
    [DataRow("TooManySnapshots", 429, DataSyncPeerErrorCode.Busy)]
    [DataRow("NodeResponseTooLarge", 502, DataSyncPeerErrorCode.TooLarge)]
    [DataRow("SnapshotTooLarge", 413, DataSyncPeerErrorCode.TooLarge)]
    [DataRow("InvalidNodeResponse", 502, DataSyncPeerErrorCode.InvalidResponse)]
    [DataRow("SomethingNew", 401, DataSyncPeerErrorCode.AccessRevoked)]
    [DataRow(null, 404, DataSyncPeerErrorCode.PeerTooOld)]
    [DataRow(null, 410, DataSyncPeerErrorCode.SnapshotExpired)]
    [DataRow(null, 429, DataSyncPeerErrorCode.Busy)]
    [DataRow(null, 502, DataSyncPeerErrorCode.Unreachable)]
    [DataRow("SomethingNew", 409, DataSyncPeerErrorCode.InvalidResponse)]
    public void EveryFederationRefusalHasItsDataSyncError(string? code, int status, DataSyncPeerErrorCode expected) =>
        Assert.AreEqual(expected, FederationDataSyncPeerClient.Classify(code, status));

    [TestMethod]
    public void RetryAfterIsReadAsADelayOrADateWithinADay()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        int? Read(Action<HttpResponseHeaders>? set)
        {
            using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            set?.Invoke(response.Headers);
            return FederationDataSyncPeerClient.RetryAfterSeconds(response, now);
        }

        Assert.IsNull(Read(null));
        Assert.AreEqual(30, Read(h => h.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30))));
        Assert.AreEqual(90, Read(h => h.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(90))));
        Assert.AreEqual(0, Read(h => h.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(-5))));
        Assert.AreEqual(86_400, Read(h => h.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(30))));
    }

    /// <summary>What goes on the wire is what the source's own checks accept; anything else never leaves.</summary>
    [TestMethod]
    public void TheDeclaredQueryIsCheckedBeforeItIsSent()
    {
        Assert.AreEqual("mode=follow&since=customProperty:4,extensionGroup:0&actor=0123456789abcdef&state=paused:PeerReset",
            FederationDataSyncPeerClient.QueryString(new DataSyncFeedQuery("follow",
                new Dictionary<string, long> { ["extensionGroup"] = 0, ["customProperty"] = 4 }, Actor, "paused:PeerReset")));
        Assert.AreEqual("", FederationDataSyncPeerClient.QueryString(new DataSyncFeedQuery(null, new Dictionary<string, long>(), null, null)));
        foreach (var invalid in new[]
                 {
                     new DataSyncFeedQuery("both", new Dictionary<string, long>(), null, null),
                     new DataSyncFeedQuery(null, new Dictionary<string, long> { ["custom property"] = 1 }, null, null),
                     new DataSyncFeedQuery(null, new Dictionary<string, long> { ["customProperty"] = -1 }, null, null),
                     new DataSyncFeedQuery(null, Enumerable.Range(0, 17).ToDictionary(i => $"kind{i}", _ => 0L), null, null),
                     new DataSyncFeedQuery(null, new Dictionary<string, long>(), "ABCDEF0123456789", null),
                     new DataSyncFeedQuery(null, new Dictionary<string, long>(), null, "needs you"),
                     new DataSyncFeedQuery(null, new Dictionary<string, long>(), null, new string('a', 65))
                 })
            Assert.ThrowsExactly<ArgumentException>(() => FederationDataSyncPeerClient.QueryString(invalid));
    }

    [TestMethod]
    public void TheServiceReadsFeedsWithThisClient()
    {
        var services = new ServiceCollection().AddFederatedLibrary();
        var registration = services.Last(d => d.ServiceType == typeof(IDataSyncPeerClient));
        Assert.AreEqual(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(FederationDataSyncPeerClient) &&
                                        d.ImplementationType == typeof(FederationDataSyncPeerClient) &&
                                        d.Lifetime == ServiceLifetime.Singleton));
        Assert.AreEqual(typeof(DataSyncNodeInfoContributor),
            services.Last(d => d.ServiceType == typeof(INodeInfoContributor)).ImplementationType);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    private static DataSyncFeedQuery Query() => new("follow", new Dictionary<string, long> { ["customProperty"] = 0 },
        Actor, "ok");

    /// <summary><paramref name="reader"/> may read <paramref name="source"/>'s definitions, with one of its codes.</summary>
    private static async Task PairAsync(DataSyncNodeHost reader, DataSyncNodeHost source)
    {
        await source.Grants.SetSharingEnabledAsync(true, false, default);
        var code = await source.Grants.CreateInvitationAsync(new DataSyncInvitationInput(false), default);
        var outcome = await reader.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, source.Address, code.Code, DataSyncRequestIntent.Follow), default);
        Assert.AreEqual("granted", outcome.Outcome);
    }

    /// <summary>A page holding one multilevel property whose first node goes <paramref name="levels"/> deep.</summary>
    private static byte[] DeepPage(int levels)
    {
        var node = new JsonObject { ["label"] = $"level {levels}", ["uuid"] = $"n{levels}" };
        for (var level = levels - 1; level >= 1; level--)
            node = new JsonObject { ["children"] = new JsonArray(node), ["label"] = $"level {level}", ["uuid"] = $"n{level}" };
        var page = new JsonObject
        {
            ["complete"] = true, ["kind"] = "customProperty", ["nextCursor"] = null,
            ["records"] = new JsonArray(new JsonObject
            {
                ["chunks"] = 0, ["content"] = new JsonObject { ["nodes"] = new JsonArray(node), ["type"] = 17 },
                ["deleted"] = false, ["keys"] = new JsonArray("7f3c0000000000000000000000000000"), ["origin"] = "node-nas",
                ["schemaVersion"] = 1, ["seq"] = 1, ["vv"] = new JsonObject { ["0123456789abcdef"] = 1 }
            }),
            ["sinceSeq"] = 0, ["snapshotId"] = "snapshot-1"
        };
        return CanonicalJson.SerializeToUtf8Bytes(page);
    }

    /// <summary>
    /// A feed source that answers as <see cref="NodeId"/> would: a canned head, manifest and pages, the refusals a
    /// test queues, and heads held until a test lets them go. It records every call and how many overlapped.
    /// </summary>
    private sealed class ScriptedFeed(string nodeId) : IDataSyncFeedSource
    {
        public static readonly string EmptyHash = ContentHash.OfCanonicalBytes("[]"u8);
        private readonly object _lock = new();
        private int _inFlight;
        private volatile TaskCompletionSource _release = NewRelease(released: true);

        public string NodeId => nodeId;
        public ConcurrentQueue<DataSyncFeedException> Errors { get; } = new();
        public List<FeedCall> Calls { get; } = [];
        public Dictionary<(string Kind, string? Cursor), byte[]> Pages { get; } = new();
        public Func<DataSyncFeedHead, DataSyncFeedHead>? HeadOverride { get; set; }
        public Func<DataSyncFeedManifest, DataSyncFeedManifest>? ManifestOverride { get; set; }
        public int MaxInFlight { get; private set; }

        /// <summary>While true, heads wait (until let go, or until their request is aborted).</summary>
        public bool HoldHeads
        {
            set
            {
                if (value) _release = NewRelease(released: false);
                else _release.TrySetResult();
            }
        }

        public async Task<DataSyncFeedHead> GetHeadAsync(DataSyncReader reader, DataSyncFeedQuery query,
            CancellationToken ct)
        {
            using var _ = Enter(new FeedCall("head", reader, query));
            await _release.Task.WaitAsync(ct);
            Throw();
            var head = new DataSyncFeedHead(nodeId, "epoch-1", Actor, 1, 1, "2.4.0-beta.150", 131,
                [new DataSyncFeedKindHead("customProperty", 1, 131, false, 1)],
                new DataSyncSourceAttention(true, 2, 0, false, 0), 12,
                new DataSyncFeedCounterpart("twoWay", true, ["customProperty"]));
            return HeadOverride?.Invoke(head) ?? head;
        }

        public Task<DataSyncFeedManifest> CreateSnapshotAsync(DataSyncReader reader, DataSyncFeedQuery query,
            CancellationToken ct)
        {
            using var _ = Enter(new FeedCall("manifest", reader, query));
            Throw();
            var manifest = new DataSyncFeedManifest("snapshot-1", 120_000, nodeId, "epoch-1", Actor, 1, 1,
                "2.4.0-beta.150", [new DataSyncFeedKind("customProperty", 1, 131, 0, 3, 0, EmptyHash, 0, 0, false)],
                null, new DataSyncSourceAttention(true, 0, 0, false, 0));
            return Task.FromResult(ManifestOverride?.Invoke(manifest) ?? manifest);
        }

        public Task<byte[]> GetPageAsync(DataSyncReader reader, string snapshotId, string kind, long sinceSeq,
            string? cursor, CancellationToken ct)
        {
            using var _ = Enter(new FeedCall("changes", reader, null, snapshotId, kind, sinceSeq, cursor));
            Throw();
            return Task.FromResult(PageBytes(kind, cursor));
        }

        public byte[] PageBytes(string kind, string? cursor) => Pages.TryGetValue((kind, cursor), out var bytes)
            ? bytes
            : Encoding.UTF8.GetBytes(
                $"{{\"complete\":{(cursor == null ? "false" : "true")},\"kind\":\"{kind}\",\"nextCursor\":" +
                $"{(cursor == null ? "\"p2\"" : "null")},\"records\":[],\"sinceSeq\":0,\"snapshotId\":\"snapshot-1\"}}");

        public async Task WaitForInFlightAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref _inFlight) < count)
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"{count} calls never reached the source.");
                await Task.Delay(10);
            }
        }

        private IDisposable Enter(FeedCall call)
        {
            lock (_lock)
            {
                Calls.Add(call);
                MaxInFlight = Math.Max(MaxInFlight, ++_inFlight);
            }
            return new Exit(this);
        }

        private void Throw()
        {
            if (Errors.TryDequeue(out var error)) throw error;
        }

        private static TaskCompletionSource NewRelease(bool released)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (released) source.SetResult();
            return source;
        }

        private sealed class Exit(ScriptedFeed feed) : IDisposable
        {
            public void Dispose()
            {
                lock (feed._lock) feed._inFlight--;
            }
        }
    }

    private sealed record FeedCall(string Call, DataSyncReader Reader, DataSyncFeedQuery? Query,
        string? SnapshotId = null, string? Kind = null, long? SinceSeq = null, string? Cursor = null);
}
