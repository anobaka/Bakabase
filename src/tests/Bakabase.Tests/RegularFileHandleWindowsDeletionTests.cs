using System.Runtime.InteropServices;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Tests;

[TestClass]
public sealed class RegularFileHandleWindowsDeletionTests
{
    private string _root = null!;
    private string Part => Path.Combine(_root, "payload.part");

    [TestInitialize]
    public void CreateFixture()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows file sharing and handle disposition.");
        _root = Directory.CreateTempSubdirectory("BakabaseWinDelete-").FullName;
        File.WriteAllText(Part, "partial");
    }

    [TestCleanup]
    public void CleanFixture()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Version(string path)
    {
        using var file = File.OpenRead(path);
        return RegularFileHandle.ReadRetirementVersion(file, path);
    }

    [TestMethod]
    public void DeletesTheRecordedFileByHandle()
    {
        var version = Version(Part);
        Assert.IsTrue(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));
        Assert.IsFalse(File.Exists(Part));
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));
    }

    [TestMethod]
    public void PreservesChangedContentsAndAReplacementWithMatchingMetadata()
    {
        var version = Version(Part);
        File.AppendAllText(Part, "changed");
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));

        version = Version(Part);
        var original = Path.Combine(_root, "original");
        File.Move(Part, original);
        File.Copy(original, Part);
        File.SetLastWriteTimeUtc(Part, File.GetLastWriteTimeUtc(original));
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));
        Assert.IsTrue(File.Exists(Part));
        Assert.IsTrue(File.Exists(original));
    }

    [TestMethod]
    public async Task PreservesSameLengthRewriteWithRestoredLastWriteTime()
    {
        var stamp = File.GetLastWriteTimeUtc(Part);
        var version = Version(Part);
        // Give the two metadata changes distinct system clock ticks.
        await Task.Delay(100);
        File.WriteAllText(Part, "altered");
        File.SetLastWriteTimeUtc(Part, stamp);
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(Part));
        Assert.AreNotEqual(version, Version(Part));
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));
        Assert.AreEqual("altered", File.ReadAllText(Part));
    }

    [TestMethod]
    public void AnOpenWriterPreventsDeletion()
    {
        var version = Version(Part);
        using var writer = new FileStream(Part, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete);
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, version));
        Assert.IsTrue(File.Exists(Part));
    }

    [TestMethod]
    public void PreservesBothNamesOfAHardLinkedFile()
    {
        var other = Path.Combine(_root, "other");
        Assert.IsTrue(CreateHardLinkW(other, Part, IntPtr.Zero));
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, Version(Part)));
        Assert.IsTrue(File.Exists(Part));
        Assert.IsTrue(File.Exists(other));
    }

    [TestMethod]
    public void DoesNotFollowASubstitutedFileLink()
    {
        var original = Path.Combine(_root, "original");
        File.Move(Part, original);
        try { File.CreateSymbolicLink(Part, original); }
        catch (UnauthorizedAccessException) { Assert.Inconclusive("Creating a symlink requires Developer Mode or privilege."); }
        catch (IOException error) when ((error.HResult & 0xffff) == 1314) { Assert.Inconclusive("The symlink privilege is unavailable."); }
        // Following the link would present precisely the requested, stable version.
        Assert.IsFalse(RegularFileHandle.TryDeleteRecordedVersionOnWindows(Part, Version(original)));
        Assert.IsNotNull(new FileInfo(Part).LinkTarget);
        Assert.IsTrue(File.Exists(original));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
}
