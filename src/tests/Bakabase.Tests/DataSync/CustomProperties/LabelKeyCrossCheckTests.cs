using System.Text;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>
/// §3.4, §13.2: <see cref="DataSyncLabelKey.Fold"/> agrees with the Property module's own label comparer — the one
/// <c>GetLabelComparer()</c> returns for each of the four reference types' options, and the one
/// <see cref="ReferencePropertyOptionsNormalizer"/> folds with — over every BMP code unit, every pair that comparer
/// treats as equal, supplementary characters and a corpus of real labels. The pure module's test pins the key against
/// <see cref="StringComparer.OrdinalIgnoreCase"/>; this one fails when the Property module changes its comparer.
/// </summary>
[TestClass]
public class LabelKeyCrossCheckTests
{
    private static IEnumerable<IReferencePropertyOptions> AllOptions(bool ignoreCase) =>
    [
        new SingleChoicePropertyOptions { IgnoreCase = ignoreCase },
        new MultipleChoicePropertyOptions { IgnoreCase = ignoreCase },
        new TagsPropertyOptions { IgnoreCase = ignoreCase },
        new MultilevelPropertyOptions { IgnoreCase = ignoreCase },
    ];

    private static StringComparer ModuleComparer(bool ignoreCase) =>
        new MultipleChoicePropertyOptions { IgnoreCase = ignoreCase }.GetLabelComparer();

    [TestMethod]
    public void EveryReferenceTypeCompareLabelsWithTheComparerTheKeyAgreesWith()
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            foreach (var options in AllOptions(ignoreCase))
                Assert.AreSame(DataSyncLabelKey.ComparerFor(ignoreCase), options.GetLabelComparer(), options.GetType().Name);
        }

        IReferencePropertyOptions? none = null;
        Assert.AreSame(DataSyncLabelKey.ComparerFor(false), none.GetLabelComparer(), "no options: ordinal");
    }

    [TestMethod]
    public void EveryBmpCodeUnitFoldsToAKeyTheComparerCallsEqual()
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            var comparer = ModuleComparer(ignoreCase);
            for (var i = 0; i < 0x10000; i++)
            {
                var s = ((char) i).ToString();
                var key = DataSyncLabelKey.Fold(s, ignoreCase);
                Assert.IsTrue(comparer.Equals(s, key), $"U+{i:X4}, IgnoreCase {ignoreCase}");
                Assert.AreEqual(key, DataSyncLabelKey.Fold(key, ignoreCase), $"U+{i:X4}: not a fixed point");
            }
        }
    }

    [TestMethod]
    public void EveryPairTheComparerTreatsAsEqualHasOneKey()
    {
        var comparer = ModuleComparer(true);
        var pairs = 0;
        foreach (var bucket in BmpBuckets(comparer))
        {
            for (var x = 0; x < bucket.Count; x++)
            {
                for (var y = x + 1; y < bucket.Count; y++)
                {
                    var equal = comparer.Equals(bucket[x], bucket[y]);
                    Assert.AreEqual(equal, DataSyncLabelKey.Fold(bucket[x], true) == DataSyncLabelKey.Fold(bucket[y], true),
                        $"U+{(int) bucket[x][0]:X4} / U+{(int) bucket[y][0]:X4}");
                    if (equal) pairs++;
                }
            }
        }

        Assert.IsTrue(pairs > 1_000, $"only {pairs} equal pairs: the grouping found too little");
    }

    /// <summary>
    /// The normalizer the service runs on every save folds two new choices exactly when their keys are equal, for
    /// every pair the comparer calls equal and for a neighbour it does not.
    /// </summary>
    [TestMethod]
    public void TheNormalizerFoldsExactlyWhatTheKeyFolds()
    {
        var comparer = ModuleComparer(true);
        var checkedPairs = 0;
        foreach (var bucket in BmpBuckets(comparer))
        {
            for (var y = 1; y < bucket.Count; y++)
            {
                AssertNormalizerAgrees(bucket[0], bucket[y]);
                checkedPairs++;
            }
        }

        foreach (var (a, b) in RealLabels())
        {
            AssertNormalizerAgrees(a, b);
            checkedPairs++;
        }

        Assert.IsTrue(checkedPairs > 1_000);

        static void AssertNormalizerAgrees(string a, string b)
        {
            var options = new MultipleChoicePropertyOptions
            {
                IgnoreCase = true,
                Choices = [new ChoiceOptions { Value = "1", Label = a }, new ChoiceOptions { Value = "2", Label = b }],
            };
            ReferencePropertyOptionsNormalizer.Normalize(options);
            var folded = options.Choices!.Count == 1;
            Assert.AreEqual(folded, DataSyncLabelKey.Fold(a, true) == DataSyncLabelKey.Fold(b, true),
                $"\"{Escape(a)}\" / \"{Escape(b)}\"");
        }
    }

    [TestMethod]
    public void SupplementaryCharactersAgreeWithTheComparer()
    {
        var comparer = ModuleComparer(true);
        for (var cp = 0x10000; cp < 0x20000; cp++)
        {
            var rune = new Rune(cp);
            var s = rune.ToString();
            var key = DataSyncLabelKey.Fold(s, true);
            Assert.IsTrue(comparer.Equals(s, key), $"U+{cp:X5}");
            foreach (var other in new[] { Rune.ToUpperInvariant(rune), Rune.ToLowerInvariant(rune) })
            {
                var o = other.ToString();
                Assert.AreEqual(comparer.Equals(s, o), key == DataSyncLabelKey.Fold(o, true), $"U+{cp:X5} / U+{other.Value:X5}");
            }
        }
    }

    [TestMethod]
    public void RealLabelsAgreeWithTheComparer()
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            var comparer = ModuleComparer(ignoreCase);
            foreach (var (a, b) in RealLabels())
            {
                Assert.AreEqual(comparer.Equals(a, b),
                    DataSyncLabelKey.Fold(a, ignoreCase) == DataSyncLabelKey.Fold(b, ignoreCase),
                    $"\"{Escape(a)}\" / \"{Escape(b)}\", IgnoreCase {ignoreCase}");
            }
        }
    }

    /// <summary>Single BMP code units grouped by the comparer's hash code (equal strings hash equal), groups of two or more.</summary>
    private static IEnumerable<List<string>> BmpBuckets(StringComparer comparer)
    {
        var buckets = new Dictionary<int, List<string>>();
        for (var i = 0; i < 0x10000; i++)
        {
            if (char.IsSurrogate((char) i)) continue;
            var s = ((char) i).ToString();
            var hash = comparer.GetHashCode(s);
            if (!buckets.TryGetValue(hash, out var bucket)) buckets[hash] = bucket = [];
            bucket.Add(s);
        }

        return buckets.Values.Where(b => b.Count > 1);
    }

    /// <summary>Real labels and the classic casing traps, as pairs.</summary>
    private static IEnumerable<(string, string)> RealLabels() =>
    [
        ("Action", "ACTION"), ("Action", "action"), ("Action", "Actíon"), ("Ä", "ä"), ("ß", "SS"), ("ß", "ẞ"),
        ("Straße", "STRASSE"), ("ſ", "S"), ("ſ", "s"), ("ı", "I"), ("İstanbul", "istanbul"), ("K", "K"), ("µ", "Μ"),
        ("µ", "μ"), ("ς", "σ"), ("ς", "Σ"), ("Ǆ", "ǅ"), ("ǆ", "Ǆ"), ("Ω", "Ω"), ("ﬃ", "FFI"), ("Ｆｕｌｌ", "ｆｕｌｌ"),
        ("京都", "京都"), ("アクション", "ｱｸｼｮﾝ"), ("Ёлка", "ёЛКА"), ("Ελλάδα", "ΕΛΛΆΔΑ"), ("𐐀𐐁", "𐐨𐐩"),
        ("🎬 Film", "🎬 FILM"), ("á", "á"), ("tab\tlabel", "TAB\tLABEL"), ("\ud801", "\ud801"), ("a\ud801b", "A\ud801B"),
        ("Sci-Fi", "sci-fi"), ("R&B", "r&b"), ("", ""),
    ];

    private static string Escape(string s) =>
        string.Concat(s.Select(c => c < 0x20 || char.IsSurrogate(c) ? $"\\u{(int) c:x4}" : c.ToString()));
}
