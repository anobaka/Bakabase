using System.IO.Compression;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Compression;
using CliWrap;

namespace Bakabase.Tests;

[TestClass]
public sealed class ArchiveExpandedSizeTests
{
    private string root = null!;

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "archive-byte-budget-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, recursive: true);

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealZipListsEveryMemberWithoutCreatingExtractionOutput(bool empty)
    {
        var source = Path.Combine(root, "selected.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            zip.CreateEntry("folder/");
            if (!empty)
            {
                await using (var member = zip.CreateEntry("folder/data").Open())
                    await member.WriteAsync(new byte[97000]);
                await using var other = zip.CreateEntry("second").Open();
                await other.WriteAsync(new byte[321]);
            }
        }
        var destination = Path.Combine(root, "new-output");
        var archives = new ArchiveExtractionService(new TestableCompressedFileService(Executable()));
        Assert.AreEqual(empty ? 0L : 97321L, await archives.TryGetExpandedBytesAsync(
            new([source], destination), default));
        Assert.IsFalse(Directory.Exists(destination));
    }

    [TestMethod]
    public async Task EncryptedHeaderAcrossVolumesListsWithTheAuthorizedPassword()
    {
        var tool = Executable();
        var first = Path.Combine(root, "one.bin");
        var second = Path.Combine(root, "two.bin");
        await File.WriteAllBytesAsync(first, RandomNumberGenerator.GetBytes(52000));
        await File.WriteAllBytesAsync(second, RandomNumberGenerator.GetBytes(41000));
        var archive = Path.Combine(root, "joined.7z");
        await Cli.Wrap(tool).WithArguments(["a", "-t7z", "-mx=0", "-v32k", "-mhe=on", "-pfixture-pass",
            archive, first, second], true).ExecuteAsync();
        var parts = Directory.GetFiles(root, "joined.7z.*").Order().ToArray();
        Assert.IsTrue(parts.Length > 1);
        var destination = Path.Combine(root, "new-output");
        var request = new ArchiveExtractionRequest(parts, destination, "fixture-pass", OpenOptions: ArchiveOpenOptions.VerifiedSevenZipVolumes);
        var archives = new ArchiveExtractionService(new TestableCompressedFileService(tool));
        Assert.AreEqual(93000L, await archives.TryGetExpandedBytesAsync(request, default));
        Assert.IsNull(await archives.TryGetExpandedBytesAsync(request with {Password = "incorrect"}, default));
        Assert.IsFalse(Directory.Exists(destination));
    }

    [DataTestMethod]
    [DataRow("Path = one", "0")]
    [DataRow("Path = one\nSize = -1", "0")]
    [DataRow("Path = one\nSize = 1", "2")]
    [DataRow("Path = one\nSize = 9223372036854775807\n\nPath = two\nSize = 1", "0")]
    public async Task IncompleteFailedOrOverflowingCatalogIsUnknown(string rows, string exit)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The list-only executable fixture requires a Unix shell.");
        var executable = Path.Combine(root, "list-only");
        await File.WriteAllTextAsync(executable,
            "#!/bin/sh\nprintf '%s\\n' \"$1\" >> \"$0.calls\"\ncat <<'CATALOG'\n----------\n" + rows + "\nCATALOG\nexit " + exit + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var source = Path.Combine(root, "selected.7z");
        await File.WriteAllTextAsync(source, "fixture");
        var service = new ArchiveExtractionService(new TestableCompressedFileService(executable));
        Assert.IsNull(await service.TryGetExpandedBytesAsync(new([source], root), default));
        CollectionAssert.AreEqual(new[] {"l"}, await File.ReadAllLinesAsync(executable + ".calls"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.TryGetExpandedBytesAsync(new([source], root), cancel.Token));
        CollectionAssert.AreEqual(new[] {"l"}, await File.ReadAllLinesAsync(executable + ".calls"));
    }

    private static string Executable()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (!File.Exists(path)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH to the archive executable.");
        return path!;
    }
}
