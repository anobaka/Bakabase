using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.MegaCmd;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.PikPakCli;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ManagedCliDependencyTests
{
    [TestMethod]
    public void OfficialAssetsCoverEveryAdvertisedAutomaticTarget()
    {
        foreach (var target in DependentComponentPlatforms.PikPakCli.Where(t => t.CanAutoInstall))
        {
            var asset = PikPakCliRelease.Select(target.Os, target.Architecture);
            Assert.IsTrue(asset.Url.StartsWith("https://download.mypikpak.com/cli/release/v0.6.0/"));
            Assert.AreEqual(64, asset.Sha256.Length);
            Assert.IsTrue(asset.Sha256.All(Uri.IsHexDigit));
        }
        foreach (var target in DependentComponentPlatforms.MegaCmd.Where(t => t.CanAutoInstall))
        {
            Assert.AreEqual("linux", target.Os);
            Assert.AreEqual("glibc", target.Runtime);
            Assert.AreEqual(64, MegaCmdRelease.Select(target.Architecture).Sha256.Length);
        }
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => PikPakCliRelease.Select("linux", "x86"));
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => MegaCmdRelease.Select("x86"));
    }

    [DataTestMethod]
    [DataRow("pikpak version v0.6.0\n", "0.6.0")]
    [DataRow("pikpak version v0.6.0\r\n", "0.6.0")]
    [DataRow("pikpak version v0.6.0-dev\n", null)]
    [DataRow("pikpak version v0.5.0\n", null)]
    [DataRow("pikpak version v0.7.0\n", null)]
    [DataRow("unrecognized pikpak version v0.6.0\n", null)]
    public void PikPakDiscoveryOnlyAcceptsReviewedCliProtocol(string output, string? expected) =>
        Assert.AreEqual(expected, PikPakCliDiscoverer.ReadVersion(output));

    [TestMethod]
    public async Task PikPakDiscoveryOnlyRequestsVersionAndSuppressesBackgroundActions()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix fixture.");
        var root = NewDirectory();
        try
        {
            var binary = Path.Combine(root, "pikpak");
            await File.WriteAllTextAsync(binary,
                "#!/bin/sh\n[ \"$1\" = --version ] || exit 2\n[ \"$PIKPAK_NO_LOG\" = 1 ] || exit 3\n[ \"$PIKPAK_NO_SKILL_SYNC\" = 1 ] || exit 4\n[ \"$PIKPAK_NO_UPDATE_NOTIFIER\" = 1 ] || exit 5\nprintf 'pikpak version v0.6.0\\n'\n");
            File.SetUnixFileMode(binary, (UnixFileMode)0x1c0);
            Assert.AreEqual("0.6.0", (await new PikPakCliDiscoverer(NullLoggerFactory.Instance).TryDiscoverAt(root, default))?.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task WrongChecksumsNeverExtractOrRunPayloads()
    {
        var root = NewDirectory();
        try
        {
            var file = Path.Combine(root, "release"); await File.WriteAllTextAsync(file, "untrusted");
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PikPakCliRelease.VerifyAsync(file,
                PikPakCliRelease.Select("linux", "arm64"), default));
            var destination = Path.Combine(root, "extract");
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => MegaCmdRelease.ExtractVerifiedAsync(file,
                destination, MegaCmdRelease.Select("arm64"), default));
            Assert.IsFalse(Directory.Exists(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MegaExtractsOnlyDeclaredRegularFilesAndCannotAutostartRenamedServer()
    {
        var root = NewDirectory();
        try
        {
            using var tar = TarFixture(extra: "../../escape");
            await MegaCmdRelease.ExtractTarAsync(tar, root, default);
            Assert.IsTrue(File.Exists(Path.Combine(root, MegaCmdRelease.ServerFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "mega-cmd-server")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "copyright")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "lib", "libavformat.so.61")));
            Assert.AreEqual(MegaCmdRelease.Files.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MegaRejectsAWhitelistedSymlinkAndDuplicateBinary()
    {
        foreach (var link in new[] { true, false })
        {
            var root = NewDirectory();
            try
            {
                using var tar = TarFixture(unsafeDuplicate: true, link: link);
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => MegaCmdRelease.ExtractTarAsync(tar, root, default));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    [TestMethod]
    public async Task MegaReceiptDetectsModifiedAndMissingBinariesWithoutLaunchingAnything()
    {
        var root = NewDirectory();
        try
        {
            using var tar = TarFixture();
            await MegaCmdRelease.ExtractTarAsync(tar, root, default);
            var asset = MegaCmdRelease.Select("arm64");
            var hashes = new Dictionary<string, string>();
            foreach (var relative in MegaCmdRelease.Files.Values)
                hashes[relative] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, relative))));
            await File.WriteAllTextAsync(Path.Combine(root, MegaCmdRelease.ReceiptName), JsonConvert.SerializeObject(
                new MegaCmdRelease.Receipt(MegaCmdRelease.Version, asset.Architecture, asset.Sha256, hashes)));
            Assert.IsTrue(await MegaCmdRelease.VerifyInstallationAsync(root, asset, default));
            await File.AppendAllTextAsync(Path.Combine(root, "mega-exec"), "changed");
            Assert.IsFalse(await MegaCmdRelease.VerifyInstallationAsync(root, asset, default));
            File.Delete(Path.Combine(root, "mega-exec"));
            Assert.IsFalse(await MegaCmdRelease.VerifyInstallationAsync(root, asset, default));
        }
        finally { Directory.Delete(root, true); }
    }

    private static MemoryStream TarFixture(string? extra = null, bool unsafeDuplicate = false, bool link = false)
    {
        var data = new MemoryStream();
        using (var writer = new TarWriter(data, leaveOpen: true))
        {
            foreach (var path in MegaCmdRelease.Files.Keys)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./" + path)
                    { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("fixture")) });
            if (extra != null) writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, extra)
                { DataStream = new MemoryStream([1]) });
            if (unsafeDuplicate) writer.WriteEntry(link
                ? new PaxTarEntry(TarEntryType.SymbolicLink, "usr/bin/mega-exec") { LinkName = "../../outside" }
                : new PaxTarEntry(TarEntryType.RegularFile, "usr/bin/mega-exec") { DataStream = new MemoryStream([1]) });
        }
        data.Position = 0;
        return data;
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "managed-cli-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
    }
}
