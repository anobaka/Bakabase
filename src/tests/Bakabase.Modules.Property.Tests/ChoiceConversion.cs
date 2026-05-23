using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for SingleChoice / MultipleChoice DbValue &lt;-&gt; BizValue
/// conversion. These reference types store option UUIDs in the database and
/// display labels, so matching label &lt;-&gt; UUID against the option set — and
/// what happens when an entry is missing — is the edge that matters.
/// </summary>
[TestClass]
public sealed class ChoiceConversion
{
    private static SingleChoicePropertyOptions SingleOptions(params (string Value, string Label)[] choices) =>
        new() { Choices = choices.Select(c => new ChoiceOptions { Value = c.Value, Label = c.Label }).ToList() };

    private static MultipleChoicePropertyOptions MultiOptions(params (string Value, string Label)[] choices) =>
        new() { Choices = choices.Select(c => new ChoiceOptions { Value = c.Value, Label = c.Label }).ToList() };

    #region SingleChoice — Db (label -> UUID)

    [TestMethod]
    public void SingleChoice_MatchDbValue_KnownLabel_ReturnsUuid()
    {
        var options = SingleOptions(("uuid-1", "Apple"), ("uuid-2", "Banana"));
        Assert.AreEqual("uuid-2", PropertyValueFactory.SingleChoice.MatchDbValue(options, "Banana"));
    }

    [TestMethod]
    public void SingleChoice_MatchDbValue_UnknownLabel_ReturnsNull()
    {
        var options = SingleOptions(("uuid-1", "Apple"));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchDbValue(options, "Cherry"));
    }

    [TestMethod]
    public void SingleChoice_MatchDbValue_NullOrEmptyLabel_ReturnsNull()
    {
        var options = SingleOptions(("uuid-1", "Apple"));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchDbValue(options, null));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchDbValue(options, ""));
    }

    [TestMethod]
    public void SingleChoice_MatchDbValue_NullOptions_ReturnsNull()
    {
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchDbValue(null, "Apple"));
    }

    [TestMethod]
    public void SingleChoice_MatchDbValue_AddOnMiss_CreatesChoiceAndReturnsItsUuid()
    {
        var options = SingleOptions(("uuid-1", "Apple"));
        var dbValue = PropertyValueFactory.SingleChoice.MatchDbValue(options, "Cherry", addOnMiss: true);
        Assert.IsNotNull(dbValue);
        Assert.AreEqual(2, options.Choices!.Count);
        Assert.AreEqual("Cherry", PropertyValueFactory.SingleChoice.MatchBizValue(options, dbValue));
    }

    [TestMethod]
    public void SingleChoice_MatchDbValue_AddOnMiss_ExistingLabel_DoesNotDuplicate()
    {
        var options = SingleOptions(("uuid-1", "Apple"));
        var dbValue = PropertyValueFactory.SingleChoice.MatchDbValue(options, "Apple", addOnMiss: true);
        Assert.AreEqual("uuid-1", dbValue);
        Assert.AreEqual(1, options.Choices!.Count);
    }

    #endregion

    #region SingleChoice — Biz (UUID -> label)

    [TestMethod]
    public void SingleChoice_MatchBizValue_KnownUuid_ReturnsLabel()
    {
        var options = SingleOptions(("uuid-1", "Apple"), ("uuid-2", "Banana"));
        Assert.AreEqual("Apple", PropertyValueFactory.SingleChoice.MatchBizValue(options, "uuid-1"));
    }

    [TestMethod]
    public void SingleChoice_MatchBizValue_OrphanedUuid_ReturnsNull()
    {
        // A DbValue whose option has been deleted no longer resolves to a label.
        var options = SingleOptions(("uuid-1", "Apple"));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchBizValue(options, "uuid-deleted"));
    }

    [TestMethod]
    public void SingleChoice_MatchBizValue_NullOrEmpty_ReturnsNull()
    {
        var options = SingleOptions(("uuid-1", "Apple"));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchBizValue(options, null));
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchBizValue(options, ""));
    }

    [TestMethod]
    public void SingleChoice_RoundTrip_LabelToDbToBiz()
    {
        var options = SingleOptions(("uuid-1", "Apple"), ("uuid-2", "Banana"));
        var db = PropertyValueFactory.SingleChoice.MatchDbValue(options, "Banana");
        var biz = PropertyValueFactory.SingleChoice.MatchBizValue(options, db);
        Assert.AreEqual("Banana", biz);
    }

    #endregion

    #region MultipleChoice

    [TestMethod]
    public void MultipleChoice_MatchDbValue_KnownLabels_ReturnUuidsInOrder()
    {
        var options = MultiOptions(("u1", "A"), ("u2", "B"), ("u3", "C"));
        var db = PropertyValueFactory.MultipleChoice.MatchDbValue(options, ["C", "A"]);
        CollectionAssert.AreEqual(new List<string> { "u3", "u1" }, db);
    }

    [TestMethod]
    public void MultipleChoice_MatchDbValue_DropsUnknownLabels()
    {
        var options = MultiOptions(("u1", "A"), ("u2", "B"));
        var db = PropertyValueFactory.MultipleChoice.MatchDbValue(options, ["A", "Unknown", "B"]);
        CollectionAssert.AreEqual(new List<string> { "u1", "u2" }, db);
    }

    [TestMethod]
    public void MultipleChoice_MatchDbValue_AllUnknown_ReturnsNull()
    {
        var options = MultiOptions(("u1", "A"));
        Assert.IsNull(PropertyValueFactory.MultipleChoice.MatchDbValue(options, ["X", "Y"]));
    }

    [TestMethod]
    public void MultipleChoice_MatchDbValue_NullOrEmptyLabels_ReturnsNull()
    {
        var options = MultiOptions(("u1", "A"));
        Assert.IsNull(PropertyValueFactory.MultipleChoice.MatchDbValue(options, null));
        Assert.IsNull(PropertyValueFactory.MultipleChoice.MatchDbValue(options, new List<string>()));
    }

    [TestMethod]
    public void MultipleChoice_MatchBizValue_DropsOrphanedUuids()
    {
        var options = MultiOptions(("u1", "A"), ("u2", "B"));
        var biz = PropertyValueFactory.MultipleChoice.MatchBizValue(options, ["u1", "u-deleted", "u2"]);
        CollectionAssert.AreEqual(new List<string> { "A", "B" }, biz);
    }

    [TestMethod]
    public void MultipleChoice_MatchDbValue_AddOnMiss_CreatesOnlyMissingChoices()
    {
        var options = MultiOptions(("u1", "A"));
        var db = PropertyValueFactory.MultipleChoice.MatchDbValue(options, ["A", "B", "C"], addOnMiss: true);
        Assert.IsNotNull(db);
        Assert.AreEqual(3, db!.Count);
        Assert.AreEqual(3, options.Choices!.Count);
    }

    [TestMethod]
    public void MultipleChoice_RoundTrip_LabelsToDbToBiz()
    {
        var options = MultiOptions(("u1", "A"), ("u2", "B"), ("u3", "C"));
        var db = PropertyValueFactory.MultipleChoice.MatchDbValue(options, ["B", "C"]);
        var biz = PropertyValueFactory.MultipleChoice.MatchBizValue(options, db);
        CollectionAssert.AreEqual(new List<string> { "B", "C" }, biz);
    }

    #endregion
}
