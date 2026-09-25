using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Planning.PlanFixture;

namespace Bakabase.Modules.DataSync.Tests.Planning;

/// <summary>§2.3: per kind in apply order, subtype changes alone, then creates, updates and binds, deletes.</summary>
[TestClass]
public class BuildBatchesTests
{
    [TestMethod]
    public void KindsFollowTheGivenOrderAndOperationsTheirPhase()
    {
        var resolved = new ResolveResult(
        [
            Op("b", Update("u1")), Op("a", Delete("d1")), Op("a", Create("c1")), Op("b", Create("c2")),
            Op("a", Bind("k1")), Op("a", Subtype("s1")), Op("a", Update("u2")), Op("a", Create("c3")),
            Op("z", Create("c4")), Op("y", Bind("k2")), Op("a", Subtype("s2")), Op("a", Delete("d2")),
            None("a", "skipped"),
        ], []);

        var batches = DataSyncPlanner.BuildBatches(resolved, ["b", "a", "unused"]);

        CollectionAssert.AreEqual(new[] { "b", "a", "a", "a", "y", "z" }, batches.Select(b => b.Kind).ToArray(),
            "the given order, then any other kind ordinally; kinds without operations have no batch");
        CollectionAssert.AreEqual(new[] { "c2", "u1" }, Ids(batches[0]));
        CollectionAssert.AreEqual(new[] { "s1" }, Ids(batches[1]), "a subtype change is alone in its batch");
        CollectionAssert.AreEqual(new[] { "s2" }, Ids(batches[2]));
        CollectionAssert.AreEqual(new[] { "c1", "c3", "k1", "u2", "d1", "d2" }, Ids(batches[3]),
            "creates, then updates and binds, then deletes, each in resolved order");
    }

    [TestMethod]
    public void ReviewBatchesCreateInIncomingPositionOrderBeforeUpdates()
    {
        var f = new PlanFixture();
        f.Local("12", 5, T("Genre"));
        f.Local("3", 6, G("Images", ".jpg"), GroupKind);
        f.Pull(9, T("Zeta"), orderKey: "a3");
        f.Pull(5, T("Genres"), orderKey: "a2");
        f.Pull(7, T("Alpha"), orderKey: "a1");
        f.Pull(6, G("Images", ".jpg", ".png"), GroupKind);
        f.Pull(8, G("Videos", ".mp4"), GroupKind);
        var plan = f.Plan();
        var resolved = f.Resolve(plan, [], strict: true);

        var batches = DataSyncPlanner.BuildBatches(resolved, plan.Kinds.Select(k => k.Kind).ToList());
        CollectionAssert.AreEqual(new[] { GroupKind, ItemKind }, batches.Select(b => b.Kind).ToArray());
        CollectionAssert.AreEqual(new[] { Id(8, GroupKind), Id(6, GroupKind) }, Ids(batches[0]));
        CollectionAssert.AreEqual(new[] { Id(7), Id(9), Id(5) }, Ids(batches[1]), "creates by position, then the update");
        Assert.IsInstanceOfType<UpdateEntityOperation>(batches[1].Operations[2]);
    }

    private static string Id(int key, string kind = ItemKind) => $"{kind}/k/{Hex(key)}";

    private static string[] Ids(ApplyBatch batch) => batch.Operations.Select(o => o.ItemId).ToArray();

    private static ResolvedItem Op(string kind, ApplyOperation op) =>
        new(op.ItemId, kind, DataSyncItemOutcome.Applied, DataSyncItemAction.None, op);

    private static ResolvedItem None(string kind, string id) =>
        new(id, kind, DataSyncItemOutcome.SkippedByUser, DataSyncItemAction.None, null);

    private static CreateEntityOperation Create(string id) => new(id, EntityKeys.None, "node", 0, new JsonObject());

    private static UpdateEntityOperation Update(string id) =>
        new(id, "1", "sha256:x", new JsonObject(), EntityKeys.None, [], []);

    private static BindOnlyOperation Bind(string id) => new(id, "1", EntityKeys.None);
    private static DeleteEntityOperation Delete(string id) => new(id, "1", "sha256:x");
    private static ChangeSubtypeOperation Subtype(string id) => new(id, "1", "sha256:x", "Tags");
}
