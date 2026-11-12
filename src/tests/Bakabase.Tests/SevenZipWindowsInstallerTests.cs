using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class SevenZipWindowsInstallerTests
{
    // A real 7z Copy archive containing only readme.txt, generated with the official portable 7-Zip.
    private const string MissingRuntimeArchive =
        "N3q8ryccAASOt219IgAAAAAAAABCAAAAAAAAAJmcITpmaXh0dXJlIHdpdGhvdXQgZXhlY3V0YWJsZSBwYXlsb2FkAQQGAAEJIgAHCwEAAQEADCIACAoBx+QppwAABQERFwByAGUAYQBkAG0AZQAuAHQAeAB0AAAAGQAVBgEAIICAgQAA";

    [DataTestMethod]
    [DataRow("../7z.exe")]
    [DataRow("folder/../../7z.dll")]
    [DataRow("/7z.exe")]
    [DataRow("\\\\server\\share\\7z.exe")]
    [DataRow("C:\\7z.exe")]
    [DataRow("7z.exe:alternate-stream")]
    [DataRow("folder/./7z.exe")]
    [DataRow("7z.exe\0ignored")]
    public void RejectsUnsafePayloadPathsBeforeSelectingRuntimeFiles(string path) =>
        Assert.ThrowsException<InvalidDataException>(() => SevenZipWindowsInstaller.ValidateEntryName(path, null));

    [TestMethod]
    public void RejectsLinksEvenWhenTheirNamesWouldNotBeInstalled() =>
        Assert.ThrowsException<InvalidDataException>(() => SevenZipWindowsInstaller.ValidateEntryName("Lang/unused.txt", "../../outside"));

    [TestMethod]
    public void SkipsFalsePeSignatureLiteralsAndFindsTheCrcValidatedEmbeddedArchive()
    {
        var archive = Convert.FromBase64String(MissingRuntimeArchive);
        var prefix = new byte[128];
        archive.AsSpan(0, 6).CopyTo(prefix.AsSpan(16));
        Assert.AreEqual(prefix.Length, SevenZipWindowsInstaller.FindPayloadOffset(prefix.Concat(archive).ToArray()));
        archive[8] ^= 1;
        Assert.ThrowsException<InvalidDataException>(() => SevenZipWindowsInstaller.FindPayloadOffset(archive));
    }

    [DataTestMethod]
    [DataRow("7z.exe", "7z.exe")]
    [DataRow("License.txt", "License.txt")]
    [DataRow("Lang\\en.txt", "Lang/en.txt")]
    [DataRow("Lang/", "Lang")]
    public void PreservesOfficialPayloadNamesAndNormalizesWindowsSeparators(string path, string expected) =>
        Assert.AreEqual(expected, SevenZipWindowsInstaller.ValidateEntryName(path, null));

    [TestMethod]
    public async Task IncompletePayloadDoesNotReplaceAnExistingConsoleAndCleansItsStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "sevenzip-windows-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "7z");
        Directory.CreateDirectory(destination);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "7z.exe"), "old console");
            var archive = Path.Combine(root, "setup.exe");
            await File.WriteAllBytesAsync(archive, Convert.FromBase64String(MissingRuntimeArchive));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                SevenZipWindowsInstaller.InstallAsync(archive, destination, NullLoggerFactory.Instance, default));
            Assert.AreEqual("old console", await File.ReadAllTextAsync(Path.Combine(destination, "7z.exe")));
            Assert.IsFalse(File.Exists(Path.Combine(destination, "readme.txt")));
            Assert.AreEqual(0, Directory.GetDirectories(root, ".7z.install-*").Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
