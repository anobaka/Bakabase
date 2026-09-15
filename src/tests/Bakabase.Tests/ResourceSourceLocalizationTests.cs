using System;
using System.Globalization;
using System.Resources;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceSourceLocalizationTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("zh-Hans")]
    public void EveryFilterSourceHasALabelInEachSupportedLanguage(string cultureName)
    {
        var resources = new ResourceManager("Bakabase.Modules.Property.Resources.PropertyResource",
            typeof(IPropertyLocalizer).Assembly);
        var resourceSet = resources.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), true, false);
        Assert.IsNotNull(resourceSet, $"Missing resource set: {cultureName}");

        foreach (var source in Enum.GetValues<ResourceSource>())
        {
            var key = $"ResourceSourceName_{source}";
            var label = resourceSet.GetString(key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(label), $"Missing source label: {cultureName}/{key}");
            Assert.AreNotEqual(key, label, "The filter must not display a translation key.");
        }
    }
}
