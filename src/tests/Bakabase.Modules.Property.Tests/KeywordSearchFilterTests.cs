using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Coverage for IPropertySearchHandler.BuildSearchFilterByKeyword — the conversion of a
/// free-text keyword into a search filter. Only text-family types support it (as a
/// Contains filter); every other type produces no keyword filter.
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
    public void SingleChoice_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.SingleChoice, "anything"));

    [TestMethod]
    public void Tags_ProducesNoKeywordFilter()
        => Assert.IsNull(KeywordFilter(PropertyType.Tags, "anything"));
}
