using System.IO.Compression;
using System.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipNameFailureDiagnosticsTests
{
    [DataTestMethod]
    [DataRow(2, "Type = zip", "ERROR: Cannot open output file : errno=2 : No such file or directory : /output/\ufffd/name.jpg", true)]
    [DataRow(0, "Type = zip", "ERROR: Cannot open output file : errno=2 : No such file or directory : /output/\ufffd/name.jpg", false)]
    [DataRow(2, "Type = 7z", "ERROR: Cannot open output file : errno=2 : No such file or directory : /output/\ufffd/name.jpg", false)]
    [DataRow(2, "Type = zip", "ERROR: Cannot open output file : errno=2 : No such file or directory : /missing/name.jpg", false)]
    [DataRow(2, "Type = zip", "ERROR: Cannot open output file : errno=28 : No space left on device : /output/\ufffd/name.jpg", false)]
    [DataRow(2, "Type = zip", "ERROR: Cannot open output file : errno=13 : Permission denied : /output/\ufffd/name.jpg", false)]
    [DataRow(2, "Type = zip", "T ERROR: Cannot open output file : errno=2 : No such file or directory : /output/\ufffd/name.jpg", false)]
    public void OnlyFailedZipWritesWithUndecodableNamesAreClassified(int exitCode, string stdout, string stderr, bool expected)
    {
        Assert.AreEqual(expected, CompressedFileService.HasZipNameDecodingFailure(new()
            {ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr}));
    }

    [TestMethod]
    public async Task ToolNameFailureBecomesRecoverableAndNeverDeletesInput()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The synthetic archive tool is a Unix shell script.");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "zip-name-diagnostic-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var tool = Path.Combine(root, "tool");
            await File.WriteAllTextAsync(tool, """
                #!/bin/sh
                case "$1" in
                t) printf '%s\n' 'Type = zip' 'Everything is Ok'; exit 0;;
                x) printf '%s\n' 'Type = zip'
                   printf '%s\n' 'ERROR: Cannot open output file : errno=2 : No such file or directory : /output/�/name.jpg' >&2
                   exit 2;;
                esac
                """);
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var source = Path.Combine(root, "source.zip"); await File.WriteAllTextAsync(source, "inert fixture archive");
            var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            var extraction = new ArchiveExtractionService(new TestableCompressedFileService(tool));
            var error = await Assert.ThrowsExactlyAsync<ArchiveNameEncodingUnsupportedException>(() => extraction.ExtractAsync(
                new([source], output, DecompressToNewFolder: false, DeleteAfterDecompression: true), null, default));
            StringAssert.Contains(error.Message, "filename encoding");
            StringAssert.Contains(error.Message, "errno=2");
            Assert.AreEqual("inert fixture archive", await File.ReadAllTextAsync(source));

            var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
            var selected = Path.Combine(input, "source.zip"); File.Copy(source, selected);
            var result = await new FileProcessingPlanExecutor(extraction).ExecuteAsync(new()
                {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]},
                input, [selected], Path.Combine(root, "state"));
            Assert.IsFalse(result.Completed);
            Assert.IsFalse(result.NeedsPassword);
            Assert.AreEqual("ArchiveNameEncodingUnsupported", result.FailureCode);
            Assert.IsTrue(File.Exists(selected));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ConfirmedEncodingUsesExistingBoundedReaderAndPreservesSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "zip-name-reader-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var source = Path.Combine(root, "known-cp932.zip");
            using (var stream = File.Create(source))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.GetEncoding(932)))
            {
                using var member = zip.CreateEntry("添付ソフト/画像.txt").Open();
                member.Write("synthetic payload"u8);
            }
            var before = await File.ReadAllBytesAsync(source);
            var service = new TestableCompressedFileService(Path.Combine(root, "tool-must-not-be-invoked"));
            var opening = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis);
            var entries = await service.GetCompressFileEntries(source, default, opening);
            Assert.AreEqual("添付ソフト/画像.txt", entries.Data!.Single().Path);
            var output = Path.Combine(root, "output");
            var result = await service.ExtractWithProgress(source, output, null, true,
                CompressedFileService.OverwriteMode.None, CompressedFileService.ProgressOutputTarget.StandardOutput,
                null, null, null, default, opening);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual("synthetic payload", await File.ReadAllTextAsync(Path.Combine(output, "添付ソフト", "画像.txt")));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(root, true); }
    }
}
