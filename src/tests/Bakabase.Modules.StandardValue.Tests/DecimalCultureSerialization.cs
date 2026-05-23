using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Decimal standard values are serialized with InvariantCulture so the stored format is
/// culture-independent (and matches the frontend, which parses with `.`). Deserialization
/// stays backward-compatible: it reads the invariant format first and falls back to the
/// machine culture for legacy data written before this change.
/// </summary>
[TestClass]
public sealed class DecimalCultureSerialization
{
    private static void WithCulture(string culture, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Serialize_UsesInvariantFormat_EvenUnderACommaDecimalCulture()
    {
        WithCulture("de-DE", () =>
            Assert.AreEqual("1.5", ((object)1.5m).SerializeAsStandardValue(StandardValueType.Decimal)));
    }

    [TestMethod]
    public void Deserialize_ReadsInvariantFormat()
        => Assert.AreEqual(1.5m, "1.5".DeserializeAsStandardValue(StandardValueType.Decimal));

    [TestMethod]
    public void Deserialize_ReadsLegacyCultureFormat_AsFallback()
    {
        // Legacy data written on a comma-decimal machine ("1,5") must still read back.
        if (new CultureInfo("de-DE").NumberFormat.NumberDecimalSeparator != ",")
        {
            Assert.Inconclusive("Globalization-invariant runtime; comma-decimal culture unavailable.");
            return;
        }

        WithCulture("de-DE", () =>
            Assert.AreEqual(1.5m, "1,5".DeserializeAsStandardValue(StandardValueType.Decimal)));
    }

    [TestMethod]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var serialized = ((object)1234.56m).SerializeAsStandardValue(StandardValueType.Decimal);
        Assert.AreEqual(1234.56m, serialized!.DeserializeAsStandardValue(StandardValueType.Decimal));
    }
}
