using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// PrepareDbValue's options policy: AutoCreateOptions (default, the write path) creates
/// missing options and mutates property.Options; MatchOnly never touches the options and
/// drops unmatched entries.
/// </summary>
[TestClass]
public sealed class PrepareDbValuePolicy
{
    private static Bakabase.Abstractions.Models.Domain.Property ChoiceProperty(
        SingleChoicePropertyOptions? options = null) =>
        new(PropertyPool.Custom, 1, PropertyType.SingleChoice, "c", options);

    [TestMethod]
    public void SingleChoice_AutoCreate_IsDefault_AndCreatesMissingChoice()
    {
        var property = ChoiceProperty(new SingleChoicePropertyOptions());
        var (dbValue, changed) = PropertySystem.Property.ToDbValue(property, "New");
        Assert.IsNotNull(dbValue);
        Assert.IsTrue(changed);
        Assert.AreEqual(1, ((SingleChoicePropertyOptions)property.Options!).Choices!.Count);
    }

    [TestMethod]
    public void SingleChoice_MatchOnly_DoesNotTouchOptions()
    {
        var options = new SingleChoicePropertyOptions
        {
            Choices = [new() { Label = "A", Value = "uuid-a" }]
        };
        var property = ChoiceProperty(options);

        var (matched, changedOnHit) = PropertySystem.Property.ToDbValue(
            property, "A", PropertyValueMatchPolicy.MatchOnly);
        Assert.AreEqual("uuid-a", matched);
        Assert.IsFalse(changedOnHit);

        var (missed, changedOnMiss) = PropertySystem.Property.ToDbValue(
            property, "Missing", PropertyValueMatchPolicy.MatchOnly);
        Assert.IsNull(missed);
        Assert.IsFalse(changedOnMiss);
        Assert.AreEqual(1, options.Choices!.Count);
    }

    [TestMethod]
    public void SingleChoice_MatchOnly_WithoutOptions_ReturnsNullWithoutCreatingOptions()
    {
        var property = ChoiceProperty();
        var (dbValue, changed) = PropertySystem.Property.ToDbValue(
            property, "New", PropertyValueMatchPolicy.MatchOnly);
        Assert.IsNull(dbValue);
        Assert.IsFalse(changed);
        Assert.IsNull(property.Options);
    }

    [TestMethod]
    public void Tags_MatchOnly_DropsUnknownTags()
    {
        var options = new TagsPropertyOptions
        {
            Tags = [new TagsPropertyOptions.TagOptions(null, "Known") { Value = "uuid-1" }]
        };
        var property = new Bakabase.Abstractions.Models.Domain.Property(
            PropertyPool.Custom, 2, PropertyType.Tags, "t", options);

        var (dbValue, changed) = PropertySystem.Property.ToDbValue(property,
            new List<TagValue> { new(null, "Known"), new(null, "Unknown") },
            PropertyValueMatchPolicy.MatchOnly);

        CollectionAssert.AreEqual(new List<string> { "uuid-1" }, (List<string>)dbValue!);
        Assert.IsFalse(changed);
        Assert.AreEqual(1, options.Tags!.Count);
    }

    [TestMethod]
    public void FactoryMatchers_DelegateToDescriptorPolicies()
    {
        var options = new SingleChoicePropertyOptions();
        // addOnMiss: false → MatchOnly → no mutation
        Assert.IsNull(PropertyValueFactory.SingleChoice.MatchDbValue(options, "New"));
        Assert.IsNull(options.Choices);

        // addOnMiss: true → AutoCreateOptions
        var id = PropertyValueFactory.SingleChoice.MatchDbValue(options, "New", addOnMiss: true);
        Assert.IsNotNull(id);
        Assert.AreEqual(1, options.Choices!.Count);
    }
}
