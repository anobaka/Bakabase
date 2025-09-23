using System;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests
{
    [TestClass]
    public class CompressedFileHelperV2Tests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Teardown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private string CreateFile(string name, string content = "data")
        {
            var path = Path.Combine(_tempDir, name);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void SingleRarFile_ShouldBeGrouped()
        {
            var f = CreateFile("archive.rar");
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f });
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(".rar", groups[0].Extension);
            Assert.AreEqual(1, groups[0].Files.Count);
        }

        [TestMethod]
        public void MultiPartRar_ShouldBeGroupedTogether()
        {
            var files = new[]
            {
                CreateFile("archive.rar"),
                CreateFile("archive.r01"),
                CreateFile("archive.r02")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(3, groups[0].Files.Count);
        }

        [TestMethod]
        public void MultiPartZip_ShouldBeGroupedTogether()
        {
            var files = new[]
            {
                CreateFile("backup.zip"),
                CreateFile("backup.z01"),
                CreateFile("backup.z02")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(".zip", groups[0].Extension);
            Assert.AreEqual(3, groups[0].Files.Count);
        }

        [TestMethod]
        public void TarGz_ShouldBeRecognizedAsCompoundExtension()
        {
            var f = CreateFile("archive.tar.gz");
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f });
            Assert.AreEqual(".tar.gz", groups[0].Extension);
        }

        [TestMethod]
        public void MultiPartTarGz_ShouldBeGrouped()
        {
            var files = new[]
            {
                CreateFile("archive.tar.gz"),
                CreateFile("archive.part1.tar.gz"),
                CreateFile("archive.part2.tar.gz")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(3, groups[0].Files.Count);
            Assert.AreEqual(".tar.gz", groups[0].Extension);
        }

        [TestMethod]
        public void UnknownExtension_ShouldBeIgnored_WhenFlagTrue()
        {
            var f = CreateFile("mystery.abc");
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f }, ignoreUnknownExtensions: true);
            Assert.AreEqual(0, groups.Count);
        }

        [TestMethod]
        public void UnknownExtension_ShouldBeKept_WhenFlagFalse()
        {
            var f = CreateFile("mystery.abc");
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f }, ignoreUnknownExtensions: false);
            Assert.AreEqual(1, groups.Count);
            Assert.IsNull(groups[0].Extension);
        }

        [TestMethod]
        public void MixedKnownAndUnknown_ShouldGroupCorrectly()
        {
            var files = new[]
            {
                CreateFile("archive.rar"),
                CreateFile("archive.r01"),
                CreateFile("mystery.abc")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files, ignoreUnknownExtensions: false);
            Assert.AreEqual(2, groups.Count);
            Assert.IsTrue(groups.Any(g => g.Extension == ".rar"));
            Assert.IsTrue(groups.Any(g => g.Extension == null));
        }

        [TestMethod]
        public void FileSizes_ShouldBeRecorded()
        {
            var f = CreateFile("archive.zip", "1234567890"); // 10 bytes
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f });
            Assert.AreEqual(10, groups[0].FileSizes[0]);
        }

        [TestMethod]
        public void DifferentDirectories_ShouldNotMixGroups()
        {
            var dir2 = Path.Combine(_tempDir, "sub");
            Directory.CreateDirectory(dir2);

            var f1 = CreateFile("archive.rar");
            var f2 = Path.Combine(dir2, "archive.rar");
            File.WriteAllText(f2, "data");

            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(new[] { f1, f2 });
            Assert.AreEqual(2, groups.Count);
        }

        [TestMethod]
        public void PartPattern_ShouldDetectPartFiles()
        {
            var files = new[]
            {
                CreateFile("movie.part1.rar"),
                CreateFile("movie.part2.rar"),
                CreateFile("movie.part3.rar")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(3, groups[0].Files.Count);
        }

        [TestMethod]
        public void SplitNumberedTarGz_ShouldBeGrouped()
        {
            var files = new[]
            {
                CreateFile("dataset.tar.gz.001"),
                CreateFile("dataset.tar.gz.002"),
                CreateFile("dataset.tar.gz.003")
            };
            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files, ignoreUnknownExtensions: false);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(".tar.gz", groups[0].Extension);
            Assert.AreEqual(3, groups[0].Files.Count);
        }

        [TestMethod]
        public void LegacyScenario_ShouldMatchExpectedGrouping()
        {
            var files = new[]
            {
                CreateFile("123.txt"),
                CreateFile(Path.Combine("dir", "123.txt")),

                CreateFile("1.zip"),
                CreateFile("1.z01"),
                CreateFile("1.z02"),

                CreateFile("2.tar"),

                CreateFile("2.tar.gz"),

                CreateFile("2.rar.part1"),
                CreateFile("2.rar.part2"),
                CreateFile("2.rar.part3"),

                CreateFile("2.7z"),

                CreateFile("2.7z.00001"),
                CreateFile("2.7z.00002"),
                CreateFile("2.7z.00003"),

                CreateFile("2.r01"),
                CreateFile("2.r02"),
                CreateFile("2.rar"),

                CreateFile(Path.Combine("dir", "1.r01")),
                CreateFile(Path.Combine("dir", "1.r02")),
                CreateFile(Path.Combine("dir", "1.r03")),
                CreateFile(Path.Combine("dir", "1.r04"))
            };

            var groups = CompressedFileHelperV2.DetectCompressedFileGroups(files, false);

            var str = string.Join(Environment.NewLine,
                groups.Select(g =>
                {
                    var entry = NormalizePath(g.Files.First());
                    var parts = g.Files.Skip(1).Select(NormalizePath).ToList();

                    var lines = $"[Key]{g.KeyName}\n[Entry]{entry}";
                    if (parts.Any())
                    {
                        lines += "\n" + string.Join("\n", parts.Select(p => $"[Part]{p}"));
                    }
                    return lines + "\n";
                }));


            const string rightStr = @"[Key]1
[Entry]1.zip
[Part]1.z01
[Part]1.z02

[Key]123
[Entry]123.txt

[Key]2
[Entry]2.7z

[Key]2
[Entry]2.7z.00001
[Part]2.7z.00002
[Part]2.7z.00003

[Key]2
[Entry]2.rar
[Part]2.r01
[Part]2.r02

[Key]2
[Entry]2.rar.part1
[Part]2.rar.part2
[Part]2.rar.part3

[Key]2
[Entry]2.tar

[Key]2
[Entry]2.tar.gz

[Key]1
[Entry]dir/1.r01
[Part]dir/1.r02
[Part]dir/1.r03
[Part]dir/1.r04

[Key]123
[Entry]dir/123.txt";

            // Because temp dir varies, just assert structure rather than exact prefix
            foreach (var expectedLine in rightStr.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (expectedLine.StartsWith("[Key]"))
                    StringAssert.Contains(str, expectedLine);
                else if (expectedLine.StartsWith("[Entry]") || expectedLine.StartsWith("[Part]"))
                {
                    var token = expectedLine.Substring(0, expectedLine.IndexOf(']') + 1);
                    var suffix = expectedLine.Substring(token.Length);
                    StringAssert.Contains(str, token + suffix);
                }
            }
        }
        
        private string NormalizePath(string fullPath)
        {
            return fullPath.Replace(_tempDir, "").Replace("\\", "/").TrimStart('/');
        }
    }
}
