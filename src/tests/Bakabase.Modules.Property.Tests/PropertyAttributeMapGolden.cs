using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// PropertyAttributeMap is generated from the descriptors (db/biz value types inferred from
/// their generic arguments, reference-ness declared per descriptor). This golden table locks
/// the generated map against the previously hand-maintained matrix so a descriptor change
/// that would alter storage semantics fails loudly.
/// </summary>
[TestClass]
public sealed class PropertyAttributeMapGolden
{
    private static readonly
        Dictionary<PropertyType, (StandardValueType Db, StandardValueType Biz, bool IsReference)> Expected = new()
        {
            {PropertyType.SingleLineText, (StandardValueType.String, StandardValueType.String, false)},
            {PropertyType.MultilineText, (StandardValueType.String, StandardValueType.String, false)},
            {PropertyType.SingleChoice, (StandardValueType.String, StandardValueType.String, true)},
            {PropertyType.MultipleChoice, (StandardValueType.ListString, StandardValueType.ListString, true)},
            {PropertyType.Number, (StandardValueType.Decimal, StandardValueType.Decimal, false)},
            {PropertyType.Percentage, (StandardValueType.Decimal, StandardValueType.Decimal, false)},
            {PropertyType.Rating, (StandardValueType.Decimal, StandardValueType.Decimal, false)},
            {PropertyType.Boolean, (StandardValueType.Boolean, StandardValueType.Boolean, false)},
            {PropertyType.Link, (StandardValueType.Link, StandardValueType.Link, false)},
            {PropertyType.Attachment, (StandardValueType.ListString, StandardValueType.ListString, false)},
            {PropertyType.Date, (StandardValueType.DateTime, StandardValueType.DateTime, false)},
            {PropertyType.DateTime, (StandardValueType.DateTime, StandardValueType.DateTime, false)},
            {PropertyType.Time, (StandardValueType.Time, StandardValueType.Time, false)},
            {PropertyType.Formula, (StandardValueType.String, StandardValueType.String, false)},
            {PropertyType.Multilevel, (StandardValueType.ListString, StandardValueType.ListListString, true)},
            {PropertyType.Tags, (StandardValueType.ListString, StandardValueType.ListTag, true)},
        };

    [TestMethod]
    public void GeneratedAttributeMap_MatchesGoldenTable()
    {
        foreach (var type in SpecificEnumUtils<PropertyType>.Values)
        {
            Assert.IsTrue(Expected.TryGetValue(type, out var expected),
                $"Golden table has no entry for {type}; add one when introducing a PropertyType.");

            var attr = PropertySystem.Property.GetAttribute(type);
            Assert.AreEqual(expected.Db, attr.DbValueType, $"{type} DbValueType");
            Assert.AreEqual(expected.Biz, attr.BizValueType, $"{type} BizValueType");
            Assert.AreEqual(expected.IsReference, attr.IsReferenceValueType, $"{type} IsReferenceValueType");

            var descriptor = PropertySystem.Property.GetDescriptor(type);
            Assert.AreEqual(attr.DbValueType, descriptor.DbValueType, $"{type} descriptor DbValueType");
            Assert.AreEqual(attr.BizValueType, descriptor.BizValueType, $"{type} descriptor BizValueType");
        }
    }
}
