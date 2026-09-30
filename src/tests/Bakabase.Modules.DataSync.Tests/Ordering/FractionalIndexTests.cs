using Bakabase.Modules.DataSync.Ordering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Ordering;

[TestClass]
public class FractionalIndexTests
{
    private static readonly string SmallestInteger = "A" + new string('0', 26);
    private static readonly string LargestInteger = "z" + new string('z', 26);

    /// <summary>
    /// Hand-computed vectors. Integer parts: a head letter gives the digit count (a0 = 0, a1 = 1, Zz = -1,
    /// b00 = 62). Fractions take the midpoint digit, rounding half up (V is 31, the middle of 0..62).
    /// </summary>
    public static IEnumerable<object?[]> BetweenVectors =>
    [
        [null, null, "a0"],
        [null, "a0", "Zz"],                          // decrement 0 → -1
        [null, "Zz", "Zy"],
        ["a0", null, "a1"],                          // increment
        ["a1", null, "a2"],
        ["a0", "a1", "a0V"],                         // adjacent integers: the fraction's middle digit
        ["a1", "a2", "a1V"],
        ["a0V", "a1", "a0l"],                        // (31 + 62 + 1) / 2 = 47 = 'l'
        ["Zz", "a0", "ZzV"],
        ["Zz", "a1", "a0"],                          // an integer fits between
        [null, "Y00", "Xzzz"],                       // decrement across a length change
        ["bzz", null, "c000"],                       // increment across a length change
        ["a0", "a0V", "a0G"],                        // 31 / 2 rounds up to 16 = 'G'
        ["a0", "a0G", "a08"],
        ["b125", "b129", "b127"],
        ["a0", "a1V", "a1"],
        ["Zz", "a01", "a0"],
        [null, "a0V", "a0"],                         // b's integer alone is smaller than b
        [null, "b999", "b99"],
        [null, "a1", "a0"],
        ["az", null, "b00"],
        [null, "b00", "az"],
        ["a0", "a01", "a00V"],                       // consecutive digits: keep a's digit, go on above it
        ["a0V", "a0W", "a0VV"],
        ["a0z", "a1", "a0zV"],
        ["a0zV", "a1", "a0zl"],
        ["a0l", "a0m", "a0lV"],
        ["a0zzzzzzzzzzV", "a1", "a0zzzzzzzzzzl"],    // long fraction
        ["a0", "a0000000001", "a0000000000V"],       // long common prefix of zeros
        [null, SmallestInteger + "1", SmallestInteger + "0V"],   // below the smallest integer: a fraction
        [LargestInteger[..^1] + "y", null, LargestInteger],
        [LargestInteger, null, LargestInteger + "V"],             // above the largest integer: a fraction
    ];

    [TestMethod]
    [DynamicData(nameof(BetweenVectors))]
    public void BetweenMatchesTheVectors(string? a, string? b, string expected)
    {
        var key = FractionalIndex.Between(a, b);
        Assert.AreEqual(expected, key);
        Assert.IsTrue(FractionalIndex.IsValid(key));
        if (a is not null) Assert.IsTrue(string.CompareOrdinal(a, key) < 0);
        if (b is not null) Assert.IsTrue(string.CompareOrdinal(key, b) < 0);
    }

    [TestMethod]
    public void NBetweenMatchesTheVectors()
    {
        CollectionAssert.AreEqual(new[] { "a0", "a1", "a2", "a3", "a4" }, FractionalIndex.NBetween(null, null, 5).ToArray());
        CollectionAssert.AreEqual("a5 a6 a7 a8 a9 aA aB aC aD aE".Split(' '),
            FractionalIndex.NBetween("a4", null, 10).ToArray());
        CollectionAssert.AreEqual("Zv Zw Zx Zy Zz".Split(' '), FractionalIndex.NBetween(null, "a0", 5).ToArray());
        CollectionAssert.AreEqual(
            "a04 a08 a0G a0K a0O a0V a0Z a0d a0l a0t a1 a14 a18 a1G a1O a1V a1Z a1d a1l a1t".Split(' '),
            FractionalIndex.NBetween("a0", "a2", 20).ToArray());
        Assert.AreEqual(0, FractionalIndex.NBetween("a0", "a1", 0).Count);
        CollectionAssert.AreEqual(new[] { "a0V" }, FractionalIndex.NBetween("a0", "a1", 1).ToArray());
    }

    [TestMethod]
    public void InvalidInputThrows()
    {
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between(null, SmallestInteger));
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("a00", null));   // trailing zero
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("a00", "a1"));
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("0", "1"));     // bad head
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("a1", "a0"));
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("b1", null));   // too short
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between("a0-", null));  // not a digit
        Assert.ThrowsException<ArgumentException>(() => FractionalIndex.NBetween("a1", "a0", 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => FractionalIndex.NBetween(null, null, -1));
    }

    [TestMethod]
    public void BetweenEqualKeysThrows()
    {
        foreach (var key in new[] { "a0", "a0V", "Zz", "b127" })
        {
            Assert.ThrowsException<ArgumentException>(() => FractionalIndex.Between(key, key), key);
            Assert.ThrowsException<ArgumentException>(() => FractionalIndex.NBetween(key, key, 2), key);
        }
    }

    [TestMethod]
    public void IsValidRecognisesKeys()
    {
        foreach (var valid in new[] { "a0", "a0V", "Zz", "b00", "zzzzzzzzzzzzzzzzzzzzzzzzzzzV", SmallestInteger + "1" })
            Assert.IsTrue(FractionalIndex.IsValid(valid), valid);
        foreach (var invalid in new[] { null, "", "a", "a00", "a0 ", "0", "b0", SmallestInteger, "a0é", "Aa" })
            Assert.IsFalse(FractionalIndex.IsValid(invalid), invalid ?? "null");
    }

    [TestMethod]
    public void BetweenOrdersTenThousandRandomPairs()
    {
        var random = new Random(20260925);
        for (var i = 0; i < 10_000; i++)
        {
            var a = RandomKey(random);
            var b = RandomKey(random);
            var cmp = string.CompareOrdinal(a, b);
            if (cmp == 0) continue;
            if (cmp > 0) (a, b) = (b, a);

            var lowerOpen = random.Next(8) == 0;
            var upperOpen = random.Next(8) == 0;
            var left = lowerOpen ? null : a;
            var right = upperOpen ? null : b;
            var key = FractionalIndex.Between(left, right);
            Assert.IsTrue(FractionalIndex.IsValid(key), key);
            if (left is not null) Assert.IsTrue(string.CompareOrdinal(left, key) < 0, $"{left} < {key}");
            if (right is not null) Assert.IsTrue(string.CompareOrdinal(key, right) < 0, $"{key} < {right}");
            Assert.AreEqual(key, FractionalIndex.Between(left, right), "deterministic");
        }
    }

    [TestMethod]
    public void NBetweenSpreadsAndStaysShort()
    {
        var random = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var a = RandomKey(random);
            var b = RandomKey(random);
            if (string.CompareOrdinal(a, b) == 0) continue;
            if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            var n = random.Next(1, 200);
            var keys = FractionalIndex.NBetween(a, b, n);
            Assert.AreEqual(n, keys.Count);
            var previous = a;
            foreach (var key in keys)
            {
                Assert.IsTrue(FractionalIndex.IsValid(key));
                Assert.IsTrue(string.CompareOrdinal(previous, key) < 0);
                previous = key;
            }

            Assert.IsTrue(string.CompareOrdinal(previous, b) < 0);
        }

        // Halving keeps n keys between two neighbours about log62(n) digits longer than their common prefix.
        var thousand = FractionalIndex.NBetween("a0", "a1", 1000);
        Assert.IsTrue(thousand.Max(k => k.Length) <= "a0".Length + 3, thousand.Max(k => k.Length).ToString());
    }

    [TestMethod]
    public void RepeatedInsertionAtTheFrontGrowsSlowly()
    {
        // The worst case for key length: always inserting just after the same left key.
        var right = FractionalIndex.Between(null, null);
        for (var i = 0; i < 50; i++) right = FractionalIndex.Between("Zz", right);
        Assert.IsTrue(right.Length < 20, right);
    }

    private static string RandomKey(Random random)
    {
        var headIsUpper = random.Next(2) == 0;
        var integerDigits = random.Next(1, 4);
        var head = headIsUpper ? (char)('Z' - integerDigits + 1) : (char)('a' + integerDigits - 1);
        var chars = new List<char> { head };
        for (var i = 0; i < integerDigits; i++) chars.Add(FractionalIndex.Digits[random.Next(62)]);
        var fraction = random.Next(0, 6);
        for (var i = 0; i < fraction; i++) chars.Add(FractionalIndex.Digits[random.Next(62)]);
        while (chars.Count > integerDigits + 1 && chars[^1] == '0') chars.RemoveAt(chars.Count - 1);
        return new string(chars.ToArray());
    }
}
