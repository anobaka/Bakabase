using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// A source cannot keep a pull going (§12: everything peer-supplied is budgeted). The reader budgets a kind's pages by
/// count as well as by bytes, and the fetcher gives up a pull that outlasts <see cref="DataSyncSchedule.SnapshotDeadline"/>,
/// so no link holds the <c>DataSync</c> task's other links waiting.
/// </summary>
[TestClass]
public class DataSyncPageBudgetTests
{
    private const string Snapshot = "snap-1";
    private const string Kind = DataSyncKindIds.ExtensionGroup;

    private static string Key(int i) => (i + 1).ToString("x32", CultureInfo.InvariantCulture);

    private static DataSyncWireRecord Tombstone(int i) =>
        new([Key(i)], "node-a", i + 1,
            DataSyncVersionVector.Empty.With(new DataSyncActorId("0123456789abcdef"), i + 1), null, true, 1, null,
            null, null, null);

    private static byte[] Page(string snapshotId, string kind, long sinceSeq, string? nextCursor,
        params JsonObject[] items)
    {
        var page = new JsonObject
        {
            ["complete"] = nextCursor is null,
            ["kind"] = kind,
            ["records"] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray()),
            ["sinceSeq"] = sinceSeq,
            ["snapshotId"] = snapshotId,
        };
        if (nextCursor is not null) page["nextCursor"] = nextCursor;
        return Encoding.UTF8.GetBytes(page.ToJsonString());
    }

    private static DataSyncFeedKind ManifestKind(IReadOnlyList<DataSyncWireRecord> records, int recordCount) =>
        new(Kind, 1, records.Count == 0 ? 0 : records[^1].Seq, 0, 0, records.Count,
            DataSyncKindPageReader.KindContentHash(records), 0, recordCount, false);

    private static IDataSyncKindPageAssembly Begin(DataSyncFeedKind manifestKind, DataSyncLimits? limits = null) =>
        new DataSyncKindPageReader([new MemoryDataSyncKind(Kind)], limits ?? DataSyncLimits.Default)
            .Begin(Kind, Snapshot, manifestKind, false);

    [TestMethod]
    public void A_page_that_is_not_the_last_must_carry_a_record()
    {
        var empty = Begin(ManifestKind([], 0));
        Assert.IsTrue(empty.Add(Page(Snapshot, Kind, 0, null)).Complete, "a kind with no records is one empty page");
        Assert.IsNotNull(empty.Complete());

        var records = new[] { Tombstone(0) };
        var assembly = Begin(ManifestKind(records, 1));
        var step = assembly.Add(Page(Snapshot, Kind, 0, "p1"));
        Assert.IsFalse(step.Ok, "a source never writes an empty page that is not the last");
        Assert.AreEqual(DataSyncKindPageReader.Corrupted, assembly.Problem);
    }

    [TestMethod]
    public void More_records_than_the_manifest_counted_discard_the_pull_at_once()
    {
        var records = Enumerable.Range(0, 3).Select(Tombstone).ToList();
        var assembly = Begin(ManifestKind(records, 2));
        Assert.IsTrue(assembly.Add(Page(Snapshot, Kind, 0, "p1", DataSyncWireFormat.ToJson(records[0]))).Ok);
        var step = assembly.Add(Page(Snapshot, Kind, 0, "p2", DataSyncWireFormat.ToJson(records[1]),
            DataSyncWireFormat.ToJson(records[2])));
        Assert.IsFalse(step.Ok, "the third record is one more than the manifest counted");
        Assert.AreEqual(DataSyncKindPageReader.Corrupted, assembly.Problem);
    }

    [TestMethod]
    public void A_page_fits_one_federation_response()
    {
        // A record travels whole, so a page must carry the largest one a source serves (3.5 MiB) within the response
        // cap every node-to-node answer is read under.
        Assert.AreEqual(3_584 << 10, DataSyncLimits.Default.MaxPageBytes);
        Assert.IsTrue(DataSyncLimits.Default.MaxPageBytes < FederationHttpClient.MaxControlResponseBytes);
    }

    [TestMethod]
    public async Task A_source_serving_endless_empty_pages_is_discarded_on_the_first()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false, configure: services =>
            services.AddScoped<IDataSyncKindPageReader>(_ => new DataSyncKindPageReader(
                [new MemoryDataSyncKind(DataSyncKindIds.ExtensionGroup), new MemoryDataSyncKind(DataSyncKindIds.CustomProperty)],
                DataSyncLimits.Default)));
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            { [DataSyncKindIds.ExtensionGroup] = 0, [DataSyncKindIds.CustomProperty] = 0 }));
        var peer = h.Peers.Peers["nas"];
        var served = 0;
        peer.Serve = (snapshotId, kind, sinceSeq, _) =>
            Page(snapshotId, kind, sinceSeq, "p" + Interlocked.Increment(ref served).ToString(CultureInfo.InvariantCulture));

        await h.FetchOnceAsync();

        Assert.AreEqual(1, peer.Pages, "the first empty page that is not the last ends the pull");
        Assert.IsNull(h.State.PeekPull(link.Id), "never planned from an incomplete snapshot");
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.InvalidResponse), h.Link(link.Id).LastErrorCode);
        Assert.AreEqual(DataSyncKindPageReader.Corrupted, h.Link(link.Id).LastErrorDetail);
        Assert.AreEqual(0, h.Link(link.Id).GetCursors()[DataSyncKindIds.ExtensionGroup], "the cursor did not move");
    }

    [TestMethod]
    public async Task A_pull_that_outlasts_the_deadline_is_given_up_as_unreachable_and_the_next_link_is_fetched()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var slow = h.AddLink("slow", l => l.SetCursors(new Dictionary<string, long>
            { [DataSyncKindIds.ExtensionGroup] = 3, [DataSyncKindIds.CustomProperty] = 2 }));
        var next = h.AddLink("next", l => l.SetCursors(new Dictionary<string, long>
            { [DataSyncKindIds.ExtensionGroup] = 3, [DataSyncKindIds.CustomProperty] = 2 }));
        var source = h.Peers.Peers["slow"];
        // Every page is sound and names another; each takes a minute.
        source.PagesPerKind = int.MaxValue;
        source.OnPage = () => h.Clock.Advance(TimeSpan.FromMinutes(1));
        var started = h.Clock.UtcNow;

        await h.FetchOnceAsync();

        Assert.AreEqual((int)DataSyncSchedule.SnapshotDeadline.TotalMinutes, source.Pages,
            "no page is asked for once the deadline has passed");
        Assert.IsNull(h.State.PeekPull(slow.Id));
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.Unreachable), h.Link(slow.Id).LastErrorCode);
        Assert.AreEqual("timeout", h.Link(slow.Id).LastErrorDetail);
        Assert.AreEqual(1, h.Link(slow.Id).ConsecutiveFailures);
        Assert.AreEqual(started + DataSyncSchedule.SnapshotDeadline + DataSyncSchedule.Backoff(1),
            h.State.GetAttempt(slow.Id).Due, "retried like a peer that did not answer");

        Assert.IsNotNull(h.State.PeekPull(next.Id), "the cycle went on to the next link");
        Assert.IsNull(h.Link(next.Id).LastErrorCode);
    }
}
