using Bakabase.InsideWorld.Business.Components.Compression;
using CliWrap;

namespace Bakabase.Tests;

[TestClass]
public sealed class ArchiveExtractionDirectoryTests
{
    private string root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "archive-group-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public async Task RealSplitSevenZipExtractsUnderWholeGroupNameAndKeepsItsInputs()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH.");
        var bytes = new byte[4500]; new Random(17).NextBytes(bytes);
        var input = Path.Combine(root, "payload.bin"); await File.WriteAllBytesAsync(input, bytes);
        await Cli.Wrap(executable!).WithArguments(["a", "-mx=0", "-v1k", Path.Combine(root, "edition.v1.7z"), input], true).ExecuteAsync();
        var files = Directory.GetFiles(root, "edition.v1.7z.*").Order(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(files.Length > 1);
        var result = await new ArchiveExtractionService(new TestableCompressedFileService(executable!))
            .ExtractAsync(new(files, root), null, default);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(Path.Combine(root, "edition.v1"), result.TargetDirectory);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(result.TargetDirectory, "payload.bin")));
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task StandalonePartWordIsNotRemovedFromTheOutputDirectory()
    {
        var service = await Stub("zip");
        var file = Path.Combine(root, "story.part1.zip"); await File.WriteAllTextAsync(file, "synthetic");
        var result = await service.ExtractAsync(new([file], root), null, default);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(Path.Combine(root, "story.part1"), result.TargetDirectory);
        Assert.IsTrue(File.Exists(file));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConfirmedSfxUsesTheGroupNameAndNeverRemovesUnselectedNeighbors(bool remove)
    {
        var service = await Stub("Rar5");
        var entry = Path.Combine(root, "edition.part1.exe"); await File.WriteAllTextAsync(entry, "synthetic");
        var second = Path.Combine(root, "edition.part2.rar"); await File.WriteAllTextAsync(second, "synthetic");
        var unselected = Path.Combine(root, "edition.part3.rar"); await File.WriteAllTextAsync(unselected, "not authorized");
        var result = await service.ExtractAsync(new([entry, second], root, DeleteAfterDecompression: remove), null, default);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(Path.Combine(root, "edition"), result.TargetDirectory);
        Assert.AreEqual("ok", await File.ReadAllTextAsync(Path.Combine(result.TargetDirectory, "payload.txt")));
        Assert.AreEqual("not authorized", await File.ReadAllTextAsync(unselected));
        Assert.AreEqual(!remove, File.Exists(entry)); Assert.AreEqual(!remove, File.Exists(second));
    }

    [DataTestMethod]
    [DataRow("gap")]
    [DataRow("ambiguous")]
    [DataRow("format-mismatch")]
    [DataRow("unrelated-selection")]
    public async Task InvalidWholeSelectionCannotDeleteInputsOrStartExtraction(string kind)
    {
        var service = await Stub(kind == "format-mismatch" ? "zip" : "Rar5");
        var names = kind switch
        {
            "gap" => new[] {"edition.part1.exe", "edition.part3.rar"},
            "ambiguous" => ["edition.part1.exe", "edition.part1.rar", "edition.part2.rar"],
            "unrelated-selection" => ["edition.zip", "important.key"],
            _ => ["edition.part1.exe", "edition.part2.rar"]
        };
        var files = names.Select(name => Path.Combine(root, name)).ToArray();
        foreach (var file in files) await File.WriteAllTextAsync(file, "keep");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExtractAsync(
            new(files, root, DeleteAfterDecompression: true), null, default));
        Assert.IsTrue(files.All(File.Exists));
        Assert.IsFalse(File.Exists(Path.Combine(root, "tool.extracted")));
    }

    private async Task<ArchiveExtractionService> Stub(string format)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Synthetic archive tool requires a Unix shell.");
        var tool = Path.Combine(root, "tool");
        await File.WriteAllTextAsync(tool, "#!/bin/sh\n" + $$"""
            case "$1" in
            l) printf '%s\n' '--' 'Type = {{format}}' '----------' 'Path = payload.txt' 'Folder = -' 'Size = 2' '';;
            x) touch "$0.extracted"
               for arg do case "$arg" in -o*) dest=${arg#-o};; esac; done
               mkdir -p "$dest"; printf 'ok' > "$dest/payload.txt";;
            esac
            """ + "\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new(new TestableCompressedFileService(tool));
    }
}
