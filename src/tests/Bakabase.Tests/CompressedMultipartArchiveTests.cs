using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed class CompressedMultipartArchiveTests
{
    [TestMethod]
    public void SfxFamilyIsMetadataOnlyUntilTheExactEntryFormatIsConfirmed()
    {
        var files = new[] {"absent/edition.v1.part3.rar", "absent/edition.v1.part1.exe", "absent/edition.v1.part2.rar"};
        var candidate = CompressedFileHelper.DetectSfxMultipartCandidates(files).Single();
        Assert.AreEqual("absent/edition.v1.part1.exe", candidate.EntryFile);
        Assert.AreEqual("edition.v1", candidate.KeyName);
        Assert.IsTrue(candidate.IsComplete); Assert.IsFalse(candidate.IsAmbiguous);
        CollectionAssert.AreEqual(new[] {files[1], files[2], files[0]}, candidate.Files.ToArray());
        var ordinary = CompressedFileHelper.DetectCompressedFileGroups(files, inspectFileSizes: false);
        Assert.IsFalse(ordinary.SelectMany(group => group.Files).Contains(files[1]));
        foreach (var format in new[] {".zip", ".7z", "PE", ""})
        {
            var rejected = CompressedFileHelper.DetectCompressedFileGroups(files, inspectFileSizes: false,
                confirmedSfxFormats: new Dictionary<string, string> {{candidate.EntryFile, format}});
            Assert.IsFalse(rejected.SelectMany(group => group.Files).Contains(candidate.EntryFile));
        }
        foreach (var format in new[] {".rar", "rar", "Rar5"})
        {
            var group = CompressedFileHelper.DetectCompressedFileGroups(files, inspectFileSizes: false,
                confirmedSfxFormats: new Dictionary<string, string> {{candidate.EntryFile, format}}).Single();
            Assert.AreEqual("edition.v1", group.KeyName); Assert.AreEqual(".rar", group.Extension);
            CollectionAssert.AreEqual(candidate.Files.ToArray(), group.Files.ToArray());
            Assert.AreEqual(0, group.FileSizes.Count);
            Assert.AreEqual("edition.v1", CompressedFileHelper.GetExtractionDirectoryName(group.Files, format));
        }
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("two-first-volumes")]
    [DataRow("duplicate-number")]
    [DataRow("second-exe")]
    [DataRow("overflow")]
    public void MissingOrAmbiguousSfxMembersCannotAcquireCleanupMembership(string kind)
    {
        var files = kind switch
        {
            "missing" => new[] {"p/game.part1.exe", "p/game.part3.rar"},
            "two-first-volumes" => ["p/game.part1.exe", "p/game.part1.rar", "p/game.part2.rar"],
            "duplicate-number" => ["p/game.part1.exe", "p/game.part2.rar", "p/game.part02.rar"],
            "second-exe" => ["p/game.part1.exe", "p/game.part2.exe", "p/game.part3.rar"],
            _ => ["p/game.part1.exe", "p/game.part99999999999999999999999999.rar"]
        };
        var candidate = CompressedFileHelper.DetectSfxMultipartCandidates(files).Single();
        Assert.IsFalse(candidate.IsComplete);
        Assert.IsNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(files, ".rar"));
        Assert.AreEqual("game.part1", CompressedFileHelper.GetExtractionDirectoryName(files, ".rar"));
    }

    [TestMethod]
    public void CrossDirectoryStemOrFormatAndOrdinaryExecutablesAreNeverMerged()
    {
        foreach (var files in new[] {
            new[] {"p/game.part1.exe", "q/game.part2.rar"},
            new[] {"p/game.v1.part1.exe", "p/game.v2.part2.rar"},
            new[] {"p/game.part1.exe", "p/game.part2.zip"},
            new[] {"p/game.exe", "p/game.part2.rar"},
            new[] {"p/game.part1.exe"}})
        {
            Assert.AreEqual(0, CompressedFileHelper.DetectSfxMultipartCandidates(files).Count);
            Assert.IsNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(files, ".rar"));
        }
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual(0, CompressedFileHelper.DetectSfxMultipartCandidates(
                ["p/game.part1.exe", "p/Game.part2.rar"]).Count);
    }

    [TestMethod]
    public void StandardMultipartNamesUseWholeBasenameButUnconfirmedPartWordsRemain()
    {
        foreach (var files in new[] {
            new[] {"d/name.v1.part1.rar", "d/name.v1.part2.rar"},
            new[] {"d/name.v1.7z.001", "d/name.v1.7z.002"},
            new[] {"d/name.v1.zip", "d/name.v1.z01", "d/name.v1.z02"},
            new[] {"d/name.v1.rar", "d/name.v1.r00", "d/name.v1.r01"},
            new[] {"d/name.v1.tar.gz.001", "d/name.v1.tar.gz.002"}})
        {
            Assert.IsNotNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(files));
            Assert.AreEqual("name.v1", CompressedFileHelper.GetExtractionDirectoryName(files));
        }
        foreach (var files in new[] {
            new[] {"d/story.part1.rar"}, new[] {"d/story.part1.zip", "d/story.part2.zip"},
            new[] {"d/story.part2.rar", "d/story.part3.rar"},
            new[] {"d/story.7z.001", "d/story.7z.003"},
            new[] {"d/story.part1.exe", "d/story.part2.rar"},
            new[] {"d/story.rar", "d/story.zip"}})
        {
            Assert.IsNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(files));
            Assert.AreEqual(Path.GetFileNameWithoutExtension(files[0]), CompressedFileHelper.GetExtractionDirectoryName(files));
        }
    }

    [TestMethod]
    public void ConfirmedVolumesHaveNaturalOrderingBeyondNineAndRequireTheTrueEntry()
    {
        var files = Enumerable.Range(1, 12).Reverse().Select(i => $"d/v1.part{i}.rar").ToArray();
        var group = CompressedFileHelper.DetectCompressedFileGroups(files, inspectFileSizes: false).Single();
        CollectionAssert.AreEqual(Enumerable.Range(1, 12).Select(i => $"d/v1.part{i}.rar").ToArray(), group.Files.ToArray());
        Assert.IsNotNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(group.Files));
        Assert.IsNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(files));
        var sfx = files.Select(path => path.EndsWith("part1.rar") ? Path.ChangeExtension(path, ".exe") : path).ToArray();
        var candidate = CompressedFileHelper.DetectSfxMultipartCandidates(sfx).Single();
        Assert.IsTrue(candidate.IsComplete);
        Assert.IsNotNull(CompressedFileHelper.GetCompleteMultipartArchiveGroup(candidate.Files, ".rar"));
        CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), candidate.Files.Select(path =>
            int.Parse(Path.GetFileNameWithoutExtension(path).Split("part")[1])).ToArray());
    }
}
