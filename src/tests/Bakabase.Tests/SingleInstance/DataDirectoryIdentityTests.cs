using System;
using System.IO;
using System.Text;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rules = Bakabase.Infrastructures.Components.App.SingleInstance.DataDirectoryIdentity.PathRules;

namespace Bakabase.Tests.SingleInstance;

[TestClass]
public class DataDirectoryIdentityTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-ident-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static string Rooted(params string[] parts) =>
        Path.Combine(new[] {OperatingSystem.IsWindows() ? @"C:\" : "/"}.Concat(parts).ToArray());

    [TestMethod]
    public void Trailing_separators_and_dot_segments_do_not_make_a_different_directory()
    {
        var plain = DataDirectoryIdentity.Normalize(Rooted("data", "Bakabase"), Rules.CaseSensitive, false);

        Assert.AreEqual(plain, DataDirectoryIdentity.Normalize(Rooted("data", "Bakabase") + Path.DirectorySeparatorChar,
            Rules.CaseSensitive, false));
        Assert.AreEqual(plain, DataDirectoryIdentity.Normalize(Rooted("data", "x", "..", ".", "Bakabase"),
            Rules.CaseSensitive, false));
    }

    [TestMethod]
    public void Case_is_folded_on_Windows_and_macOS_but_not_on_Linux()
    {
        var upper = Rooted("Data", "Bakabase");
        var lower = Rooted("data", "bakabase");

        Assert.AreEqual(DataDirectoryIdentity.Normalize(upper, Rules.Windows, false),
            DataDirectoryIdentity.Normalize(lower, Rules.Windows, false));
        Assert.AreEqual(DataDirectoryIdentity.Normalize(upper, Rules.MacOs, false),
            DataDirectoryIdentity.Normalize(lower, Rules.MacOs, false));
        Assert.AreNotEqual(DataDirectoryIdentity.Normalize(upper, Rules.CaseSensitive, false),
            DataDirectoryIdentity.Normalize(lower, Rules.CaseSensitive, false),
            "Linux file systems keep Data and data apart, so the guard must too");
    }

    [TestMethod]
    public void Composed_and_decomposed_names_are_one_directory_on_macOS_only()
    {
        var composed = Rooted("Caf\u00e9");      // é as one code point (NFC)
        var decomposed = Rooted("Cafe\u0301");   // e + combining acute (NFD), what Finder hands over
        Assert.AreNotEqual(composed, decomposed);

        Assert.AreEqual(DataDirectoryIdentity.Normalize(composed, Rules.MacOs, false),
            DataDirectoryIdentity.Normalize(decomposed, Rules.MacOs, false));
        Assert.AreNotEqual(DataDirectoryIdentity.Normalize(composed, Rules.CaseSensitive, false),
            DataDirectoryIdentity.Normalize(decomposed, Rules.CaseSensitive, false));
    }

    [TestMethod]
    public void A_symbolic_link_names_the_directory_it_points_at()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "link");
        if (!TryCreateLink(link, real)) Assert.Inconclusive("Cannot create symbolic links here.");

        Assert.AreEqual(DataDirectoryIdentity.Normalize(real), DataDirectoryIdentity.Normalize(link));
    }

    [TestMethod]
    public void A_link_in_the_middle_of_the_path_is_resolved_too()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(Path.Combine(real, "inner"));
        var link = Path.Combine(_root, "link");
        if (!TryCreateLink(link, real)) Assert.Inconclusive("Cannot create symbolic links here.");

        Assert.AreEqual(DataDirectoryIdentity.Normalize(Path.Combine(real, "inner")),
            DataDirectoryIdentity.Normalize(Path.Combine(link, "inner")));

        // …including below the link, where nothing exists yet: a first launch's data dir.
        Assert.AreEqual(DataDirectoryIdentity.Normalize(Path.Combine(real, "inner", "new", "dir")),
            DataDirectoryIdentity.Normalize(Path.Combine(link, "inner", "new", "dir")));
    }

    [TestMethod]
    public void A_relative_link_resolves_against_its_own_directory()
    {
        var real = Path.Combine(_root, "a", "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "a", "rel");
        try
        {
            Directory.CreateSymbolicLink(link, "real");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("Cannot create symbolic links here.");
        }

        Assert.AreEqual(DataDirectoryIdentity.Normalize(real), DataDirectoryIdentity.Normalize(link));
    }

    [TestMethod]
    public void A_link_cycle_does_not_hang()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        if (!TryCreateLink(a, b) || !TryCreateLink(b, a)) Assert.Inconclusive("Cannot create symbolic links here.");

        // Whatever it answers, it answers.
        Assert.IsFalse(string.IsNullOrEmpty(DataDirectoryIdentity.Normalize(Path.Combine(a, "data"))));
    }

    [TestMethod]
    public void The_hash_is_short_stable_and_distinct()
    {
        var one = DataDirectoryIdentity.Hash("/data/one");
        Assert.AreEqual(16, one.Length);
        StringAssert.Matches(one, new System.Text.RegularExpressions.Regex("^[0-9a-f]{16}$"));
        Assert.AreEqual(one, DataDirectoryIdentity.Hash("/data/one"));
        Assert.AreNotEqual(one, DataDirectoryIdentity.Hash("/data/two"));

        // Pinned: a launch of the next version must find the channel of a running older one.
        Assert.AreEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes("/data/one")))[..16].ToLowerInvariant(), one);
    }

    private static bool TryCreateLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Windows without Developer Mode or elevation.
            return false;
        }
    }
}
