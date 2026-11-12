using System.IO.Compression;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Compression;
using CliWrap;

namespace Bakabase.Tests;

[TestClass]
public sealed class ManualArchiveExtractionTests
{
    private string root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "manual-archive-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public async Task EncryptedPrefixedZipUsesExplicitPasswordAndWrongPasswordDoesNotBecomeSuccessfulRetry()
    {
        var (service, source) = await Encrypted();
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var output = Path.Combine(root, "output");
        var wrong = await service.ExtractForFileExplorerAsync(source, output, "incorrect-fixture-pass", null, default);
        Assert.AreNotEqual(0, wrong.ExitCode);
        if (File.Exists(Path.Combine(output, "payload.txt")))
        {
            // Failed output is preserved. A retry cannot silently skip it and claim completion.
            await Assert.ThrowsAsync<IOException>(() => service.ExtractForFileExplorerAsync(source, output, "fixture-pass", null, default));
            Directory.Delete(output, true); // The user explicitly removes this synthetic failed output before retrying.
        }
        var correct = await service.ExtractForFileExplorerAsync(source, output, "fixture-pass", null, default);
        Assert.AreEqual(0, correct.ExitCode, correct.StandardError);
        Assert.AreEqual("synthetic encrypted content", await File.ReadAllTextAsync(Path.Combine(output, "payload.txt")));
        CollectionAssert.AreEqual(before, SHA256.HashData(await File.ReadAllBytesAsync(source)));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("user file must survive")]
    public async Task ExistingEmptyOrNonemptyOutputCannotBeOverwrittenOrReportedComplete(string existing)
    {
        var (service, source) = await Encrypted();
        var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        var file = Path.Combine(output, "payload.txt"); await File.WriteAllTextAsync(file, existing);
        await Assert.ThrowsAsync<IOException>(() => service.ExtractForFileExplorerAsync(source, output, "fixture-pass", null, default));
        Assert.AreEqual(existing, await File.ReadAllTextAsync(file)); Assert.IsTrue(File.Exists(source));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyAndDirectoryOnlyArchivesRemainValid(bool directory)
    {
        var source = Path.Combine(root, "empty.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            if (directory) zip.CreateEntry("kept-empty/");
        var output = Path.Combine(root, "output");
        if (directory) Directory.CreateDirectory(Path.Combine(output, "kept-empty"));
        var result = await new TestableCompressedFileService(Executable()).ExtractForFileExplorerAsync(source, output, null, null, default);
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        if (directory) Assert.IsTrue(Directory.Exists(Path.Combine(output, "kept-empty")));
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public async Task ToolExitZeroWithAnEmptyReplacementForDeclaredContentIsFailure()
    {
        var service = await Stub("""
            case "$1" in
            l) printf '%s\n' '--' 'Type = zip' '----------' 'Path = payload.txt' 'Folder = -' 'Size = 27' '';;
            x) for arg do case "$arg" in -o*) dest=${arg#-o};; esac; done
               mkdir -p "$dest"; : > "$dest/payload.txt";;
            esac
            """);
        var source = Path.Combine(root, "source.bin"); await File.WriteAllTextAsync(source, "inert source");
        var output = Path.Combine(root, "output");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ExtractForFileExplorerAsync(source, output, "fixture", null, default));
        StringAssert.Contains(error.Message, "unexpected length");
        Assert.AreEqual(0L, new FileInfo(Path.Combine(output, "payload.txt")).Length);
        Assert.AreEqual("inert source", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task UnverifiableCatalogDoesNotStartExtraction()
    {
        var service = await Stub("""
            case "$1" in
            l) printf '%s\n' 'unrecognized list';;
            x) touch "$0.extracted";;
            esac
            """);
        var source = Path.Combine(root, "source.bin"); await File.WriteAllTextAsync(source, "inert source");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExtractForFileExplorerAsync(source, Path.Combine(root, "output"), null, null, default));
        Assert.IsFalse(File.Exists(Path.Combine(root, "tool.extracted")));
    }

    [TestMethod]
    public void CatalogRejectsLinksTraversalMissingLengthsAndNameCollisions()
    {
        foreach (var member in new[] {"Path = payload\nSize = 1\nSymbolic Link = elsewhere", "Path = payload", "Path = payload\nSize = -1"})
            Assert.ThrowsExactly<InvalidDataException>(() => CompressedFileService.ParseManualExtractionCatalog("----------\n" + member + "\n"));
        foreach (var name in new[] {"../outside", "/outside", "C:/outside", "parent/../outside"})
            Assert.ThrowsExactly<InvalidDataException>(() => CompressedFileService.ValidateManualExtractionTargets(root,
                [new(name, 0)], true));
        Assert.ThrowsExactly<InvalidDataException>(() => CompressedFileService.ValidateManualExtractionTargets(root,
            [new("file", 0), new("file/child", 0)], true));
        Assert.ThrowsExactly<InvalidDataException>(() => CompressedFileService.ValidateManualExtractionTargets(root,
            [new("file", 0), new("FILE", 0)], true));
    }

    [TestMethod]
    public void LinkedOutputAncestorsAreNeverFollowed()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Symlink fixture requires Unix.");
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(output, "link"), outside);
        Assert.ThrowsExactly<InvalidDataException>(() => CompressedFileService.ValidateManualExtractionTargets(output,
            [new("link/file", 4)], true));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(outside).Length);
    }

    private async Task<(CompressedFileService Service, string Source)> Encrypted()
    {
        var executable = Executable();
        var payload = Path.Combine(root, "payload.txt"); await File.WriteAllTextAsync(payload, "synthetic encrypted content");
        var ordinary = Path.Combine(root, "ordinary.zip");
        await Cli.Wrap(executable).WithArguments(["a", "-tzip", "-mem=ZipCrypto", "-pfixture-pass", ordinary, payload], true).ExecuteAsync();
        var zip = ZipStructureFixture.Zip64WithEmptyCentralExtra(await File.ReadAllBytesAsync(ordinary));
        var source = await ZipStructureFixture.Write(Path.Combine(root, "selected.png"), 608331, zip);
        await using (var file = new FileStream(source, FileMode.Open, FileAccess.Write))
            await file.WriteAsync(Convert.FromHexString("89504e470d0a1a0a"));
        return (new TestableCompressedFileService(executable), source);
    }
    private async Task<CompressedFileService> Stub(string body)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Shell fixture requires Unix.");
        var path = Path.Combine(root, "tool"); await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new TestableCompressedFileService(path);
    }
    private static string Executable()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive-tool fixture.");
        return path!;
    }
}
