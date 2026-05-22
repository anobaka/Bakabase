using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;
using Bakabase.Modules.Property.Components.Properties.Number;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for the RatingMax10 enhancement converter, which rescales
/// an incoming 0-10 rating onto the target Rating property's configured max.
/// </summary>
[TestClass]
public sealed class RatingConverter
{
    private static readonly RatingMax10 Converter = new();

    private static DomainProperty RatingProperty(object? options) =>
        new(PropertyPool.Custom, 1, PropertyType.Rating, "Rating", options, 0);

    [TestMethod]
    public void Convert_RescalesToConfiguredMax()
    {
        var result = (decimal?)Converter.Convert(8m, RatingProperty(new RatingPropertyOptions { MaxValue = 5 }));
        Assert.AreEqual(4m, result); // 8 / 10 * 5
    }

    [TestMethod]
    public void Convert_TargetMaxTen_LeavesValueUnchanged()
    {
        var result = (decimal?)Converter.Convert(7m, RatingProperty(new RatingPropertyOptions { MaxValue = 10 }));
        Assert.AreEqual(7m, result);
    }

    [TestMethod]
    public void Convert_NullOptions_UsesDefaultMax()
    {
        // RatingPropertyOptions.DefaultMaxValue is 5.
        var result = (decimal?)Converter.Convert(8m, RatingProperty(null));
        Assert.AreEqual(4m, result);
    }

    [TestMethod]
    public void Convert_RescalesToMaxThree()
    {
        var result = (decimal?)Converter.Convert(10m, RatingProperty(new RatingPropertyOptions { MaxValue = 3 }));
        Assert.AreEqual(3m, result);
    }

    [TestMethod]
    public void Convert_Zero_StaysZero()
    {
        var result = (decimal?)Converter.Convert(0m, RatingProperty(new RatingPropertyOptions { MaxValue = 5 }));
        Assert.AreEqual(0m, result);
    }

    [TestMethod]
    public void Convert_NullRawValue_ReturnsNull()
        => Assert.IsNull(Converter.Convert(null, RatingProperty(new RatingPropertyOptions())));

    [TestMethod]
    public void Convert_NonDecimalRawValue_ReturnsNull()
        => Assert.IsNull(Converter.Convert("not a number", RatingProperty(new RatingPropertyOptions())));
}
