using System.Text;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// §3.4: <see cref="DataSyncLabelKey.Fold"/> agrees with the property's comparer — the Property module's
/// <c>GetLabelComparer()</c>, which is <see cref="StringComparer.OrdinalIgnoreCase"/> with IgnoreCase on and
/// <see cref="StringComparer.Ordinal"/> with it off — over every BMP code unit, every pair the comparer treats as equal,
/// supplementary characters and a corpus of real labels.
/// </summary>
[TestClass]
public class LabelKeyCrossCheckTests
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    [TestMethod]
    public void TheComparerIsThePropertyModulesLabelComparer()
    {
        // GetLabelComparer: options?.IgnoreCase == true ? OrdinalIgnoreCase : Ordinal (F72).
        Assert.AreSame(StringComparer.OrdinalIgnoreCase, DataSyncLabelKey.ComparerFor(true));
        Assert.AreSame(StringComparer.Ordinal, DataSyncLabelKey.ComparerFor(false));
        Assert.AreSame(StringComparer.OrdinalIgnoreCase, OptionMatcher.ComparerFor(true));
    }

    [TestMethod]
    public void EveryBmpCodeUnitFoldsToAnEqualKeyThatIsAFixedPoint()
    {
        for (var i = 0; i < 0x10000; i++)
        {
            var s = ((char)i).ToString();
            var key = DataSyncLabelKey.Fold(s, true);
            Assert.AreEqual(1, key.Length, $"U+{i:X4}");
            Assert.IsTrue(IgnoreCase.Equals(s, key), $"U+{i:X4} → U+{(int)key[0]:X4} is not OrdinalIgnoreCase-equal");
            Assert.AreEqual(key, DataSyncLabelKey.Fold(key, true), $"U+{i:X4}: the key is not a fixed point");
            Assert.AreSame(s, DataSyncLabelKey.Fold(s, false), "IgnoreCase off is the identity");
        }
    }

    [TestMethod]
    public void EveryPairTheComparerTreatsAsEqualHasOneKey()
    {
        // Grouped by the comparer's own hash code: equal strings have equal hashes.
        var buckets = new Dictionary<int, List<string>>();
        for (var i = 0; i < 0x10000; i++)
        {
            if (char.IsSurrogate((char)i)) continue;
            var s = ((char)i).ToString();
            var hash = IgnoreCase.GetHashCode(s);
            if (!buckets.TryGetValue(hash, out var bucket)) buckets[hash] = bucket = [];
            bucket.Add(s);
        }

        var pairs = 0;
        foreach (var bucket in buckets.Values.Where(b => b.Count > 1))
        {
            for (var x = 0; x < bucket.Count; x++)
            {
                for (var y = x + 1; y < bucket.Count; y++)
                {
                    var equal = IgnoreCase.Equals(bucket[x], bucket[y]);
                    var sameKey = DataSyncLabelKey.Fold(bucket[x], true) == DataSyncLabelKey.Fold(bucket[y], true);
                    Assert.AreEqual(equal, sameKey,
                        $"U+{(int)bucket[x][0]:X4} / U+{(int)bucket[y][0]:X4}: comparer {equal}, keys {sameKey}");
                    if (equal) pairs++;
                }
            }
        }

        Assert.IsTrue(pairs > 1_000, $"only {pairs} equal pairs: the grouping found too little");
    }

    [TestMethod]
    public void SupplementaryCharactersAgreeWithTheComparer()
    {
        for (var cp = 0x10000; cp < 0x20000; cp++)
        {
            var rune = new Rune(cp);
            var s = rune.ToString();
            var key = DataSyncLabelKey.Fold(s, true);
            Assert.IsTrue(IgnoreCase.Equals(s, key), $"U+{cp:X5}");
            foreach (var other in new[] { Rune.ToUpperInvariant(rune), Rune.ToLowerInvariant(rune) })
            {
                var o = other.ToString();
                Assert.AreEqual(IgnoreCase.Equals(s, o), key == DataSyncLabelKey.Fold(o, true),
                    $"U+{cp:X5} / U+{other.Value:X5}");
            }
        }
    }

    [TestMethod]
    public void LoneSurrogatesFoldToThemselves()
    {
        foreach (var s in new[] { "\ud801", "\udc28", "\udc28\ud801" })
            Assert.AreEqual(s, DataSyncLabelKey.Fold(s, true));
        Assert.AreEqual("A\ud801B", DataSyncLabelKey.Fold("a\ud801b", true), "only the letters around it fold");
        // A surrogate pair is one character: the Deseret pair folds, the halves alone do not.
        Assert.AreEqual(DataSyncLabelKey.Fold("\U00010428", true), DataSyncLabelKey.Fold("\U00010400", true));
        Assert.IsTrue(IgnoreCase.Equals("\U00010428", "\U00010400"));
    }

    /// <summary>Real labels and the classic casing traps; for each pair the key agrees with the comparer.</summary>
    [TestMethod]
    [DataRow("Action", "ACTION")]
    [DataRow("Action", "action")]
    [DataRow("Action", "Actíon")]
    [DataRow("Ä", "ä")]
    [DataRow("ß", "SS")]
    [DataRow("ß", "ẞ")]
    [DataRow("Straße", "STRASSE")]
    [DataRow("ſ", "S")]
    [DataRow("ſ", "s")]
    [DataRow("ı", "I")]
    [DataRow("İstanbul", "istanbul")]
    [DataRow("K", "K")]
    [DataRow("µ", "Μ")]
    [DataRow("µ", "μ")]
    [DataRow("ς", "σ")]
    [DataRow("ς", "Σ")]
    [DataRow("Ǆ", "ǅ")]
    [DataRow("ǆ", "Ǆ")]
    [DataRow("Ω", "Ω")]
    [DataRow("ﬃ", "FFI")]
    [DataRow("Ｆｕｌｌ", "ｆｕｌｌ")]
    [DataRow("京都", "京都")]
    [DataRow("アクション", "ｱｸｼｮﾝ")]
    [DataRow("Ёлка", "ёЛКА")]
    [DataRow("Ελλάδα", "ΕΛΛΆΔΑ")]
    [DataRow("𐐀𐐁", "𐐨𐐩")]
    [DataRow("🎬 Film", "🎬 FILM")]
    [DataRow("á", "á")]
    [DataRow("tab\tlabel", "TAB\tLABEL")]
    public void RealLabelsAgreeWithTheComparer(string a, string b)
    {
        Assert.AreEqual(IgnoreCase.Equals(a, b), DataSyncLabelKey.Fold(a, true) == DataSyncLabelKey.Fold(b, true), "IgnoreCase on");
        Assert.AreEqual(StringComparer.Ordinal.Equals(a, b), DataSyncLabelKey.Fold(a, false) == DataSyncLabelKey.Fold(b, false),
            "IgnoreCase off");
        Assert.IsTrue(IgnoreCase.Equals(a, DataSyncLabelKey.Fold(a, true)));
    }

    [TestMethod]
    public void KeysAreUpperCaseForEverydayLetters()
    {
        Assert.AreEqual("ACTION", DataSyncLabelKey.Fold("Action", true));
        Assert.AreEqual("STRAßE", DataSyncLabelKey.Fold("Straße", true), "ß has no simple upper case");
        Assert.AreEqual("", DataSyncLabelKey.Fold("", true));
    }
}
