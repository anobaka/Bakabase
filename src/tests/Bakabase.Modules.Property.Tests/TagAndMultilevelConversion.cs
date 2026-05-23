using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for the complex reference types Tags and Multilevel.
/// Tags match by group AND name; Multilevel matches a label chain against a
/// tree. Both store option/node UUIDs in the DB and must drop entries that no
/// longer resolve.
/// </summary>
[TestClass]
public sealed class TagAndMultilevelConversion
{
    private static TagsPropertyOptions BuildTags(params (string? Group, string Name, string Value)[] tags) =>
        new()
        {
            Tags = tags.Select(t => new TagsPropertyOptions.TagOptions(t.Group, t.Name) { Value = t.Value }).ToList()
        };

    private static MultilevelDataOptions Node(string value, string label, params MultilevelDataOptions[] children) =>
        new() { Value = value, Label = label, Children = children.Length > 0 ? children.ToList() : null };

    // Asia -> {Japan, China}, Europe -> {France}
    private static MultilevelPropertyOptions BuildTree() =>
        new()
        {
            Data =
            [
                Node("v-asia", "Asia", Node("v-japan", "Japan"), Node("v-china", "China")),
                Node("v-europe", "Europe", Node("v-france", "France"))
            ]
        };

    #region Tags

    [TestMethod]
    public void Tags_MatchDbValue_KnownTags_ReturnUuidsInOrder()
    {
        var options = BuildTags(("Studio", "Ghibli", "u1"), ("Genre", "Fantasy", "u2"));
        var db = PropertyValueFactory.Tags.MatchDbValue(options,
            new List<TagValue> { new("Genre", "Fantasy"), new("Studio", "Ghibli") });
        CollectionAssert.AreEqual(new List<string> { "u2", "u1" }, db);
    }

    [TestMethod]
    public void Tags_MatchDbValue_GroupDisambiguatesSameName()
    {
        // Same name under different groups must resolve to different options.
        var options = BuildTags((null, "Action", "u-noGroup"), ("Genre", "Action", "u-genre"));
        CollectionAssert.AreEqual(new List<string> { "u-genre" },
            PropertyValueFactory.Tags.MatchDbValue(options, new List<TagValue> { new("Genre", "Action") }));
        CollectionAssert.AreEqual(new List<string> { "u-noGroup" },
            PropertyValueFactory.Tags.MatchDbValue(options, new List<TagValue> { new(null, "Action") }));
    }

    [TestMethod]
    public void Tags_MatchDbValue_UnknownTag_IsDropped()
    {
        var options = BuildTags(("G", "Known", "u1"));
        var db = PropertyValueFactory.Tags.MatchDbValue(options,
            new List<TagValue> { new("G", "Known"), new("G", "Missing") });
        CollectionAssert.AreEqual(new List<string> { "u1" }, db);
    }

    [TestMethod]
    public void Tags_MatchDbValue_NullEmptyOrNamelessTags_ReturnNull()
    {
        var options = BuildTags(("G", "N", "u1"));
        Assert.IsNull(PropertyValueFactory.Tags.MatchDbValue(options, null));
        Assert.IsNull(PropertyValueFactory.Tags.MatchDbValue(options, new List<TagValue>()));
        Assert.IsNull(PropertyValueFactory.Tags.MatchDbValue(options, new List<TagValue> { new("G", "") }));
    }

    [TestMethod]
    public void Tags_MatchDbValue_AddOnMiss_CreatesMissingTags()
    {
        var options = BuildTags(("G", "Existing", "u1"));
        var db = PropertyValueFactory.Tags.MatchDbValue(options,
            new List<TagValue> { new("G", "Existing"), new("G", "New") }, addOnMiss: true);
        Assert.IsNotNull(db);
        Assert.AreEqual(2, db!.Count);
        Assert.AreEqual(2, options.Tags!.Count);
    }

    [TestMethod]
    public void Tags_MatchBizValue_KnownUuids_ReturnTagValues()
    {
        var options = BuildTags(("Studio", "Ghibli", "u1"), (null, "Classic", "u2"));
        var biz = PropertyValueFactory.Tags.MatchBizValue(options, new List<string> { "u2", "u1" });
        Assert.IsNotNull(biz);
        Assert.AreEqual(2, biz!.Count);
        Assert.IsNull(biz[0].Group);
        Assert.AreEqual("Classic", biz[0].Name);
        Assert.AreEqual("Studio", biz[1].Group);
        Assert.AreEqual("Ghibli", biz[1].Name);
    }

    [TestMethod]
    public void Tags_MatchBizValue_OrphanedUuid_IsDropped()
    {
        var options = BuildTags(("G", "N", "u1"));
        var biz = PropertyValueFactory.Tags.MatchBizValue(options, new List<string> { "u1", "u-deleted" });
        Assert.IsNotNull(biz);
        Assert.AreEqual(1, biz!.Count);
        Assert.AreEqual("N", biz[0].Name);
    }

    [TestMethod]
    public void Tags_RoundTrip_TagToDbToBiz()
    {
        var options = BuildTags(("Studio", "Ghibli", "u1"), ("Genre", "Fantasy", "u2"));
        var db = PropertyValueFactory.Tags.MatchDbValue(options, new List<TagValue> { new("Genre", "Fantasy") });
        var biz = PropertyValueFactory.Tags.MatchBizValue(options, db);
        Assert.IsNotNull(biz);
        Assert.AreEqual(1, biz!.Count);
        Assert.AreEqual("Genre", biz[0].Group);
        Assert.AreEqual("Fantasy", biz[0].Name);
    }

    #endregion

    #region Multilevel

    [TestMethod]
    public void Multilevel_MatchDbValue_KnownLeafPath_ReturnsNodeValue()
    {
        var db = PropertyValueFactory.Multilevel.MatchDbValue(BuildTree(), [["Asia", "Japan"]]);
        CollectionAssert.AreEqual(new List<string> { "v-japan" }, db);
    }

    [TestMethod]
    public void Multilevel_MatchDbValue_IntermediatePath_ReturnsNodeValue()
    {
        // A path may address a non-leaf node.
        var db = PropertyValueFactory.Multilevel.MatchDbValue(BuildTree(), [["Asia"]]);
        CollectionAssert.AreEqual(new List<string> { "v-asia" }, db);
    }

    [TestMethod]
    public void Multilevel_MatchDbValue_UnknownPath_ReturnsNull()
    {
        var db = PropertyValueFactory.Multilevel.MatchDbValue(BuildTree(), [["Asia", "Korea"]]);
        Assert.IsNull(db);
    }

    [TestMethod]
    public void Multilevel_MatchDbValue_NullOrEmptyPaths_ReturnNull()
    {
        var options = BuildTree();
        Assert.IsNull(PropertyValueFactory.Multilevel.MatchDbValue(options, null));
        Assert.IsNull(PropertyValueFactory.Multilevel.MatchDbValue(options, new List<List<string>>()));
        Assert.IsNull(PropertyValueFactory.Multilevel.MatchDbValue(options, [new List<string>()]));
    }

    [TestMethod]
    public void Multilevel_MatchDbValue_NullData_ReturnsNull()
    {
        var emptyOptions = new MultilevelPropertyOptions();
        Assert.IsNull(PropertyValueFactory.Multilevel.MatchDbValue(emptyOptions, [["Asia"]]));
    }

    [TestMethod]
    public void Multilevel_MatchDbValue_AddOnMiss_CreatesBranch()
    {
        var options = BuildTree();
        var db = PropertyValueFactory.Multilevel.MatchDbValue(options, [["Asia", "Korea"]], addOnMiss: true);
        Assert.IsNotNull(db);
        Assert.AreEqual(1, db!.Count);
        var biz = PropertyValueFactory.Multilevel.MatchBizValue(options, db);
        Assert.IsNotNull(biz);
        CollectionAssert.AreEqual(new List<string> { "Asia", "Korea" }, biz![0]);
    }

    [TestMethod]
    public void Multilevel_MatchBizValue_KnownValue_ReturnsLabelChain()
    {
        var biz = PropertyValueFactory.Multilevel.MatchBizValue(BuildTree(), new List<string> { "v-france" });
        Assert.IsNotNull(biz);
        Assert.AreEqual(1, biz!.Count);
        CollectionAssert.AreEqual(new List<string> { "Europe", "France" }, biz[0]);
    }

    [TestMethod]
    public void Multilevel_MatchBizValue_UnknownValue_ReturnsNull()
    {
        Assert.IsNull(PropertyValueFactory.Multilevel.MatchBizValue(BuildTree(), new List<string> { "v-unknown" }));
    }

    [TestMethod]
    public void Multilevel_RoundTrip_PathToDbToBiz()
    {
        var options = BuildTree();
        var db = PropertyValueFactory.Multilevel.MatchDbValue(options, [["Asia", "China"]]);
        var biz = PropertyValueFactory.Multilevel.MatchBizValue(options, db);
        Assert.IsNotNull(biz);
        CollectionAssert.AreEqual(new List<string> { "Asia", "China" }, biz![0]);
    }

    #endregion
}
