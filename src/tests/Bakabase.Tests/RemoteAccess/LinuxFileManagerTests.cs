using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Helpers;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class LinuxFileManagerTests
{
    private static string TorrentPath => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Gallery with spaces",
        "[Manga] Gallery 'quote' \"double\" # 46; $(name).torrent"));

    private sealed class FakeProcess(bool exited = true, int exitCode = 0) : LinuxFileManager.IRevealProcess
    {
        public List<int> Waits { get; } = [];
        public int ExitCode => exitCode;
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }
        public bool WaitForExit(int milliseconds) { Waits.Add(milliseconds); return exited; }
        public void Kill() => Killed = true;
        public void Dispose() => Disposed = true;
    }

    [TestMethod]
    public void ShowItems_ReceivesOneEncodedUriArgument_WithoutShellParsingOrFileLaunch()
    {
        var process = new FakeProcess();
        var commands = new List<ProcessStartInfo>();
        LinuxFileManager.RevealInParentDirectory(TorrentPath, info => { commands.Add(info); return process; });

        Assert.AreEqual(1, commands.Count);
        var command = commands.Single();
        Assert.AreEqual("gdbus", command.FileName);
        Assert.IsFalse(command.UseShellExecute);
        Assert.AreEqual(string.Empty, command.Arguments);
        var args = command.ArgumentList.ToArray();
        CollectionAssert.AreEqual(new[] {"call", "--session", "--dest", "org.freedesktop.FileManager1",
            "--object-path", "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems",
            "--timeout", "2"}, args[..10]);
        Assert.AreEqual(12, args.Length);
        Assert.AreEqual("''", args[11]);
        Assert.IsTrue(args[10].StartsWith("['file://", StringComparison.Ordinal));
        Assert.IsTrue(args[10].EndsWith("']", StringComparison.Ordinal));
        var uri = new Uri(args[10][2..^2].Replace("\\'", "'"));
        Assert.AreEqual(TorrentPath, uri.LocalPath);
        Assert.AreEqual(string.Empty, uri.Query);
        Assert.AreEqual(string.Empty, uri.Fragment);
        CollectionAssert.AreEqual(new[] {2000}, process.Waits);
        Assert.IsFalse(process.Killed);
        Assert.IsTrue(process.Disposed);
    }

    [TestMethod]
    public void UnsupportedFileManager_OpensParentDirectory_InsteadOfTorrentFile()
    {
        var commands = new List<ProcessStartInfo>();
        var failed = new FakeProcess(exitCode: 1);
        var fallback = new FakeProcess();
        LinuxFileManager.RevealInParentDirectory(TorrentPath, info =>
        {
            commands.Add(info);
            return info.FileName == "gdbus" ? failed : fallback;
        });

        AssertParentFallback(commands);
        Assert.IsTrue(failed.Disposed);
        Assert.IsTrue(fallback.Disposed);
        Assert.AreEqual(0, fallback.Waits.Count, "Opening the fallback folder must not wait for the desktop window to close.");
    }

    [TestMethod]
    public void MissingGdbus_StillOpensParentDirectory()
    {
        var commands = new List<ProcessStartInfo>();
        LinuxFileManager.RevealInParentDirectory(TorrentPath, info =>
        {
            commands.Add(info);
            return info.FileName == "gdbus" ? throw new Win32Exception("gdbus unavailable") : new FakeProcess();
        });

        AssertParentFallback(commands);
    }

    [TestMethod]
    public void StalledDbusChild_IsKilledAfterBoundedWait_BeforeParentFallback()
    {
        var commands = new List<ProcessStartInfo>();
        var stalled = new FakeProcess(exited: false);
        LinuxFileManager.RevealInParentDirectory(TorrentPath, info =>
        {
            commands.Add(info);
            if (info.FileName == "gdbus") return stalled;
            Assert.IsTrue(stalled.Killed);
            Assert.IsTrue(stalled.Disposed);
            return new FakeProcess();
        });

        AssertParentFallback(commands);
        CollectionAssert.AreEqual(new[] {2000}, stalled.Waits);
    }

    [TestMethod]
    public void NullDbusProcess_UsesParentFallback()
    {
        var commands = new List<ProcessStartInfo>();
        LinuxFileManager.RevealInParentDirectory(TorrentPath, info =>
        {
            commands.Add(info);
            return info.FileName == "gdbus" ? null : new FakeProcess();
        });

        AssertParentFallback(commands);
    }

    [TestMethod]
    public void MissingFallbackLauncher_IsReportedToCaller()
    {
        var error = new Win32Exception("xdg-open unavailable");
        var actual = Assert.ThrowsException<Win32Exception>(() => LinuxFileManager.RevealInParentDirectory(TorrentPath,
            info => info.FileName == "gdbus" ? new FakeProcess(exitCode: 1) : throw error));

        Assert.AreSame(error, actual);
    }

    private static void AssertParentFallback(List<ProcessStartInfo> commands)
    {
        Assert.AreEqual(2, commands.Count);
        var fallback = commands[1];
        Assert.AreEqual("xdg-open", fallback.FileName);
        Assert.IsFalse(fallback.UseShellExecute);
        Assert.AreEqual(string.Empty, fallback.Arguments);
        CollectionAssert.AreEqual(new[] {Path.GetDirectoryName(TorrentPath)!}, fallback.ArgumentList.ToArray());
        Assert.IsFalse(fallback.ArgumentList.Any(value => value.EndsWith(".torrent", StringComparison.Ordinal)));
    }
}
