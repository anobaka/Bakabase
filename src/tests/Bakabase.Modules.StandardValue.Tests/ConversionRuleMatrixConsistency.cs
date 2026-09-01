using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// The hand-written conversion rule matrix (StandardValueInternals.ConversionRules) and the
/// handlers' actual behavior have no compile-time link. These tests tie them together through
/// the golden conversion examples: an Incompatible pair must never produce a value, and a
/// compatible pair must produce one for at least one golden sample.
/// </summary>
[TestClass]
public sealed class ConversionRuleMatrixConsistency
{
    [TestMethod]
    public void RuleMatrix_CoversEveryTypePair()
    {
        foreach (var fromType in SpecificEnumUtils<StandardValueType>.Values)
        {
            foreach (var toType in SpecificEnumUtils<StandardValueType>.Values)
            {
                // Throws KeyNotFoundException if a pair is missing from the matrix.
                _ = StandardValueSystem.GetConversionRules(fromType, toType);
            }
        }
    }

    [TestMethod]
    public void IncompatiblePairs_NeverProduceAValue()
    {
        var examples = StandardValueSystem.GetExpectedConversions();
        foreach (var fromType in SpecificEnumUtils<StandardValueType>.Values)
        {
            foreach (var toType in SpecificEnumUtils<StandardValueType>.Values)
            {
                var rules = StandardValueSystem.GetConversionRules(fromType, toType);
                if (!rules.HasFlag(StandardValueConversionRule.Incompatible))
                {
                    continue;
                }

                foreach (var (fromValue, _) in examples[fromType][toType])
                {
                    var converted = StandardValueSystem.Convert(fromValue, fromType, toType);
                    Assert.IsNull(converted,
                        $"{fromType} -> {toType} is declared Incompatible but converted '{fromValue}' to '{converted}'");
                }
            }
        }
    }

    [TestMethod]
    public void CompatiblePairs_ProduceAValueForAtLeastOneGoldenSample()
    {
        var examples = StandardValueSystem.GetExpectedConversions();
        foreach (var fromType in SpecificEnumUtils<StandardValueType>.Values)
        {
            foreach (var toType in SpecificEnumUtils<StandardValueType>.Values)
            {
                var rules = StandardValueSystem.GetConversionRules(fromType, toType);
                if (rules.HasFlag(StandardValueConversionRule.Incompatible))
                {
                    continue;
                }

                var samples = examples[fromType][toType];
                if (samples.Count == 0)
                {
                    continue;
                }

                Assert.IsTrue(samples.Any(s => s.ExpectedValue != null),
                    $"{fromType} -> {toType} is declared compatible but no golden sample expects a value");
            }
        }
    }
}
