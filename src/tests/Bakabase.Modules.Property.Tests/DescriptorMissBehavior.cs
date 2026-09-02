using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// When a stored DbValue references an option that no longer exists, every read path
/// must behave the same way: drop the entry instead of leaking the raw UUID to the UI.
/// </summary>
[TestClass]
public sealed class DescriptorMissBehavior
{
    private static Bakabase.Abstractions.Models.Domain.Property MultiChoiceProperty() => new(
        PropertyPool.Custom, 1, PropertyType.MultipleChoice, "m",
        new MultipleChoicePropertyOptions
        {
            Choices = [new() { Label = "A", Value = "uuid-a" }]
        });

    [TestMethod]
    public void MultipleChoice_BizValue_DropsEntriesWithDeletedChoices()
    {
        var biz = PropertySystem.Property.ToBizValue(
            MultiChoiceProperty(), new List<string> { "uuid-a", "uuid-deleted" }) as List<string>;
        Assert.IsNotNull(biz);
        CollectionAssert.AreEqual(new List<string> { "A" }, biz);
    }

    [TestMethod]
    public void MultipleChoice_BizValue_AllChoicesDeleted_ReturnsNull()
    {
        var biz = PropertySystem.Property.ToBizValue(
            MultiChoiceProperty(), new List<string> { "uuid-deleted" });
        Assert.IsNull(biz);
    }

    [TestMethod]
    public void SingleChoice_BizValue_DeletedChoice_ReturnsNull()
    {
        var property = new Bakabase.Abstractions.Models.Domain.Property(
            PropertyPool.Custom, 2, PropertyType.SingleChoice, "s",
            new SingleChoicePropertyOptions
            {
                Choices = [new() { Label = "A", Value = "uuid-a" }]
            });
        Assert.IsNull(PropertySystem.Property.ToBizValue(property, "uuid-deleted"));
    }
}
