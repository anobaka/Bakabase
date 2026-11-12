using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class CompressedFileHelperDottedNamesTests
{
    // These are deliberately absent paths. Grouping projected workflow names must not
    // require creating or reading archive payloads.
    private static CompressedFileGroup[] Groups(params string[] names) =>
        CompressedFileHelper.DetectCompressedFileGroups(
            names.Select(n => Path.Combine("metadata-only", n)).ToArray(),
            inspectFileSizes: false).ToArray();

    [TestMethod]
    [DataRow(".zip")]
    [DataRow(".rar")]
    [DataRow(".7z")]
    [DataRow(".tar")]
    [DataRow(".gz")]
    [DataRow(".bz2")]
    [DataRow(".tgz")]
    [DataRow(".tar.gz")]
    [DataRow(".tar.bz2")]
    [DataRow(".tar.xz")]
    public void Independent_dotted_archives_have_separate_exact_membership(string extension)
    {
        var groups = Groups("foo.v1" + extension, "foo.v2" + extension);
        Assert.AreEqual(2, groups.Length);
        CollectionAssert.AreEquivalent(new[] {"foo.v1", "foo.v2"}, groups.Select(g => g.KeyName).ToArray());
        foreach (var group in groups)
        {
            Assert.AreEqual(extension, group.Extension);
            Assert.AreEqual(1, group.Files.Count);
            Assert.AreEqual(group.KeyName + extension, Path.GetFileName(group.Files[0]));
            Assert.AreEqual(0, group.FileSizes.Count);
        }
    }

    [TestMethod]
    public void Dotted_part_rar_preserves_each_basename_and_all_its_parts()
    {
        var groups = Groups("foo.v1.part2.rar", "foo.v2.part1.rar", "foo.v1.part1.rar", "foo.v2.part2.rar");
        Assert.AreEqual(2, groups.Length);
        foreach (var version in new[] {"v1", "v2"})
        {
            var group = groups.Single(g => g.KeyName == "foo." + version);
            Assert.AreEqual(".rar", group.Extension);
            CollectionAssert.AreEqual(new[] {$"foo.{version}.part1.rar", $"foo.{version}.part2.rar"},
                group.Files.Select(Path.GetFileName).ToArray());
        }
    }

    [TestMethod]
    [DataRow(".rar", ".r00", ".r01")]
    [DataRow(".zip", ".z01", ".z02")]
    public void Dotted_legacy_volumes_keep_the_exact_main_archive(string extension, string first, string second)
    {
        var groups = Groups("foo.v1" + second, "foo.v2" + extension, "foo.v1" + extension, "foo.v1" + first);
        Assert.AreEqual(2, groups.Length);
        var group = groups.Single(g => g.KeyName == "foo.v1");
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual("foo.v1" + extension, Path.GetFileName(group.Files[0]));
        CollectionAssert.AreEquivalent(new[] {"foo.v1" + extension, "foo.v1" + first, "foo.v1" + second},
            group.Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual(1, groups.Single(g => g.KeyName == "foo.v2").Files.Count);
    }

    [TestMethod]
    [DataRow(".rar", ".r00", ".r01")]
    [DataRow(".zip", ".z01", ".z02")]
    public void Dotted_legacy_parts_report_archive_format_even_without_the_main_file(string extension, string first, string second)
    {
        var group = Groups("foo.v1" + first, "foo.v1" + second).Single();
        Assert.AreEqual("foo.v1", group.KeyName);
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual(2, group.Files.Count);
    }

    [TestMethod]
    public void Dotted_7z_numbered_volumes_stay_separate_from_an_ordinary_7z()
    {
        var groups = Groups("foo.v1.7z", "foo.v1.7z.001", "foo.v1.7z.002", "foo.v2.7z.001", "foo.v2.7z.002");
        Assert.AreEqual(3, groups.Length);
        Assert.AreEqual(1, groups.Single(g => g.Files.Any(f => Path.GetFileName(f) == "foo.v1.7z")).Files.Count);
        foreach (var version in new[] {"v1", "v2"})
        {
            var group = groups.Single(g => g.KeyName == "foo." + version && g.Files.Count == 2);
            Assert.AreEqual(".7z", group.Extension);
            CollectionAssert.AreEqual(new[] {$"foo.{version}.7z.001", $"foo.{version}.7z.002"},
                group.Files.Select(Path.GetFileName).ToArray());
        }
    }

    [TestMethod]
    [DataRow(".tar.gz")]
    [DataRow(".tar.bz2")]
    [DataRow(".tar.xz")]
    public void Dotted_compound_archives_preserve_existing_part_family(string extension)
    {
        var groups = Groups("foo.v1" + extension, "foo.v1.part1" + extension, "foo.v1.part2" + extension, "foo.v2" + extension);
        Assert.AreEqual(2, groups.Length);
        var group = groups.Single(g => g.KeyName == "foo.v1");
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual(3, group.Files.Count);
        Assert.AreEqual(1, groups.Single(g => g.KeyName == "foo.v2").Files.Count);
    }

    [TestMethod]
    [DataRow(".tar.gz")]
    [DataRow(".tar.bz2")]
    [DataRow(".tar.xz")]
    public void Numbered_compound_archives_remove_the_whole_format_suffix_only(string extension)
    {
        var groups = Groups("foo.v1" + extension + ".001", "foo.v1" + extension + ".002", "foo.v2" + extension + ".001");
        Assert.AreEqual(2, groups.Length);
        var group = groups.Single(g => g.KeyName == "foo.v1");
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual(2, group.Files.Count);
    }

    [TestMethod]
    public void Legacy_trailing_part_names_keep_dots_without_merging_with_regular_rar()
    {
        var groups = Groups("foo.v1.rar.part1", "foo.v1.rar.part2", "foo.v2.rar.part1", "foo.v1.rar");
        Assert.AreEqual(3, groups.Length);
        var parts = groups.Single(g => g.KeyName == "foo.v1" && g.Files.Count == 2);
        Assert.IsNull(parts.Extension);
        CollectionAssert.AreEqual(new[] {"foo.v1.rar.part1", "foo.v1.rar.part2"}, parts.Files.Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void Part_word_inside_an_ordinary_basename_is_not_removed()
    {
        var groups = Groups("foo.part1.preview.rar", "foo.part2.preview.rar", "foo.part1x.rar");
        Assert.AreEqual(3, groups.Length);
        CollectionAssert.AreEquivalent(new[] {"foo.part1.preview", "foo.part2.preview", "foo.part1x"},
            groups.Select(g => g.KeyName).ToArray());
    }

    [TestMethod]
    [DataRow(".zip")]
    [DataRow(".7z")]
    [DataRow(".tar")]
    public void Part_named_ordinary_archives_are_not_a_supported_volume_family(string extension)
    {
        var groups = Groups("foo.part1" + extension, "foo.part2" + extension);
        Assert.AreEqual(2, groups.Length);
        CollectionAssert.AreEquivalent(new[] {"foo.part1", "foo.part2"}, groups.Select(g => g.KeyName).ToArray());
        Assert.IsTrue(groups.All(g => g.Files.Count == 1));
    }

    [TestMethod]
    public void Explicit_part_family_with_disguised_suffix_preserves_dotted_basename()
    {
        var groups = Groups("foo.v1.part1.r删除ar", "foo.v1.part2.r删除ar", "foo.v2.part1.r删除ar");
        Assert.AreEqual(2, groups.Length);
        var first = groups.Single(g => g.KeyName == "foo.v1");
        Assert.AreEqual(".r删除ar", first.Extension);
        CollectionAssert.AreEqual(new[] {"foo.v1.part1.r删除ar", "foo.v1.part2.r删除ar"},
            first.Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual(1, groups.Single(g => g.KeyName == "foo.v2").Files.Count);
    }

    [TestMethod]
    [DataRow(".rar", ".r00", ".r01")]
    [DataRow(".zip", ".z01", ".z02")]
    public void Standard_tail_volume_takes_precedence_over_part_word_in_basename(string extension, string first, string second)
    {
        var group = Groups("foo.part1.preview" + extension, "foo.part1.preview" + first, "foo.part1.preview" + second).Single();
        Assert.AreEqual("foo.part1.preview", group.KeyName);
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual(3, group.Files.Count);
        Assert.AreEqual("foo.part1.preview" + extension, Path.GetFileName(group.Files[0]));
    }

    [TestMethod]
    [DataRow(".7z")]
    [DataRow(".tar.gz")]
    public void Numeric_tail_volume_takes_precedence_over_part_word_in_basename(string extension)
    {
        var group = Groups("foo.part1.preview" + extension + ".001", "foo.part1.preview" + extension + ".002").Single();
        Assert.AreEqual("foo.part1.preview", group.KeyName);
        Assert.AreEqual(extension, group.Extension);
        Assert.AreEqual(2, group.Files.Count);
    }
}
