using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using CliWrap;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests
{
    /// <summary>
    /// A testable version of CompressedFileService that allows specifying the 7z executable path directly.
    /// </summary>
    internal class TestableCompressedFileService : CompressedFileService
    {
        private readonly string _sevenZipPath;

        public TestableCompressedFileService(string sevenZipPath)
            : base(null!, NullLogger<CompressedFileService>.Instance)
        {
            _sevenZipPath = sevenZipPath;
        }

        protected override string? GetSevenZipExecutable() => _sevenZipPath;
    }

    /// <summary>
    /// Integration tests for CompressedFileService extraction functionality.
    /// These tests require 7z to be installed on the system.
    /// </summary>
    [TestClass]
    public class CompressedFileServiceTests
    {
        private string _testDir = null!;
        private string _sourceDir = null!;
        private string _archiveDir = null!;
        private string _extractDir = null!;
        private string? _sevenZipPath;
        private CompressedFileService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"CompressedFileServiceTests_{Guid.NewGuid():N}");
            _sourceDir = Path.Combine(_testDir, "source");
            _archiveDir = Path.Combine(_testDir, "archives");
            _extractDir = Path.Combine(_testDir, "extracted");

            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_archiveDir);
            Directory.CreateDirectory(_extractDir);

            // Try to find 7z executable
            _sevenZipPath = Find7zExecutable();

            if (_sevenZipPath != null)
            {
                _service = new TestableCompressedFileService(_sevenZipPath);
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private static string? Find7zExecutable()
        {
            // Common paths for 7z
            var possiblePaths = new[]
            {
                "7z", // In PATH
                "7za", // In PATH (standalone)
                "/usr/bin/7z",
                "/usr/local/bin/7z",
                "/opt/homebrew/bin/7z",
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe"
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    var result = Cli.Wrap(path)
                        .WithArguments("--help")
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync()
                        .GetAwaiter()
                        .GetResult();

                    if (result.ExitCode == 0)
                    {
                        return path;
                    }
                }
                catch
                {
                    // Try next path
                }
            }

            return null;
        }

        private void SkipIf7zNotAvailable()
        {
            if (_sevenZipPath == null)
            {
                Assert.Inconclusive("7z executable not found. Skipping integration test.");
            }
        }

        /// <summary>
        /// Creates test source files for compression
        /// </summary>
        private void CreateTestSourceFiles()
        {
            // Create a simple directory structure with files
            File.WriteAllText(Path.Combine(_sourceDir, "file1.txt"), "Content of file 1");
            File.WriteAllText(Path.Combine(_sourceDir, "file2.txt"), "Content of file 2");

            var subDir = Path.Combine(_sourceDir, "subdir");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "file3.txt"), "Content of file 3 in subdir");
        }

        /// <summary>
        /// Creates an archive using 7z command line
        /// </summary>
        private async Task CreateArchive(string archivePath, string? format = null)
        {
            var args = new List<string> { "a" };

            if (format != null)
            {
                args.Add($"-t{format}");
            }

            args.Add(archivePath);
            args.Add(Path.Combine(_sourceDir, "*"));

            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(args, true)
                .WithWorkingDirectory(_sourceDir)
                .ExecuteAsync();
        }

        /// <summary>
        /// Creates a .tar.gz archive (two-stage compression)
        /// </summary>
        private async Task CreateTarGzArchive(string archivePath)
        {
            var tarPath = Path.ChangeExtension(archivePath, null); // Remove .gz to get .tar path
            if (!tarPath.EndsWith(".tar"))
            {
                tarPath = archivePath.Replace(".tar.gz", ".tar").Replace(".tgz", ".tar");
            }

            // First create tar
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-ttar", tarPath, Path.Combine(_sourceDir, "*") }, true)
                .WithWorkingDirectory(_sourceDir)
                .ExecuteAsync();

            // Then compress to gzip
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-tgzip", archivePath, tarPath }, true)
                .ExecuteAsync();

            // Remove intermediate tar
            File.Delete(tarPath);
        }

        /// <summary>
        /// Creates a .tar.bz2 archive (two-stage compression)
        /// </summary>
        private async Task CreateTarBz2Archive(string archivePath)
        {
            var tarPath = archivePath.Replace(".tar.bz2", ".tar");

            // First create tar
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-ttar", tarPath, Path.Combine(_sourceDir, "*") }, true)
                .WithWorkingDirectory(_sourceDir)
                .ExecuteAsync();

            // Then compress to bzip2
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-tbzip2", archivePath, tarPath }, true)
                .ExecuteAsync();

            // Remove intermediate tar
            File.Delete(tarPath);
        }

        /// <summary>
        /// Creates a .tar.xz archive (two-stage compression)
        /// </summary>
        private async Task CreateTarXzArchive(string archivePath)
        {
            var tarPath = archivePath.Replace(".tar.xz", ".tar");

            // First create tar
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-ttar", tarPath, Path.Combine(_sourceDir, "*") }, true)
                .WithWorkingDirectory(_sourceDir)
                .ExecuteAsync();

            // Then compress to xz
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-txz", archivePath, tarPath }, true)
                .ExecuteAsync();

            // Remove intermediate tar
            File.Delete(tarPath);
        }

        private void VerifyExtractedFiles(string extractDir)
        {
            // Verify all expected files exist
            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "file1.txt")), "file1.txt should exist");
            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "file2.txt")), "file2.txt should exist");
            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "subdir", "file3.txt")), "subdir/file3.txt should exist");

            // Verify content
            Assert.AreEqual("Content of file 1", File.ReadAllText(Path.Combine(extractDir, "file1.txt")));
            Assert.AreEqual("Content of file 2", File.ReadAllText(Path.Combine(extractDir, "file2.txt")));
            Assert.AreEqual("Content of file 3 in subdir", File.ReadAllText(Path.Combine(extractDir, "subdir", "file3.txt")));
        }

        private void ClearExtractDir()
        {
            if (Directory.Exists(_extractDir))
            {
                Directory.Delete(_extractDir, true);
            }
            Directory.CreateDirectory(_extractDir);
        }

        #region Single-Stage Format Tests

        [TestMethod]
        public async Task ExtractZip_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.zip");
            await CreateArchive(archivePath, "zip");

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);
        }

        [TestMethod]
        public async Task Extract7z_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.7z");
            await CreateArchive(archivePath, "7z");

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);
        }

        [TestMethod]
        public async Task ExtractTar_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar");
            await CreateArchive(archivePath, "tar");

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);
        }

        [TestMethod]
        public async Task ExtractGz_SingleFile_ShouldExtract()
        {
            SkipIf7zNotAvailable();

            // For .gz, we test with a single file (gzip doesn't support directories)
            var sourceFile = Path.Combine(_extractDir, "single.txt");
            File.WriteAllText(sourceFile, "Single file content");

            var archivePath = Path.Combine(_extractDir, "single.txt.gz");
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-tgzip", archivePath, sourceFile }, true)
                .ExecuteAsync();

            // Remove original to test extraction
            File.Delete(sourceFile);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            Assert.IsTrue(File.Exists(sourceFile), "single.txt should be extracted");
            Assert.AreEqual("Single file content", File.ReadAllText(sourceFile));
        }

        #endregion

        #region Compound Format Tests (Two-Stage Extraction)

        [TestMethod]
        public async Task ExtractTarGz_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar.gz");
            await CreateTarGzArchive(archivePath);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);

            // Verify no intermediate .tar file remains
            Assert.IsFalse(File.Exists(Path.Combine(_extractDir, "test.tar")),
                "Intermediate .tar file should be cleaned up");
        }

        [TestMethod]
        public async Task ExtractTgz_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            // .tgz is same as .tar.gz
            var tarPath = Path.Combine(_archiveDir, "test.tar");
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-ttar", tarPath, Path.Combine(_sourceDir, "*") }, true)
                .WithWorkingDirectory(_sourceDir)
                .ExecuteAsync();

            var archivePath = Path.Combine(_extractDir, "test.tgz");
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", "-tgzip", archivePath, tarPath }, true)
                .ExecuteAsync();

            File.Delete(tarPath);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);
        }

        [TestMethod]
        public async Task ExtractTarBz2_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar.bz2");
            await CreateTarBz2Archive(archivePath);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);

            // Verify no intermediate .tar file remains
            Assert.IsFalse(File.Exists(Path.Combine(_extractDir, "test.tar")),
                "Intermediate .tar file should be cleaned up");
        }

        [TestMethod]
        public async Task ExtractTarXz_ShouldExtractAllFiles()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar.xz");
            await CreateTarXzArchive(archivePath);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            VerifyExtractedFiles(_extractDir);

            // Verify no intermediate .tar file remains
            Assert.IsFalse(File.Exists(Path.Combine(_extractDir, "test.tar")),
                "Intermediate .tar file should be cleaned up");
        }

        #endregion

        #region Conflict Tests

        [TestMethod]
        public async Task ExtractTarGz_WithExistingTarFile_ShouldNotConflict()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar.gz");
            await CreateTarGzArchive(archivePath);

            // Create a pre-existing .tar file with different content
            var existingTarPath = Path.Combine(_extractDir, "test.tar");
            File.WriteAllText(existingTarPath, "This is a pre-existing tar file that should not be affected");

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            // Verify extraction worked
            VerifyExtractedFiles(_extractDir);

            // Verify the pre-existing .tar file is unchanged (since we use temp directory)
            Assert.IsTrue(File.Exists(existingTarPath), "Pre-existing .tar file should still exist");
            Assert.AreEqual("This is a pre-existing tar file that should not be affected",
                File.ReadAllText(existingTarPath));
        }

        [TestMethod]
        public async Task ExtractMultipleTarGz_Concurrently_ShouldNotConflict()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            // Create multiple .tar.gz archives
            var archivePaths = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                var archivePath = Path.Combine(_archiveDir, $"test{i}.tar.gz");
                await CreateTarGzArchive(archivePath);
                archivePaths.Add(archivePath);
            }

            // Create separate extract directories for each
            var extractDirs = archivePaths.Select((_, i) =>
            {
                var dir = Path.Combine(_testDir, $"extract{i + 1}");
                Directory.CreateDirectory(dir);
                return dir;
            }).ToList();

            // Copy archives to their extract directories
            for (int i = 0; i < archivePaths.Count; i++)
            {
                var destPath = Path.Combine(extractDirs[i], Path.GetFileName(archivePaths[i]));
                File.Copy(archivePaths[i], destPath);
                archivePaths[i] = destPath;
            }

            // Extract concurrently
            var tasks = archivePaths.Select(path =>
                _service.ExtractToCurrentDirectory(path, true, CancellationToken.None));

            await Task.WhenAll(tasks);

            // Verify all extractions succeeded
            foreach (var extractDir in extractDirs)
            {
                VerifyExtractedFiles(extractDir);
            }
        }

        #endregion

        #region Overwrite Tests

        [TestMethod]
        public async Task ExtractZip_WithOverwrite_ShouldOverwriteExisting()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.zip");
            await CreateArchive(archivePath, "zip");

            // Create a pre-existing file with different content
            var existingFile = Path.Combine(_extractDir, "file1.txt");
            File.WriteAllText(existingFile, "Old content");

            await _service.ExtractToCurrentDirectory(archivePath, overwrite: true, CancellationToken.None);

            // Verify file was overwritten
            Assert.AreEqual("Content of file 1", File.ReadAllText(existingFile));
        }

        [TestMethod]
        public async Task ExtractTarGz_WithOverwrite_ShouldOverwriteExisting()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            var archivePath = Path.Combine(_extractDir, "test.tar.gz");
            await CreateTarGzArchive(archivePath);

            // Create a pre-existing file with different content
            var existingFile = Path.Combine(_extractDir, "file1.txt");
            File.WriteAllText(existingFile, "Old content");

            await _service.ExtractToCurrentDirectory(archivePath, overwrite: true, CancellationToken.None);

            // Verify file was overwritten
            Assert.AreEqual("Content of file 1", File.ReadAllText(existingFile));
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public async Task ExtractTarGz_WithSpecialCharactersInPath_ShouldWork()
        {
            SkipIf7zNotAvailable();
            CreateTestSourceFiles();

            // Create directory with spaces and special characters
            var specialDir = Path.Combine(_testDir, "special dir (test)");
            Directory.CreateDirectory(specialDir);

            var archivePath = Path.Combine(specialDir, "test archive.tar.gz");
            await CreateTarGzArchive(archivePath);

            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);

            // Verify extraction worked
            Assert.IsTrue(File.Exists(Path.Combine(specialDir, "file1.txt")));
        }

        [TestMethod]
        public async Task ExtractEmptyArchive_ShouldNotThrow()
        {
            SkipIf7zNotAvailable();

            // Create an empty directory and archive it
            var emptyDir = Path.Combine(_testDir, "empty");
            Directory.CreateDirectory(emptyDir);

            var archivePath = Path.Combine(_extractDir, "empty.zip");
            await Cli.Wrap(_sevenZipPath!)
                .WithArguments(new[] { "a", archivePath, emptyDir }, true)
                .ExecuteAsync();

            // Should not throw
            await _service.ExtractToCurrentDirectory(archivePath, true, CancellationToken.None);
        }

        #endregion
    }
}
