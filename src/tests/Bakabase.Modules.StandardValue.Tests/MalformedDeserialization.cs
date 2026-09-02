using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// DeserializeAsStandardValue must degrade gracefully on malformed input — returning
/// null rather than throwing — for every standard value type, while still throwing when
/// the caller opts in via throwOnError.
/// </summary>
[TestClass]
public sealed class MalformedDeserialization
{
    [TestMethod]
    public void Decimal_NonNumeric_ReturnsNull()
    {
        Assert.IsNull("abc".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.IsNull("1.2.3".DeserializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void Boolean_Garbage_ReturnsNull()
    {
        Assert.IsNull("yes".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.IsNull("2".DeserializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void Boolean_AcceptsAllHistoricTextualForms()
    {
        // bool.ToString() output (the serializer), lowercase, and the "1"/"0"
        // forms emitted by the conversion/display layer.
        Assert.AreEqual(true, "True".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual(false, "False".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual(true, "true".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual(false, "false".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual(true, "1".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual(false, "0".DeserializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void DateTime_NonNumeric_ReturnsNull()
        => Assert.IsNull("not-a-timestamp".DeserializeAsStandardValue(StandardValueType.DateTime));

    [TestMethod]
    public void Time_NonNumeric_ReturnsNull()
        => Assert.IsNull("not-a-number".DeserializeAsStandardValue(StandardValueType.Time));

    [TestMethod]
    public void Link_SingleSegment_RecoversAsTextOnlyLink()
    {
        // A well-formed Link has two comma-separated fields; a single-segment
        // legacy/corrupted payload must recover as a text-only link instead of
        // silently dropping the whole value.
        var link = "onlytext".DeserializeAsStandardValue(StandardValueType.Link) as LinkValue;
        Assert.IsNotNull(link);
        Assert.AreEqual("onlytext", link.Text);
        Assert.IsNull(link.Url);
    }

    [TestMethod]
    public void ListTag_SingleSegmentEntry_RecoversAsGroupLessTag()
    {
        var tags = "loneName;G,N".DeserializeAsStandardValue(StandardValueType.ListTag) as List<TagValue>;
        Assert.IsNotNull(tags);
        Assert.AreEqual(2, tags.Count);
        Assert.IsNull(tags[0].Group);
        Assert.AreEqual("loneName", tags[0].Name);
        Assert.AreEqual("G", tags[1].Group);
        Assert.AreEqual("N", tags[1].Name);
    }

    [TestMethod]
    public void GenericDeserialize_HonorsThrowOnError()
    {
        Assert.ThrowsException<System.FormatException>(() =>
            "abc".DeserializeAsStandardValue<decimal>(StandardValueType.Decimal, throwOnError: true));
        Assert.AreEqual(default, "abc".DeserializeAsStandardValue<decimal>(StandardValueType.Decimal));
    }

    [TestMethod]
    public void EmptyString_ReturnsNull_ForEveryType()
    {
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.ListString));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.DateTime));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Time));
    }

    [TestMethod]
    public void Malformed_WithThrowOnError_Throws()
        => Assert.ThrowsException<System.FormatException>(() =>
            "abc".DeserializeAsStandardValue(StandardValueType.Decimal, throwOnError: true));

    [TestMethod]
    public void ValidValues_StillDeserializeCorrectly()
    {
        Assert.AreEqual(1.5m, "1.5".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.AreEqual(true, "True".DeserializeAsStandardValue(StandardValueType.Boolean));
    }
}
