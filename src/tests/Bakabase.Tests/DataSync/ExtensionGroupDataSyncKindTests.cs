using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Kinds;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The extension group adapter (v3.1 §3.4, §8.3; spec §3.8) over the real service and database: canonical content,
/// the raw-hash fast path, writes only through <see cref="IExtensionGroupService"/> (the stored case of existing
/// extensions kept, N10), the per-item hash check, pre-images, caches, and no echo after an update.
/// </summary>
[TestClass]
public class ExtensionGroupDataSyncKindTests
{
    private const string Kind = DataSyncKindIds.ExtensionGroup;

    private IServiceProvider _services = null!;
    private IDataSyncKind _kind = null!;
    private IExtensionGroupService _groups = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(s =>
            s.AddExtensionGroupDataSyncKind(new TestExtensionGroupCodec()));
        _kind = _services.GetServices<IDataSyncKind>().Single(k => k.Codec.Descriptor.Kind == Kind);
        _groups = _services.GetRequiredService<IExtensionGroupService>();
    }

    private static JsonObject Content(string name, params string[] extensions) =>
        new() {["extensions"] = new JsonArray(extensions.Select(e => (JsonNode?) JsonValue.Create(e)).ToArray()), ["name"] = name};

    [TestMethod]
    public async Task Local_content_is_canonical()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".MKV", "mp4", "AVI"]));
        var docs = await _groups.Add(new ExtensionGroupAddInputModel("Docs", null));

        var all = await _kind.ReadAsync(null, default);

        Assert.AreEqual(2, all.Count);
        var first = all[0];
        Assert.AreEqual((video.Id.ToString(), 0, (string?) null, false),
            (first.LocalKey, first.Position, first.Fingerprint, first.Unreadable), "no fingerprint for extension groups (F11)");
        Assert.IsTrue(JsonNode.DeepEquals(Content("Video", ".avi", ".mkv", ".mp4"), first.Content),
            "trimmed, one dot, lowercased, deduplicated and sorted (v3.1 §3.4)");
        Assert.IsTrue(JsonNode.DeepEquals(Content("Docs"), all[1].Content));

        var one = (await _kind.ReadAsync([docs.Id.ToString()], default)).Single();
        Assert.AreEqual(1, one.Position, "the position within the kind, not within the request");
        CollectionAssert.AreEqual(new[] {video.Id.ToString(), docs.Id.ToString()},
            (await _kind.ReadOrderAsync(default)).ToArray(), "no order: id order");
    }

    [TestMethod]
    public async Task Raw_hashes_move_only_with_the_stored_row()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var docs = await _groups.Add(new ExtensionGroupAddInputModel("Docs", [".pdf"]));
        var before = await _kind.ReadRawHashesAsync(default);
        CollectionAssert.AreEquivalent(before.ToList(), (await _kind.ReadRawHashesAsync(default)).ToList());

        await _groups.Put(video.Id, new ExtensionGroupPutInputModel("Video", [".mkv", ".mp4"]));

        var after = await _kind.ReadRawHashesAsync(default);
        Assert.AreNotEqual(before[video.Id.ToString()], after[video.Id.ToString()]);
        Assert.AreEqual(before[docs.Id.ToString()], after[docs.Id.ToString()]);
    }

    [TestMethod]
    public async Task Creates_go_through_AddRange_in_input_order()
    {
        var outcome = await _kind.ApplyAsync(new ApplyBatch(Kind,
        [
            new CreateEntityOperation("i1", EntityKeys.None, "node", 0, Content("Video", ".mkv", ".mp4")),
            new CreateEntityOperation("i2", EntityKeys.None, "node", 1, Content("Docs", ".pdf")),
            new BindOnlyOperation("i3", "999", EntityKeys.None),
        ]), default);

        Assert.AreEqual(2, outcome.CreatedLocalKeysByItemId.Count);
        Assert.AreEqual(0, outcome.ChangedDuringApplyItemIds.Count);
        var video = (await _kind.ReadAsync([outcome.CreatedLocalKeysByItemId["i1"]], default)).Single();
        Assert.IsTrue(JsonNode.DeepEquals(Content("Video", ".mkv", ".mp4"), video.Content));
        Assert.IsTrue(int.Parse(outcome.CreatedLocalKeysByItemId["i1"]) < int.Parse(outcome.CreatedLocalKeysByItemId["i2"]));
        Assert.AreEqual("Docs", (await _groups.Get(int.Parse(outcome.CreatedLocalKeysByItemId["i2"]))).Name);
    }

    [TestMethod]
    public async Task An_update_writes_the_stored_extensions_minus_removals_plus_adds()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".MKV", ".mp4"]));
        var key = video.Id.ToString();
        var before = (await _kind.ReadAsync([key], default)).Single();
        var merged = Content("Videos", ".avi", ".mkv");

        var outcome = await _kind.ApplyAsync(new ApplyBatch(Kind,
            [new UpdateEntityOperation("i1", key, ContentHash.Of(before.Content), merged, EntityKeys.None, [".avi"], [".mp4"])]),
            default);

        Assert.AreEqual(0, outcome.ChangedDuringApplyItemIds.Count);
        var stored = await _groups.Get(video.Id);
        Assert.AreEqual("Videos", stored.Name);
        CollectionAssert.AreEquivalent(new[] {".MKV", ".avi"}, stored.Extensions!.ToArray(),
            "the stored case of an existing extension never changes (N10)");
        Assert.IsTrue(JsonNode.DeepEquals(merged, (await _kind.ReadAsync([key], default)).Single().Content),
            "the re-read canonical content equals the merged content");
    }

    [TestMethod]
    public async Task A_stale_expected_hash_is_ChangedDuringApply()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var key = video.Id.ToString();

        var outcome = await _kind.ApplyAsync(new ApplyBatch(Kind,
        [
            new UpdateEntityOperation("i1", key, "sha256:stale", Content("Videos", ".mkv"), EntityKeys.None, [], []),
            new UpdateEntityOperation("i2", "12345", "sha256:stale", Content("Gone"), EntityKeys.None, [], []),
            new DeleteEntityOperation("i3", key, "sha256:stale"),
        ]), default);

        CollectionAssert.AreEquivalent(new[] {"i1", "i2", "i3"}, outcome.ChangedDuringApplyItemIds.ToArray());
        Assert.AreEqual("Video", (await _groups.Get(video.Id)).Name, "nothing was written");
    }

    [TestMethod]
    public async Task A_delete_goes_through_the_service()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var key = video.Id.ToString();
        var current = (await _kind.ReadAsync([key], default)).Single();

        await _kind.ApplyAsync(new ApplyBatch(Kind, [new DeleteEntityOperation("i1", key, ContentHash.Of(current.Content))]),
            default);

        Assert.AreEqual(0, (await _kind.ReadAsync(null, default)).Count);
        Assert.AreEqual(0, (await _groups.GetAll()).Length);
    }

    [TestMethod]
    public async Task A_pre_image_restores_the_raw_row()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".MKV", ".mp4"]));
        var key = video.Id.ToString();
        var before = (await _kind.ReadAsync([key], default)).Single();
        var preImage = (await _kind.CapturePreImageAsync([key], default))[key];

        await _groups.Put(video.Id, new ExtensionGroupPutInputModel("Changed", [".txt"]));
        await _kind.RestoreAsync(key, preImage, default);

        var restored = await _groups.Get(video.Id);
        Assert.AreEqual("Video", restored.Name);
        CollectionAssert.AreEquivalent(new[] {".MKV", ".mp4"}, restored.Extensions!.ToArray());
        Assert.AreEqual(ContentHash.Of(before.Content), ContentHash.Of((await _kind.ReadAsync([key], default)).Single().Content));

        await _groups.Delete(video.Id);
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => _kind.RestoreAsync(key, preImage, default));
    }

    [TestMethod]
    public async Task ResetCaches_drops_what_the_service_cached()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var key = video.Id.ToString();
        await _kind.ReadAsync(null, default);

        // A write the cache never saw (another process's, or a rolled-back transaction's).
        var db = _services.GetRequiredService<BakabaseDbContext>();
        await db.ExtensionGroups.Where(g => g.Id == video.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, "Movies"));
        Assert.AreEqual("Video", (await _kind.ReadAsync([key], default)).Single().Content["name"]!.GetValue<string>());

        _kind.ResetCaches();

        Assert.AreEqual("Movies", (await _kind.ReadAsync([key], default)).Single().Content["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Extensions_are_unused_values_and_groups_have_no_order_or_subtype()
    {
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var key = video.Id.ToString();

        var usage = await _kind.GetUsageAsync(new Dictionary<string, IReadOnlyCollection<string>> {[key] = [".mkv", ".avi"]},
            default);
        Assert.AreEqual(0, usage[key].ValueCount);
        CollectionAssert.AreEquivalent(new[] {0, 0}, usage[key].ResourceCountByChildId.Values.ToArray());

        await _kind.ApplyOrderAsync([key], default);
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _kind.ChangeSubtypeAsync(key, "x", default));
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _kind.PreviewSubtypeChangeAsync(key, "x", default));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _kind.ApplyAsync(
            new ApplyBatch(Kind, [new ChangeSubtypeOperation("i1", key, "sha256:x", "x")]), default));
        Assert.ThrowsException<ArgumentException>(() => new ServiceCollection().AddExtensionGroupDataSyncKind(
            new MemoryCodec(DataSyncKindIds.CustomProperty, false)));
    }

    [TestMethod]
    public async Task Refresh_over_the_kind_and_no_echo_after_an_update_recorded_from_the_re_read()
    {
        _services.GetRequiredService<DataSyncActorGuard>().MarkVerified();
        var video = await _groups.Add(new ExtensionGroupAddInputModel("Video", [".MKV"]));
        var key = video.Id.ToString();
        var gate = _services.GetRequiredService<DataSyncGate>();
        var refresher = _services.GetRequiredService<DataSyncRefresher>();
        using (var lease = await gate.EnterAsync(null, default))
            Assert.AreEqual(1, (await refresher.RefreshAsync(lease, [Kind], false, default)).Changed);

        // An apply adds .avi; its record is taken from the re-read entity (§6.4), as the runner does.
        var before = (await _kind.ReadAsync([key], default)).Single();
        await _kind.ApplyAsync(new ApplyBatch(Kind,
            [new UpdateEntityOperation("i1", key, ContentHash.Of(before.Content), Content("Video", ".avi", ".mkv"), EntityKeys.None, [".avi"], [])]),
            default);
        var reread = (await _kind.ReadAsync([key], default)).Single();
        var form = DataSyncEntityForms.Evaluate(_kind.Codec, reread, DataSyncOverlay.None, false, null, null);
        var db = _services.GetRequiredService<BakabaseDbContext>();
        var row = await db.DataSyncEntities.SingleAsync(e => e.Kind == Kind);
        row.LocalHash = form.LocalHash;
        row.RawHash = (await _kind.ReadRawHashesAsync(default))[key];
        row.SharedHash = form.SharedHash!;
        await db.SaveChangesAsync();
        var recorded = await db.DataSyncEntities.AsNoTracking().SingleAsync(e => e.Kind == Kind);

        using (var lease = await gate.EnterAsync(null, default))
            Assert.AreEqual(0, (await refresher.RefreshAsync(lease, [Kind], false, default)).Changed,
                "the next Refresh finds nothing to do");
        var after = await db.DataSyncEntities.AsNoTracking().SingleAsync(e => e.Kind == Kind);
        Assert.AreEqual((recorded.Seq, recorded.VvJson), (after.Seq, after.VvJson));
        CollectionAssert.AreEquivalent(new[] {".MKV", ".avi"}, (await _groups.Get(video.Id)).Extensions!.ToArray());
    }
}
