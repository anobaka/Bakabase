using System.Formats.Tar;
using System.Runtime.InteropServices;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Verify installation with no external tar, xz or 7zz on PATH.
public sealed class SevenZipInstallationTests
{
    // USTAR + XZ fixtures contain a 0755 /bin/sh script and doc/readme.txt (0644).
    // The script prints a 7-Zip version in GoodArchive; in BadArchive it exits with an error.
    private const string GoodArchive = "/Td6WFoAAATm1rRGAgAhARYAAAB0L+Wj4Cf/ALddABuerAG6cMFdl3MTgqAqt3ShhOV5c4ZT5GPtjHRe37Wr7T+De6jMiCl2vFI5nLR4EaZtikWHZGYapRJQVd5PGKZAmHCoRGzXYMddhsAsmDqyAIbpgzw5voiKwM+k1zkn+vXfdurYQvzouDGRWB3QyL+mXx3F6rio0sT7w4xh+yXgi8Hg1tCAN6sV+O7LkE9MvBN3VePp4KDz5/gBlGD5y1M1dimGL2CrUA8JsI7TvHBkRigl1NsCqAAA7SC20nD103UAAdMBgFAAAGcLeuuxxGf7AgAAAAAEWVo=";
    private const string BadArchive = "/Td6WFoAAATm1rRGAgAhARYAAAB0L+Wj4Cf/AJhdABuerAG6cMFdl3MTgqAqt3QehInWX/JhQ82vW6TxYUgpMJ0dNcF73ojR3Fpec8YdnEswsBC6tsta++Yc2/ca2tpT5FPKXsIR6B6I/5t4J69JcTthHzmar44FsMbnaawM8z6b6bpd4sEDEqXbO2kE1GGgrC8Ijx+RmlR5bt/ZNR2xsQJEuAjsCfVa3X7P46WNF7HCSn/LH0PmAJh+0AGXbqv9AAG0AYBQAACFNIv+scRn+wIAAAAABFla";
    // This probe never exits by itself: cancellation must kill it and remove staging.
    private const string HangingArchive = "/Td6WFoAAATm1rRGAgAhARYAAAB0L+Wj4Cf/AHFdABuerAG6cMFdl3MTgqAqt3Q+hyNpn4ZgmFJRSkR2Hv9KlHaqdf2OXABsYd+hz4KUSjy0/UZMAYj/5uM06YQkzjDLS7KOemyL93q174SXEKTbZtzHwH5IMMYDPpELtJTcroFZaKKmaAR96zJ02CmtbzgAAAAAAIhdAn/CoQvzAAGNAYBQAADgYv7dscRn+wIAAAAABFla";
    private string _root = null!;
    private string? _path;
    private string Destination => Path.Combine(_root, "7z");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-sevenzip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", _root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        Directory.Delete(_root, true);
    }

    [DataTestMethod]
    [DataRow(OsPlatform.Linux, Architecture.Arm64, "7z2604-linux-arm64.tar.xz")]
    [DataRow(OsPlatform.Linux, Architecture.Arm, "7z2604-linux-arm.tar.xz")]
    [DataRow(OsPlatform.Linux, Architecture.X64, "7z2604-linux-x64.tar.xz")]
    [DataRow(OsPlatform.Osx, Architecture.Arm64, "7z2604-mac.tar.xz")]
    [DataRow(OsPlatform.Osx, Architecture.X64, "7z2604-mac.tar.xz")]
    [DataRow(OsPlatform.Windows, Architecture.Arm64, "7z2604-arm64.exe")]
    [DataRow(OsPlatform.Windows, Architecture.X64, "7z2604-x64.exe")]
    [DataRow(OsPlatform.Windows, Architecture.X86, "7z2604.exe")]
    public void ReleaseSelection_MatchesExactPlatformAndArchitecture(OsPlatform platform, Architecture arch, string expected)
    {
        string[] assets = ["7zr.exe", "7z2604-linux-arm64.tar.xz", "7z2604-arm64.exe", "7z2604-linux-arm.tar.xz",
            "7z2604-src.tar.xz", "7z2604-linux-x64.tar.xz", "7z2604-x64.exe", "7z2604.exe", "7z2604-mac.tar.xz"];
        var release = new GithubRelease { TagName = "v26.04", Assets = assets.Select(name => new GithubAsset { Name = name }).ToList() };
        Assert.AreEqual(expected, SevenZipService.SelectReleaseAsset(release, platform, arch).Name);
        release.Assets.RemoveAll(a => a.Name == expected);
        Assert.ThrowsException<NotSupportedException>(() => SevenZipService.SelectReleaseAsset(release, platform, arch));
    }

    [TestMethod]
    public async Task Upgrade_ExtractsWithoutSystemTools_ProbesAndReplacesExistingCopy()
    {
        RequireUnix();
        await ExistingInstall();
        var archive = await Download(GoodArchive);
        await SevenZipUnixInstaller.InstallAsync(archive, Destination, NullLoggerFactory.Instance, default);

        var discovered = await new SevenZipDiscoverer(NullLoggerFactory.Instance).Discover(Destination, default);
        Assert.AreEqual("26.04", discovered!.Value.Version);
        Assert.AreEqual(Destination, discovered.Value.Location);
        Assert.AreEqual((UnixFileMode)0x1ED, File.GetUnixFileMode(Path.Combine(Destination, "7zz")));
        Assert.AreEqual("portable build", await File.ReadAllTextAsync(Path.Combine(Destination, "doc/readme.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(Destination, "old-marker")));
        Assert.IsFalse(Directory.Exists(Path.Combine(Destination, "temp")));
        AssertNoWorkDirectories();
    }

    [TestMethod]
    public async Task FailedProbe_PreservesExistingBinaryAndCleansStaging()
    {
        RequireUnix();
        var original = await ExistingInstall();
        // A valid system copy must not make a broken staged download pass validation.
        File.Copy(Path.Combine(Destination, "7zz"), Path.Combine(_root, "7zz"));
        File.SetUnixFileMode(Path.Combine(_root, "7zz"), (UnixFileMode)0x1ED);
        var archive = await Download(BadArchive);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            SevenZipUnixInstaller.InstallAsync(archive, Destination, NullLoggerFactory.Instance, default));
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(Destination, "7zz")));
        Assert.AreEqual("23.01", (await new SevenZipDiscoverer(NullLoggerFactory.Instance).Discover(Destination, default))!.Value.Version);
        AssertNoWorkDirectories();
    }

    [TestMethod]
    public async Task TruncatedDownload_PreservesExistingBinary()
    {
        RequireUnix();
        var original = await ExistingInstall();
        var archive = await Download(GoodArchive);
        await File.WriteAllBytesAsync(archive, Convert.FromBase64String(GoodArchive)[..80]);
        await Assert.ThrowsAsync<Exception>(() =>
            SevenZipUnixInstaller.InstallAsync(archive, Destination, NullLoggerFactory.Instance, default));
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(Destination, "7zz")));
        AssertNoWorkDirectories();
    }

    [TestMethod]
    public async Task Cancellation_DoesNotModifyExistingInstallation()
    {
        var original = await ExistingInstall();
        var archive = await Download(GoodArchive);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SevenZipUnixInstaller.InstallAsync(archive, Destination, NullLoggerFactory.Instance, cts.Token));
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(Destination, "7zz")));
        AssertNoWorkDirectories();
    }

    [TestMethod]
    public async Task CancellationDuringProbe_StopsChildAndPreservesExistingInstallation()
    {
        RequireUnix();
        var original = await ExistingInstall();
        var archive = await Download(HangingArchive);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SevenZipUnixInstaller.InstallAsync(archive, Destination, NullLoggerFactory.Instance, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(Destination, "7zz")));
        AssertNoWorkDirectories();
    }

    [DataTestMethod]
    [DataRow("../escaped", TarEntryType.RegularFile)]
    [DataRow("link", TarEntryType.SymbolicLink)]
    [DataRow("hardlink", TarEntryType.HardLink)]
    public async Task UnsafeArchiveEntries_AreRejectedBeforeWritingOutsideStaging(string name, TarEntryType type)
    {
        using var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, leaveOpen: true))
        {
            var entry = new UstarTarEntry(type, name);
            if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream([1, 2, 3]);
            else entry.LinkName = "../escaped";
            writer.WriteEntry(entry);
        }
        archive.Position = 0;
        Directory.CreateDirectory(Destination);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            SevenZipUnixInstaller.ExtractTarAsync(archive, Destination, default));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "escaped")));
        Assert.IsEmpty(Directory.GetFileSystemEntries(Destination));
    }

    private async Task<string> ExistingInstall()
    {
        Directory.CreateDirectory(Destination);
        var path = Path.Combine(Destination, "7zz");
        const string script = "#!/bin/sh\nprintf '7-Zip 23.01 (arm64) : old version\\n'\n";
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0x1ED);
        await File.WriteAllTextAsync(Path.Combine(Destination, "old-marker"), "old install");
        return script;
    }

    private async Task<string> Download(string contents)
    {
        var temp = Path.Combine(Destination, "temp");
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "7z2604-test.tar.xz");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(contents));
        return path;
    }

    private void AssertNoWorkDirectories() => Assert.AreEqual(0,
        Directory.GetDirectories(_root).Count(path => Path.GetFileName(path).StartsWith('.')));

    private static void RequireUnix()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Portable Unix installer regression.");
    }
}
