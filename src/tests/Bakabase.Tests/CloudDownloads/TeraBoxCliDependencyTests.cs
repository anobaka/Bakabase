using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class TeraBoxCliDependencyTests
{
    [TestMethod]
    public void PinnedManifestCoversExactlyAdvertisedTargets()
    {
        Assert.AreEqual(5, TeraBoxCliRelease.Assets.Count);
        Assert.AreEqual(5, DependentComponentPlatforms.TeraBoxCli.Count);
        foreach (var target in DependentComponentPlatforms.TeraBoxCli)
        {
            var asset = TeraBoxCliRelease.Select(target.Os, target.Architecture);
            Assert.IsTrue(target.CanAutoInstall);
            Assert.IsTrue(asset.Url.StartsWith("https://s5.teraboxcdn.com/ai-terabox/terabox-cli/1.1.0/", StringComparison.Ordinal));
            Assert.AreEqual(64, asset.Sha256.Length);
            Assert.IsTrue(asset.Sha256.All(Uri.IsHexDigit));
        }
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => TeraBoxCliRelease.Select("windows", "arm64"));
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => TeraBoxCliRelease.Select("linux", "x86"));
    }

    [DataTestMethod]
    [DataRow("terabox 1.1.0\n", "1.1.0")]
    [DataRow("terabox 1.1.0\r\n", "1.1.0")]
    [DataRow("terabox 1.1.0", "1.1.0")]
    [DataRow("terabox 1.1.0-beta\n", null)]
    [DataRow("terabox 1.2.0\n", null)]
    [DataRow("untrusted terabox 1.1.0\n", null)]
    [DataRow("terabox 1.1.0\nadditional output", null)]
    public void DiscoveryAcceptsOnlyReviewedProtocol(string output, string? expected) =>
        Assert.AreEqual(expected, TeraBoxCliDiscoverer.ReadVersion(output));

    [TestMethod]
    public async Task DiscoveryRequestsOnlyVersionWithUpdatesDisabled()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix fixture.");
        var root = NewDirectory();
        try
        {
            var binary = Path.Combine(root, "terabox");
            await File.WriteAllTextAsync(binary,
                "#!/bin/sh\n[ \"$#\" = 2 ] || exit 2\n[ \"$1\" = --no-check-update ] || exit 3\n[ \"$2\" = --version ] || exit 4\nprintf 'terabox 1.1.0\\n'\n");
            File.SetUnixFileMode(binary, (UnixFileMode)0x1c0);
            Assert.AreEqual("1.1.0", (await new TeraBoxCliDiscoverer(NullLoggerFactory.Instance)
                .TryDiscoverAt(root, default))?.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ChecksumMismatchNeverCreatesExtractionDestination()
    {
        var root = NewDirectory();
        try
        {
            var archive = Path.Combine(root, "untrusted.tar.gz");
            await File.WriteAllTextAsync(archive, "untrusted");
            var destination = Path.Combine(root, "extracted");
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => TeraBoxCliRelease.ExtractVerifiedAsync(
                archive, destination, TeraBoxCliRelease.Select("linux", "arm64"), default));
            Assert.IsFalse(Directory.Exists(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task TarAndZipExtractOnlyBinaryFromReviewedLayout()
    {
        foreach (var zip in new[] { false, true })
        {
            var root = NewDirectory();
            try
            {
                using var archive = zip ? ZipFixture() : TarFixture();
                if (zip) await TeraBoxCliRelease.ExtractZipAsync(archive, root, default);
                else await TeraBoxCliRelease.ExtractTarAsync(archive, root, default);
                var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                CollectionAssert.AreEqual(new[] { Path.Combine(root, zip ? "terabox.exe" : "terabox") }, files);
                Assert.AreEqual("binary", await File.ReadAllTextAsync(files[0]));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    [DataTestMethod]
    [DataRow("traversal")]
    [DataRow("symlink")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("oversized")]
    public async Task TarRejectsUnsafeOrIncompleteArchive(string unsafeCase)
    {
        var root = NewDirectory();
        try
        {
            using var archive = TarFixture(unsafeCase);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => TeraBoxCliRelease.ExtractTarAsync(archive, root, default));
        }
        finally { Directory.Delete(root, true); }
    }

    [DataTestMethod]
    [DataRow("traversal")]
    [DataRow("symlink")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    public async Task ZipRejectsUnsafeOrIncompleteArchive(string unsafeCase)
    {
        var root = NewDirectory();
        try
        {
            using var archive = ZipFixture(unsafeCase);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => TeraBoxCliRelease.ExtractZipAsync(archive, root, default));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CancellationStopsExtractionBeforeCreatingBinary()
    {
        var root = NewDirectory();
        try
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            using var archive = TarFixture();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => TeraBoxCliRelease.ExtractTarAsync(archive, root, cancellation.Token));
            Assert.AreEqual(0, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private static MemoryStream TarFixture(string? unsafeCase = null)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "terabox/"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "terabox/VERSION")
                { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(unsafeCase == "oversized" ? new string('x', 33) : "1.1.0\n")) });
            if (unsafeCase != "missing")
            {
                var path = unsafeCase == "traversal" ? "../escape" : "terabox/terabox";
                writer.WriteEntry(unsafeCase == "symlink"
                    ? new PaxTarEntry(TarEntryType.SymbolicLink, path) { LinkName = "../../outside" }
                    : new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream("binary"u8.ToArray()) });
                if (unsafeCase == "duplicate") writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                    { DataStream = new MemoryStream("duplicate"u8.ToArray()) });
            }
        }
        stream.Position = 0; return stream;
    }

    private static MemoryStream ZipFixture(string? unsafeCase = null)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("terabox/").ExternalAttributes = 0x4000 << 16;
            using (var version = archive.CreateEntry("terabox/VERSION").Open()) version.Write("1.1.0\n"u8);
            if (unsafeCase != "missing")
            {
                var path = unsafeCase == "traversal" ? "../escape" : "terabox/terabox.exe";
                var entry = archive.CreateEntry(path);
                if (unsafeCase == "symlink") entry.ExternalAttributes = 0xa000 << 16;
                using (var output = entry.Open()) output.Write("binary"u8);
                if (unsafeCase == "duplicate")
                    using (var duplicate = archive.CreateEntry(path).Open()) duplicate.Write("duplicate"u8);
            }
        }
        stream.Position = 0; return stream;
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "terabox-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
}
