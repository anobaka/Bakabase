using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Kinds;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncRefreshFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The lost-update guard (spec §6.5): a whole-row write that undoes what a recent apply wrote holds the entity
/// (PublishHeld, a SuspectedLostUpdate item of no link, HeldAtSource at readers, incoming merges frozen) instead of
/// publishing the stale overwrite. A later local edit updates the item; Publish releases it as a local revision;
/// outside the 10-minute window, after an undo, or after a newer apply that left the entity alone, a revert is an
/// ordinary revision.
/// </summary>
[TestClass]
public class LostUpdateGuardTests
{
    /// <summary>The content before the apply: Genre, a:Horror, b:Drama, Asia/Europe with Japan under Asia, precision 1.</summary>
    private static async Task<DataSyncRefreshFixture> BeforeTheApplyAsync()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        var genre = f.Kind.Add("1", "Genre", ("a", "Horror"), ("b", "Drama"), ("asia", "Asia"), ("eu", "Europe"));
        genre.Children.Add(new MemoryChild("jp", "Japan", "asia"));
        genre.Extra["settings"] = new JsonObject {["precision"] = 1};
        await f.RefreshAsync();
        return f;
    }

    /// <summary>
    /// What a sync apply wrote (rename a, add c, remove b, move Japan under Europe, rename the entity, precision 2),
    /// recorded as the runner does it: the content, the re-read hashes and the log's change list.
    /// </summary>
    private static async Task<DataSyncRefreshFixture> AppliedAsync(DataSyncHistoryKind kind = DataSyncHistoryKind.AutoSync)
    {
        var f = await BeforeTheApplyAsync();
        var genre = f.Kind.Definitions["1"];
        genre.Name = "Genres";
        genre.Children = [new("a", "Horror films"), new("c", "Mystery"), new("asia", "Asia"), new("eu", "Europe"), new("jp", "Japan", "eu")];
        genre.Extra["settings"] = new JsonObject {["precision"] = 2};
        await f.RefreshAsync();
        await f.LogApplyAsync(kind, Changes());
        return f;
    }

    private static DataSyncEntityChanges Changes(string localKey = "1") => new(DataSyncKindIds.CustomProperty, localKey,
        [new DataSyncScalarChange("name", Json("Genre"), Json("Genres")), new DataSyncScalarChange("settings.precision", Json(1), Json(2))],
        [
            new DataSyncChildChange("choice:pa", Child("a", "Horror"), Child("a", "Horror films")),
            new DataSyncChildChange("choice:pc", null, Child("c", "Mystery")),
            new DataSyncChildChange("choice:pb", Child("b", "Drama"), null, new JsonObject {["id"] = "b", ["label"] = "Drama"}),
            new DataSyncChildChange("node:pjp:parent", Child("jp", "Japan", "asia"), Child("jp", "Japan", "eu")),
        ]);

    [TestMethod]
    public async Task A_whole_row_write_that_undoes_a_synced_rename_holds_the_entity()
    {
        var f = await AppliedAsync();
        var applied = await f.RowAsync("1");

        // A writer that read the definition before the apply writes its stale copy of one option back.
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        var result = await f.RefreshAsync(collectPublished: true);

        Assert.AreEqual(0, result.Changed, "no revision: the stale overwrite is not published");
        var held = await f.RowAsync("1");
        Assert.IsTrue(held.PublishHeld);
        Assert.IsTrue(held.Seq > applied.Seq, "readers receive it as HeldAtSource = PendingDecision");
        Assert.AreEqual(applied.VvJson, held.VvJson);
        Assert.AreEqual(applied.SharedHash, held.SharedHash);
        Assert.AreEqual(DataSyncHeldReason.PendingDecision, result.Published![(f.KindId, "1")].Held);

        var item = (await f.ItemsAsync()).Single();
        Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, item.Type);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);
        Assert.IsNull(item.LinkId, "it belongs to no link (§9.1 J)");
        Assert.IsNull(item.PeerNodeId);
        Assert.AreEqual((f.KindId, held.SyncKey, "1", ""), (item.Kind, item.SyncKey, item.LocalKey, item.SubjectPath));
        Assert.AreEqual(held.VvJson, item.LocalVvJson);
        var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "");
        Assert.AreEqual(("Genres", "PC-1"), (payload.EntityName, payload.PeerName));
        var field = payload.Fields.Single();
        Assert.AreEqual(("choice:pa", DataSyncFieldResolution.TookRemote), (field.Path, field.Resolution));
        Assert.AreEqual(("Horror", "Horror", "Horror films"), (field.Base!.Text, field.Local!.Text, field.Remote!.Text));
        Assert.AreEqual(DataSyncInboxTokens.Of(DataSyncInboxItemType.SuspectedLostUpdate, "", payload.Fields), item.Token);

        // Incoming merges of it freeze: the merger sees it held (§8.4 row F).
        var local = await f.Services.GetRequiredService<DataSyncLocalStateReader>().ReadAsync(f.KindId, default);
        Assert.IsTrue(local.Entities.Single().PublishHeld);

        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "held until someone decides");
        Assert.IsTrue((await f.RowAsync("1")).PublishHeld);
    }

    [TestMethod]
    public void Every_undone_change_is_found_by_path_and_by_child_id()
    {
        var codec = new MemoryCodec(DataSyncKindIds.CustomProperty, false);
        var changes = Changes();

        JsonObject Content(Action<MemoryDefinition> edit)
        {
            var definition = new MemoryDefinition("Genres",
                [new("a", "Horror films"), new("c", "Mystery"), new("asia", "Asia"), new("eu", "Europe"), new("jp", "Japan", "eu")])
            {
                Extra = new JsonObject {["settings"] = new JsonObject {["precision"] = 2}},
            };
            edit(definition);
            return definition.ToContent();
        }

        string[] Undone(Action<MemoryDefinition> edit, bool childrenLocal = false) =>
            DataSyncLostUpdateGuard.FindUndone(codec, Content(edit), childrenLocal, changes).Select(u => u.Path).ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), Undone(_ => { }), "as applied");
        CollectionAssert.AreEqual(new[] {"name"}, Undone(d => d.Name = "Genre"), "a scalar back at its before value");
        CollectionAssert.AreEqual(new[] {"settings.precision"},
            Undone(d => d.Extra["settings"] = new JsonObject {["precision"] = 1}), "a nested scalar");
        CollectionAssert.AreEqual(new[] {"choice:pa"}, Undone(d => d.Children[0] = new("a", "Horror")), "renamed back");
        CollectionAssert.AreEqual(new[] {"choice:pc"}, Undone(d => d.Children.RemoveAt(1)), "an added child gone");
        CollectionAssert.AreEqual(new[] {"choice:pc"}, Undone(d => d.Children[1] = new("c2", "Mystery")),
            "by uuid: the same label under another id does not count");
        CollectionAssert.AreEqual(new[] {"choice:pb"}, Undone(d => d.Children.Add(new("b", "Drama"))), "a removed child back");
        CollectionAssert.AreEqual(new[] {"node:pjp:parent"}, Undone(d => d.Children[4] = new("jp", "Japan", "asia")),
            "moved back under its old parent");

        // Anything else is an ordinary edit.
        CollectionAssert.AreEqual(Array.Empty<string>(), Undone(d =>
        {
            d.Name = "Kinds";
            d.Children[0] = new("a", "Horror!");
            d.Children.Add(new("d", "Comedy"));
            d.Children[4] = new("jp", "Japan", "asia2");
        }));

        var shared = new DataSyncEntityChanges(DataSyncKindIds.CustomProperty, "1",
            [new DataSyncScalarChange("childrenLocal", Json(false), Json(true)), new DataSyncScalarChange("type", Json("Tags"), Json("Tags"))], []);
        Assert.AreEqual("childrenLocal", DataSyncLostUpdateGuard.FindUndone(codec, Content(_ => { }), false, shared).Single().Path,
            "childrenLocal is read from the side row; a path whose before equals its after is never undone");
        Assert.AreEqual(0, DataSyncLostUpdateGuard.FindUndone(codec, Content(_ => { }), true, shared).Count);
    }

    [TestMethod]
    public async Task A_later_local_edit_only_updates_the_item()
    {
        var f = await AppliedAsync();
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        f.Kind.Definitions["1"].Children.RemoveAt(1);
        await f.RefreshAsync();
        var held = await f.RowAsync("1");
        var first = (await f.ItemsAsync()).Single();
        Assert.AreEqual(2, DataSyncStoredJson.Read<DataSyncInboxPayload>(first.PayloadJson, "").Fields.Count);

        f.Clock.Advance(TimeSpan.FromMinutes(1));
        f.Kind.Definitions["1"].Children.Insert(1, new MemoryChild("c", "Mystery"));
        var result = await f.RefreshAsync();

        Assert.AreEqual(0, result.Changed);
        var after = await f.RowAsync("1");
        Assert.IsTrue(after.PublishHeld);
        Assert.AreEqual((held.Seq, held.VvJson), (after.Seq, after.VvJson), "a local edit before the decision is no revision");
        var updated = (await f.ItemsAsync()).Single();
        Assert.AreEqual(first.Id, updated.Id);
        Assert.AreEqual(first.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.IsTrue(updated.UpdatedAtUtc > first.UpdatedAtUtc);
        var fields = DataSyncStoredJson.Read<DataSyncInboxPayload>(updated.PayloadJson, "").Fields;
        CollectionAssert.AreEqual(new[] {"choice:pa"}, fields.Select(x => x.Path).ToArray());
        Assert.AreNotEqual(first.Token, updated.Token);
    }

    [TestMethod]
    public async Task Publish_keeps_this_devices_version_as_a_local_revision()
    {
        var f = await AppliedAsync();
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        await f.RefreshAsync();
        var held = await f.RowAsync("1");

        var result = await f.RefreshAsync(options: new DataSyncRefreshOptions([(f.KindId, "1")]));

        Assert.AreEqual(1, result.Changed);
        var published = await f.RowAsync("1");
        Assert.IsFalse(published.PublishHeld);
        Assert.IsTrue(published.Seq > held.Seq);
        Assert.AreEqual(DataSyncVvRelation.DominatedBy, Vv(held.VvJson).CompareTo(Vv(published.VvJson)),
            "a normal local revision of the current content");
        Assert.AreNotEqual(held.SharedHash, published.SharedHash);
        Assert.AreEqual(1, await f.Store.CloseStaleStateItemsAsync([(f.KindId, new SyncKey(published.SyncKey))], null,
            f.Now, default), "its state is gone, so its item closes (§9.3)");
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed);
    }

    [TestMethod]
    public async Task Outside_the_window_a_revert_is_an_ordinary_revision()
    {
        var f = await AppliedAsync();
        f.Clock.Advance(DataSyncLostUpdateGuard.Window + TimeSpan.FromSeconds(1));

        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        var result = await f.RefreshAsync();

        Assert.AreEqual(1, result.Changed);
        Assert.IsFalse((await f.RowAsync("1")).PublishHeld);
        Assert.AreEqual(0, (await f.ItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Undo_is_exempt()
    {
        // The apply was undone: its changes no longer count.
        var f = await AppliedAsync();
        var log = await f.Db.DataSyncApplyLogs.SingleAsync();
        log.UndoneAtUtc = f.Now;
        await f.Db.SaveChangesAsync();
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        Assert.AreEqual(1, (await f.RefreshAsync()).Changed);
        Assert.IsFalse((await f.RowAsync("1")).PublishHeld);

        // Undo's own log is never one the guard reads.
        var g = await AppliedAsync(DataSyncHistoryKind.Undo);
        g.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        Assert.AreEqual(1, (await g.RefreshAsync()).Changed);
        Assert.IsFalse((await g.RowAsync("1")).PublishHeld);
    }

    [TestMethod]
    public async Task A_newer_apply_that_left_the_entity_alone_is_the_one_compared_against()
    {
        var f = await AppliedAsync();
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.LogApplyAsync(DataSyncHistoryKind.Resolution, new DataSyncEntityChanges(f.KindId, "1", [], []));

        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        Assert.AreEqual(1, (await f.RefreshAsync()).Changed, "the entity's most recent apply changed nothing to undo");
        Assert.IsFalse((await f.RowAsync("1")).PublishHeld);
    }

    [TestMethod]
    public async Task A_stale_write_through_a_request_scoped_service_is_held()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(
            configure: s => s.AddExtensionGroupDataSyncKind(new TestExtensionGroupCodec()));
        var scopes = f.Services.GetRequiredService<IServiceScopeFactory>();
        var group = await f.Services.GetRequiredService<IExtensionGroupService>()
            .Add(new ExtensionGroupAddInputModel("Video", [".mkv", ".mp4"]));
        var key = group.Id.ToString();
        await RefreshGroupsAsync(f);

        // A request reads the group before a sync apply commits…
        ExtensionGroup stale;
        await using (var request = scopes.CreateAsyncScope())
            stale = await request.ServiceProvider.GetRequiredService<IExtensionGroupService>().Get(group.Id);

        // …the apply adds .avi through the adapter and records it…
        var adapter = f.Services.GetServices<IDataSyncKind>().Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.ExtensionGroup);
        var current = (await adapter.ReadAsync([key], default)).Single();
        var outcome = await adapter.ApplyAsync(new ApplyBatch(DataSyncKindIds.ExtensionGroup,
        [
            new UpdateEntityOperation("item-1", key, ContentHash.Of(current.Content),
                new JsonObject {["extensions"] = new JsonArray(".avi", ".mkv", ".mp4"), ["name"] = "Video"},
                EntityKeys.None, [".avi"], []),
        ]), default);
        Assert.AreEqual(0, outcome.ChangedDuringApplyItemIds.Count);
        await RefreshGroupsAsync(f);
        await f.LogApplyAsync(DataSyncHistoryKind.AutoSync, new DataSyncEntityChanges(DataSyncKindIds.ExtensionGroup, key, [],
            [new DataSyncChildChange("ext:.avi", null, Child(".avi", ".avi"))]));
        var applied = await f.Db.DataSyncEntities.AsNoTracking().SingleAsync(e => e.Kind == DataSyncKindIds.ExtensionGroup);

        // …and the request writes its stale whole row back afterwards.
        await using (var request = scopes.CreateAsyncScope())
            await request.ServiceProvider.GetRequiredService<IExtensionGroupService>()
                .Put(group.Id, new ExtensionGroupPutInputModel(stale.Name, stale.Extensions!));
        var result = await RefreshGroupsAsync(f);

        Assert.AreEqual(0, result.Changed);
        var held = await f.Db.DataSyncEntities.AsNoTracking().SingleAsync(e => e.Kind == DataSyncKindIds.ExtensionGroup);
        Assert.IsTrue(held.PublishHeld);
        Assert.AreEqual(applied.VvJson, held.VvJson);
        var item = (await f.ItemsAsync()).Single();
        Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, item.Type);
        Assert.AreEqual("ext:.avi", DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "")
            .Fields.Single().Path);
    }

    private static async Task<DataSyncRefreshResult> RefreshGroupsAsync(
        DataSyncRefreshFixture f)
    {
        using var lease = await f.Gate.EnterAsync(null, default);
        return await f.Refresher.RefreshAsync(lease, [DataSyncKindIds.ExtensionGroup], false, default);
    }
}
