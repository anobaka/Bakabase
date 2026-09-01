using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Tags;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Coverage for IPropertySearchHandler.BuildSearchFilterByKeyword — the conversion of a
/// free-text keyword into a search filter. Text-family types build a Contains filter;
/// choice/tag types match their option labels (case-insensitively) into an In filter;
/// everything else produces no keyword filter.
/// </summary>
[TestClass]
public sealed class KeywordSearchFilterTests
{
    private static ResourceSearchFilter? KeywordFilter(PropertyType type, string keyword)
    {
        var property = new DomainProperty(PropertyPool.Custom, 1, type, "Test");
        return PropertySystem.Property.TryGetSearchHandler(type)!.BuildSearchFilterByKeyword(property, keyword);
    }

    [TestMethod]
    public void SingleLineText_BuildsContainsFilter()
    {
        var filter = KeywordFilter(PropertyType.SingleLineText, "hello");
        Assert.IsNotNull(filter);
        Assert.AreEqual(SearchOperation.Contains, filter!.Operation);
        Assert.AreEqual("hello", filter.DbValue);
    }

    [TestMethod]
    public void MultilineText_BuildsContainsFilter()
    {
        var filter = KeywordFilter(PropertyType.MultilineText, "world");
        Assert.IsNotNull(filter);
        Assert.AreEqual(SearchOperation.Contains, filter!.Operation);
    }

    [TestMethod]
    public void Link_BuildsContainsFilter()
    {
        var filter = KeywordFilter(PropertyType.Link, "example");
        Assert.IsNotNull(filter);
        Assert.AreEqual(SearchOperation.Contains, filter!.Operation);
    }

    [TestMethod]
    public void Number_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.Number, "5"));

    [TestMethod]
    public void Boolean_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.Boolean, "true"));

    [TestMethod]
    public void DateTime_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.DateTime, "2024"));

    [TestMethod]
    public void SingleChoice_WithoutOptions_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.SingleChoice, "anything"));

    [TestMethod]
    public void Tags_WithoutOptions_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.Tags, "anything"));

    [TestMethod]
    public void Filter_PreservesPropertyPool()
    {
        // The built filter must target the property's own pool, not a hardcoded one.
        var property = new DomainProperty(PropertyPool.Reserved,
            (int)Bakabase.InsideWorld.Models.Constants.ResourceProperty.Introduction,
            PropertyType.MultilineText, "Introduction");
        var filter = PropertySystem.Property.TryGetSearchHandler(PropertyType.MultilineText)!
            .BuildSearchFilterByKeyword(property, "hello");
        Assert.IsNotNull(filter);
        Assert.AreEqual(PropertyPool.Reserved, filter!.PropertyPool);
        Assert.AreEqual(property.Id, filter.PropertyId);
    }

    [TestMethod]
    public void SingleChoice_MatchesLabelsCaseInsensitively()
    {
        var property = new DomainProperty(PropertyPool.Custom, 1, PropertyType.SingleChoice, "c",
            new SingleChoicePropertyOptions
            {
                Choices = [new() { Label = "Anime", Value = "uuid-1" }]
            });
        var filter = PropertySystem.Property.TryGetSearchHandler(PropertyType.SingleChoice)!
            .BuildSearchFilterByKeyword(property, "anime");
        Assert.IsNotNull(filter);
        Assert.AreEqual(SearchOperation.In, filter!.Operation);
        CollectionAssert.AreEqual(new List<string> { "uuid-1" }, (List<string>)filter.DbValue!);
    }

    [TestMethod]
    public void Tags_MatchesGroupAndNameCaseInsensitively()
    {
        var property = new DomainProperty(PropertyPool.Custom, 1, PropertyType.Tags, "t",
            new TagsPropertyOptions
            {
                Tags =
                [
                    new TagsPropertyOptions.TagOptions("Genre", "Comedy") { Value = "uuid-1" },
                    new TagsPropertyOptions.TagOptions(null, "Drama") { Value = "uuid-2" }
                ]
            });
        var filter = PropertySystem.Property.TryGetSearchHandler(PropertyType.Tags)!
            .BuildSearchFilterByKeyword(property, "GENRE");
        Assert.IsNotNull(filter);
        var values = (HashSet<string>)filter!.DbValue!;
        CollectionAssert.AreEquivalent(new List<string> { "uuid-1" }, values.ToList());
    }
}
