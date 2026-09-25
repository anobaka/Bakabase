using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// The engineering critique's cases that need custom property semantics (§13.1 <c>DesignExamplesTests</c>), with
/// package B's codec and the service's normalization (<see cref="CustomPropertySimKind"/>): (a) a node moved on one
/// device converges and stops; (b) a tag group edited <c>null</c> → <c>""</c> makes no revision; (d) a source holding
/// "Action" and "action" keeps both, the receiver holds one, and no deletion is offered; and example D under
/// IgnoreCase.
/// </summary>
public partial class DesignExamplesTests
{
    private const string CpKind = DataSyncKindIds.CustomProperty;

    private static CustomPropertyTagV1 Tag(string id, string? group, string name) => new(id, group, name, null);

    private static CustomPropertyContentV1 CpOf(SimNode node, string name) =>
        (CustomPropertyContentV1)node.Row(name, CpKind).Content!;

    private static string CpForm(SimNode node, string name)
    {
        var row = node.Row(name, CpKind);
        return DataSyncPublication.Of(CustomPropertyCodec.Instance, row.Content!, row.Overlay, row.ChildrenLocal, row.OrderKey,
            row.Unknown).SharedHash!;
    }

    private static void AssertSameCpForm(SimNode a, SimNode b, string name) => Assert.AreEqual(CpForm(a, name),
        CpForm(b, name), $"{a} and {b} differ on {name}: {a.Row(name, CpKind).Shown} / {b.Row(name, CpKind).Shown}");

    /// <summary>Settles, then checks one more round issues nothing anywhere: no revision, no Seq bump.</summary>
    private static void AssertStops(params SimNode[] nodes)
    {
        var before = nodes.Select(n => (n.LastSeq, n.ActorCounter)).ToArray();
        Settle(nodes);
        CollectionAssert.AreEqual(before, nodes.Select(n => (n.LastSeq, n.ActorCounter)).ToArray(), "a ping-pong");
    }

    // ---- (a) ---------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(false, DisplayName = "alone")]
    [DataRow(true, DisplayName = "while the other device renames the property")]
    public void CritiqueA_ANodeMovedOnOneDeviceConvergesAndStops(bool renamedThere)
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(CpKind, Tree("Region", false, N("asia", "Asia", N("jp", "Japan"), N("cn", "China")), N("eu", "Europe")));
        Settle(pc1, pc2);

        pc1.Edit<CustomPropertyContentV1>("Region", CpKind, c => c with
        {
            Nodes = [N("asia", "Asia", N("cn", "China")), N("eu", "Europe", N("jp", "Japan"))],
        });
        if (renamedThere) pc2.Edit<CustomPropertyContentV1>("Region", CpKind, c => c with { Name = "Regions" });
        pc2.Pull(pc1);
        pc1.Pull(pc2);
        Settle(pc1, pc2);

        var name = renamedThere ? "Regions" : "Region";
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions, "a move one device made is no question");
        AssertSameCpForm(pc1, pc2, name);
        Assert.AreEqual("Asia(China),Europe(Japan)", CustomProperties.M3.Shape(CpOf(pc2, name).Nodes));
        Assert.AreEqual("jp", CpOf(pc2, name).Nodes[1].Children.Single().Uuid, "the node moved, keeping its id");
        AssertStops(pc1, pc2);
    }

    // ---- (b) ---------------------------------------------------------------------------------------

    [TestMethod]
    public void CritiqueB_ATagGroupEditedFromNullToEmptyMakesNoRevision()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(CpKind, Tags("Studio", false, Tag("k", null, "Kyoto"), Tag("t", "Region", "Tokyo")));
        Settle(pc1, pc2);
        var vv = pc1.Row("Studio", CpKind).Vv;
        var before = (pc1.LastSeq, pc1.ActorCounter, pc2.LastSeq, pc2.ActorCounter);

        // "" and null are one group (§3.4): the comparison form does not change, so nothing is published.
        pc1.Edit<CustomPropertyContentV1>("Studio", CpKind, c => c with
        {
            Tags = [Tag("k", "", "Kyoto"), Tag("t", "Region", "Tokyo")],
        });
        Settle(pc1, pc2);

        Assert.AreEqual("", CpOf(pc1, "Studio").Tags[0].Group, "the edit is stored");
        Assert.AreEqual(vv, pc1.Row("Studio", CpKind).Vv, "no revision");
        Assert.AreEqual(before, (pc1.LastSeq, pc1.ActorCounter, pc2.LastSeq, pc2.ActorCounter), "nothing to publish");
        Assert.IsNull(CpOf(pc2, "Studio").Tags[0].Group);
        AssertSameCpForm(pc1, pc2, "Studio");

        // And back: still nothing.
        pc1.Edit<CustomPropertyContentV1>("Studio", CpKind, c => c with
        {
            Tags = [Tag("k", null, "Kyoto"), Tag("t", "Region", "Tokyo")],
        });
        Settle(pc1, pc2);
        Assert.AreEqual(vv, pc1.Row("Studio", CpKind).Vv);
    }

    // ---- (d) ---------------------------------------------------------------------------------------

    [TestMethod]
    public void CritiqueD_ASourceHoldingActionAndactionKeepsBothAndTheReceiverHoldsOne()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        // Both stored before IgnoreCase was turned on: the service's Put keeps every stored id (F72).
        pc1.Create(CpKind, Choice("Genre", false, C("a1", "Action"), C("a2", "action"), C("d", "Drama")));
        pc1.Edit<CustomPropertyContentV1>("Genre", CpKind, c => c with { IgnoreCase = true });
        CollectionAssert.AreEqual(new[] { "a1", "a2", "d" }, CpOf(pc1, "Genre").Choices.Select(c => c.Uuid).ToArray());
        Settle(pc1, pc2);

        // The receiver's create folds as a fresh AddRange does: one member of the class, the first.
        CollectionAssert.AreEqual(new[] { "Action", "Drama" }, CpOf(pc2, "Genre").Choices.Select(c => c.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "a1", "a2", "d" }, CpOf(pc1, "Genre").Choices.Select(c => c.Uuid).ToArray(),
            "the source keeps both");
        AssertSameCpForm(pc1, pc2, "Genre");

        // Edits of either side travel, and no deletion of the duplicate is ever offered or made.
        pc2.Edit<CustomPropertyContentV1>("Genre", CpKind, c => c with { Name = "Genres" });
        Settle(pc1, pc2);
        pc1.Edit<CustomPropertyContentV1>("Genres", CpKind, c => c with { Choices = [.. c.Choices, C("h", "Horror")] });
        Settle(pc1, pc2);
        CollectionAssert.AreEqual(new[] { "a1", "a2", "d", "h" }, CpOf(pc1, "Genres").Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { "Action", "Drama", "Horror" },
            CpOf(pc2, "Genres").Choices.Select(c => c.Label).ToArray());
        Assert.AreEqual(0, pc1.Items.Count + pc2.Items.Count, "nothing asked");
        Assert.IsFalse(pc1.Notes.Concat(pc2.Notes).Any(n => n.Code == DataSyncMergeNoteCodes.ChildrenRemoved));
        AssertSameCpForm(pc1, pc2, "Genres");
        AssertStops(pc1, pc2);
    }

    // ---- D under IgnoreCase ------------------------------------------------------------------------

    [TestMethod]
    [DataRow(true, DisplayName = "concurrent")]
    [DataRow(false, DisplayName = "sequential")]
    public void D_BothDevicesAddTheSameOptionUnderIgnoreCase(bool concurrent)
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(CpKind, Choice("Genre", true, C("1", "Drama")));
        Settle(pc1, pc2);

        pc1.Edit<CustomPropertyContentV1>("Genre", CpKind, c => c with { Choices = [.. c.Choices, C("a1", "Action")] });
        if (!concurrent) Settle(pc1, pc2);
        // Sequential, PC-2 already holds "Action": the service folds the new "action" into it (Put, F72).
        pc2.Edit<CustomPropertyContentV1>("Genre", CpKind, c => c with { Choices = [.. c.Choices, C("b1", "action")] });
        pc2.Pull(pc1);
        pc1.Pull(pc2);
        Settle(pc1, pc2);

        // Mapped, not duplicated: one member of the class on each device, no rename, no item, no livelock.
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
        AssertSameCpForm(pc1, pc2, "Genre");
        foreach (var node in new[] { pc1, pc2 })
            Assert.AreEqual(1, CpOf(node, "Genre").Choices.Count(c => DataSyncLabelKey.Fold(c.Label, true) == DataSyncLabelKey.Fold("action", true)));
        Assert.AreEqual("Action", CpOf(pc1, "Genre").Choices[1].Label, "no rename");
        Assert.AreEqual(concurrent ? "action" : "Action", CpOf(pc2, "Genre").Choices[1].Label, "no rename");
        AssertStops(pc1, pc2);
        Assert.AreEqual(DataSyncVvRelation.Equal, pc1.Row("Genre", CpKind).Vv.CompareTo(pc2.Row("Genre", CpKind).Vv));
    }
}
