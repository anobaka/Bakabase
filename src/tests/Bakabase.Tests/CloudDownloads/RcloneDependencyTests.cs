using System.IO.Compression;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneDependencyTests
{
    [TestMethod]
    public void AllSixDeclaredTargetsHaveExactlyOnePinnedOfficialAsset()
    {
        Assert.AreEqual(6, RcloneRelease.Assets.Count);
        foreach (var target in DependentComponentPlatforms.Rclone)
        {
            var asset = RcloneRelease.Select(target.Os, target.Architecture);
            Assert.IsTrue(asset.Url.StartsWith("https://github.com/rclone/rclone/releases/download/v1.75.2/"));
            Assert.AreEqual(64, asset.Sha256.Length);
            Assert.IsTrue(asset.Sha256.All(Uri.IsHexDigit));
        }
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => RcloneRelease.Select("windows", "x86"));
    }

    [DataTestMethod]
    [DataRow("rclone v1.75.2\n- os/version: macOS 26.0\n", "1.75.2")]
    [DataRow("rclone v1.76.0\r\n", "1.76.0")]
    [DataRow("rclone v1.75.0\n", null)]
    [DataRow("rclone v1.75.1\n", null)]
    [DataRow("rclone v1.75.2-DEV\n", null)]
    [DataRow("garbage rclone v1.75.2\n", null)]
    public void DiscoveryRejectsVulnerableAndUnrecognizedBuilds(string output, string? expected) =>
        Assert.AreEqual(expected, RcloneDiscoverer.ReadVersion(output));

    [TestMethod]
    public async Task InstallerVerifiesBeforeExtractingAndRetainsReadmeLicense()
    {
        var root = Path.Combine(Path.GetTempPath(), "rclone-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = await Fixture(root, "rclone", "README.txt");
            var original = RcloneRelease.Select("macos", "arm64");
            var output = Path.Combine(root, "good");
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RcloneRelease.ExtractVerifiedAsync(archive, output, original, default));
            Assert.IsFalse(Directory.Exists(output), "No files are extracted before SHA verification.");
            var asset = original with { Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))) };
            await RcloneRelease.ExtractVerifiedAsync(archive, output, asset, default);
            Assert.AreEqual("fixture", await File.ReadAllTextAsync(Path.Combine(output, "rclone")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "README.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [DataTestMethod]
    [DataRow("../escaped")]
    [DataRow("rclone-v1.75.2-osx-arm64/../../escaped")]
    [DataRow("rclone-v1.75.2-osx-arm64/..\\escaped")]
    public async Task InstallerRejectsUnsafeArchivePaths(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "rclone-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "release.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry(name);
            var asset = RcloneRelease.Select("macos", "arm64") with
                { Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))) };
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                RcloneRelease.ExtractVerifiedAsync(archive, Path.Combine(root, "output"), asset, default));
            Assert.IsFalse(File.Exists(Path.Combine(root, "escaped")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ManagedExecutableIsDiscoveredWithItsActualVersion()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix executable fixture.");
        var root = Path.Combine(Path.GetTempPath(), "rclone-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var binary = Path.Combine(root, "rclone");
            await File.WriteAllTextAsync(binary, "#!/bin/sh\n[ \"$1\" = version ] || exit 2\nprintf 'rclone v1.75.2\\n'\n");
            File.SetUnixFileMode(binary, (UnixFileMode)0x1c0);
            var found = await new RcloneDiscoverer(NullLoggerFactory.Instance).TryDiscoverAt(root, default);
            Assert.AreEqual("1.75.2", found?.Version);
            Assert.AreEqual(root, found?.Location);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<string> Fixture(string root, params string[] entries)
    {
        var archive = Path.Combine(root, "release.zip");
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        foreach (var name in entries)
        {
            var entry = zip.CreateEntry("rclone-v1.75.2-osx-arm64/" + name);
            await using var writer = new StreamWriter(entry.Open()); await writer.WriteAsync("fixture");
        }
        return archive;
    }
}
