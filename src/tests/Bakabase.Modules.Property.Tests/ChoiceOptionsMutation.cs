using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for ChoicePropertyExtensions.AddChoices: appending new
/// choices, the ignore-same-value de-duplication, supplied vs. generated ids,
/// and the values/dbValues length-mismatch guard.
/// </summary>
[TestClass]
public sealed class ChoiceOptionsMutation
{
    [TestMethod]
    public void AddChoices_NewLabels_AppendsAndReturnsTrue()
    {
        var options = new SingleChoicePropertyOptions();
        var changed = options.AddChoices(false, ["A", "B"], null);
        Assert.IsTrue(changed);
        Assert.AreEqual(2, options.Choices!.Count);
        CollectionAssert.AreEquivalent(
            new List<string> { "A", "B" }, options.Choices.Select(c => c.Label).ToList());
    }

    [TestMethod]
    public void AddChoices_EmptyArray_ReturnsFalse()
    {
        var options = new SingleChoicePropertyOptions();
        Assert.IsFalse(options.AddChoices(false, [], null));
    }

    [TestMethod]
    public void AddChoices_AllWhitespaceLabels_ReturnsFalse()
    {
        var options = new SingleChoicePropertyOptions();
        Assert.IsFalse(options.AddChoices(false, ["", "   "], null));
    }

    [TestMethod]
    public void AddChoices_IgnoreSameValue_SkipsExistingLabels()
    {
        var options = new SingleChoicePropertyOptions
        {
            Choices = [new ChoiceOptions { Label = "A", Value = "v-a" }]
        };
        var changed = options.AddChoices(true, ["A", "B"], null);
        Assert.IsTrue(changed);
        Assert.AreEqual(2, options.Choices!.Count); // A kept, only B added
    }

    [TestMethod]
    public void AddChoices_IgnoreSameValue_AllExisting_ReturnsFalse()
    {
        var options = new SingleChoicePropertyOptions
        {
            Choices =
            [
                new ChoiceOptions { Label = "A", Value = "v-a" },
                new ChoiceOptions { Label = "B", Value = "v-b" }
            ]
        };
        var changed = options.AddChoices(true, ["A", "B"], null);
        Assert.IsFalse(changed);
        Assert.AreEqual(2, options.Choices!.Count);
    }

    [TestMethod]
    public void AddChoices_WithDbValues_UsesProvidedIdsInOrder()
    {
        var options = new SingleChoicePropertyOptions();
        options.AddChoices(false, ["A", "B"], ["id-1", "id-2"]);
        Assert.AreEqual("id-1", options.Choices!.First(c => c.Label == "A").Value);
        Assert.AreEqual("id-2", options.Choices.First(c => c.Label == "B").Value);
    }

    [TestMethod]
    public void AddChoices_EmptyDbValue_FallsBackToGeneratedId()
    {
        var options = new SingleChoicePropertyOptions();
        options.AddChoices(false, ["A"], [""]);
        Assert.IsFalse(string.IsNullOrEmpty(options.Choices!.Single().Value));
    }

    [TestMethod]
    public void AddChoices_DbValuesLengthMismatch_Throws()
    {
        Assert.ThrowsException<Exception>(
            () => { _ = new SingleChoicePropertyOptions().AddChoices(false, ["A", "B"], ["only-one"]); });
    }
}
