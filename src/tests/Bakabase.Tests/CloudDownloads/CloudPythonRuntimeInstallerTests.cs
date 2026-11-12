using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudPythonRuntimeInstallerTests
{
    private string _root = null!;
    private string Destination => Path.Combine(_root, "installed", "python");
    private string Working => Path.Combine(_root, "work");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "cloud-python-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Working);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private sealed record Fixture(string Name, string? Text = null, string? Link = null);

    private string Archive(Fixture first, params Fixture[] entries)
    {
        var archive = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tar.gz");
        using var file = File.Create(archive);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip);
        foreach (var item in new[] { first }.Concat(entries))
        {
            var entry = new PaxTarEntry(item.Link == null ? TarEntryType.RegularFile : TarEntryType.SymbolicLink, item.Name)
            { Mode = (UnixFileMode)0x1ed };
            if (item.Link != null) entry.LinkName = item.Link;
            else entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(item.Text ?? ""));
            tar.WriteEntry(entry);
            entry.DataStream?.Dispose();
        }
        return archive;
    }

    private string RawUstarArchive(Fixture first, params Fixture[] entries)
    {
        var archive = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tar.gz");
        using var file = File.Create(archive);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        foreach (var entry in new[] { first }.Concat(entries))
        {
            var header = new byte[512];
            static void Field(byte[] target, int offset, string value) =>
                Encoding.ASCII.GetBytes(value).CopyTo(target, offset);
            var data = entry.Link == null ? Encoding.UTF8.GetBytes(entry.Text ?? "") : [];
            Field(header, 0, entry.Name);
            Field(header, 100, "0000755\0");
            Field(header, 108, "0000000\0");
            Field(header, 116, "0000000\0");
            Field(header, 124, Convert.ToString(data.Length, 8)!.PadLeft(11, '0') + "\0");
            Field(header, 136, "00000000000\0");
            header.AsSpan(148, 8).Fill((byte)' ');
            header[156] = (byte)(entry.Link == null ? '0' : '2');
            if (entry.Link != null) Field(header, 157, entry.Link);
            Field(header, 257, "ustar\0");
            Field(header, 263, "00");
            Field(header, 148, Convert.ToString(header.Sum(b => (int)b), 8)!.PadLeft(6, '0') + "\0 ");
            gzip.Write(header);
            gzip.Write(data);
            if (data.Length % 512 != 0) gzip.Write(new byte[512 - data.Length % 512]);
        }
        gzip.Write(new byte[1024]);
        return archive;
    }

    private static async Task Validate(string root, CancellationToken ct)
    {
        var marker = await File.ReadAllTextAsync(Path.Combine(root, "bin", "python3"), ct);
        if (!marker.StartsWith("runtime-", StringComparison.Ordinal))
            throw new InvalidDataException("Runtime fixture did not pass validation.");
    }

    private void Existing(string text)
    {
        Directory.CreateDirectory(Path.Combine(Destination, "bin"));
        File.WriteAllText(Path.Combine(Destination, "bin", "python3"), text);
    }

    [TestMethod]
    public async Task FiltersCaseCollidingTerminfoAndPreservesInternalInterpreterSymlink()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The fixture exercises Unix runtime symlinks.");
        var archive = Archive(
            new Fixture("python/bin/python3", Link: "python3.13"),
            new Fixture("python/bin/python3.13", "runtime-native"),
            new Fixture("python/lib/libpython3.13.so", Link: "libpython3.13.so.1.0"),
            new Fixture("python/lib/libpython3.13.so.1.0", "library"),
            new Fixture("python/share/terminfo/2/2621A", "upper"),
            new Fixture("python/share/terminfo/2/2621a", "lower"),
            new Fixture("python/share/terminfo/E/Eterm", Link: "../e/eterm"),
            new Fixture("python/LICENSE.txt", "upstream license"));

        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);

        Assert.AreEqual("runtime-native", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
        Assert.AreEqual("python3.13", new FileInfo(Path.Combine(Destination, "bin", "python3")).LinkTarget);
        Assert.AreEqual("libpython3.13.so.1.0", new FileInfo(Path.Combine(Destination, "lib", "libpython3.13.so")).LinkTarget);
        Assert.IsFalse(Directory.Exists(Path.Combine(Destination, "share", "terminfo")));
        Assert.AreEqual("upstream license", await File.ReadAllTextAsync(Path.Combine(Destination, "LICENSE.txt")));
    }

    [TestMethod]
    public async Task RetryUsesFreshStagingInsteadOfLegacyPartiallyExtractedFiles()
    {
        var stale = Path.Combine(Working, "extracted", "python", "bin");
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "idle3"), "stale partial file");
        await File.WriteAllTextAsync(Path.Combine(stale, "python3"), "old incomplete runtime");
        var archive = Archive(new Fixture("python/bin/python3", "runtime-retry"), new Fixture("python/bin/idle3", "new tool"));

        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);

        Assert.AreEqual("runtime-retry", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
        Assert.AreEqual("new tool", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "idle3")));
        Assert.AreEqual("stale partial file", await File.ReadAllTextAsync(Path.Combine(stale, "idle3")));
        Assert.AreEqual(0, Directory.GetDirectories(Working, "extract-*").Length);
    }

    [TestMethod]
    public async Task PreservesPaxLongNamesWhileFilteringTerminalDefinitions()
    {
        var relative = "lib/python3.13/site-packages/" + new string('a', 130) + "/module.py";
        var archive = Archive(new Fixture("python/bin/python3", "runtime-longnames"), new Fixture("python/" + relative, "module source"));
        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);
        Assert.AreEqual("module source", await File.ReadAllTextAsync(Path.Combine(Destination, relative)));
    }

    [TestMethod]
    public async Task FixedWidthUstarNamesAndLinksEndAtFirstNull()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The fixture exercises Unix runtime symlinks.");
        var archive = RawUstarArchive(
            new Fixture("python/bin/python3\0stale-header-padding", Link: "python3.13\0old-link-padding"),
            new Fixture("python/bin/python3.13\0stale-header-padding", "runtime-null-terminated"));
        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);
        Assert.AreEqual("runtime-null-terminated", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
        Assert.AreEqual("python3.13", new FileInfo(Path.Combine(Destination, "bin", "python3")).LinkTarget);
        Assert.AreEqual(0, Directory.GetDirectories(Working, "extract-*").Length);
    }

    [TestMethod]
    public async Task NullTerminatedUstarStillRejectsTraversalBeforeItsTerminator()
    {
        var archive = RawUstarArchive(new Fixture("python/../../escaped.txt\0padding", "escape"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default));
        Assert.IsFalse(Directory.Exists(Destination));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.AreEqual(0, Directory.GetDirectories(Working, "extract-*").Length);
    }

    [TestMethod]
    public async Task ValidationFailureDoesNotPublishAnIncompleteRuntime()
    {
        var archive = Archive(new Fixture("python/bin/python3", "broken"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default));
        Assert.IsFalse(Directory.Exists(Destination));
    }

    [TestMethod]
    public async Task HealthyExistingRuntimeIsPreserved()
    {
        Existing("runtime-working");
        var archive = Archive(new Fixture("python/bin/python3", "runtime-replacement"));
        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);
        Assert.AreEqual("runtime-working", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
    }

    [TestMethod]
    public async Task InvalidExistingRuntimeIsReplacedOnlyAfterCandidateValidation()
    {
        Existing("broken old copy");
        var archive = Archive(new Fixture("python/bin/python3", "runtime-repaired"));
        await CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default);
        Assert.AreEqual("runtime-repaired", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
    }

    [TestMethod]
    public async Task FailedRepairPreservesExistingFiles()
    {
        Existing("broken old copy");
        var archive = Archive(new Fixture("python/bin/python3", "broken new copy"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default));
        Assert.AreEqual("broken old copy", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
    }

    [DataTestMethod]
    [DataRow("python/../../escaped.txt", null)]
    [DataRow("python/bin/escape", "../../escaped.txt")]
    [DataRow("python/bin/escape", "/absolute-escape")]
    public async Task TraversalAndEscapingSymlinksAreRejected(string name, string? link)
    {
        var archive = Archive(new Fixture("python/bin/python3", "runtime-unpublished"), new Fixture(name, "escaped", link));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            CloudPythonRuntimeInstaller.InstallAsync(archive, Path.GetDirectoryName(Destination)!, Working, Validate, default));
        Assert.IsFalse(Directory.Exists(Destination));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(Working, "escaped.txt")));
    }

    [TestMethod]
    public async Task CancellationDuringValidationKeepsExistingRuntime()
    {
        Existing("broken old copy");
        var archive = Archive(new Fixture("python/bin/python3", "runtime-new"));
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CloudPythonRuntimeInstaller.InstallAsync(
            archive, Path.GetDirectoryName(Destination)!, Working, async (candidate, ct) =>
            {
                await Validate(candidate, ct);
                cancel.Cancel();
                ct.ThrowIfCancellationRequested();
            }, cancel.Token));
        Assert.AreEqual("broken old copy", await File.ReadAllTextAsync(Path.Combine(Destination, "bin", "python3")));
    }

    [TestMethod]
    public async Task CancelledToolProcessExitsBeforeCallerCanCleanItsWorkingDirectory()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The process fixture uses /bin/sh.");
        var script = Path.Combine(Working, "wait.sh");
        await File.WriteAllTextAsync(script,
            "#!/bin/sh\nprintf '%s' \"$$\" > pid.txt\nwhile :\ndo\n  sleep 1\ndone\n");
        using var cancel = new CancellationTokenSource();
        var running = CloudToolProcess.RunAsync("/bin/sh", [script], Working, cancel.Token);
        var pid = 0;
        try
        {
            for (var i = 0; i < 150 && pid == 0; i++)
            {
                var file = Path.Combine(Working, "pid.txt");
                if (File.Exists(file)) int.TryParse(await File.ReadAllTextAsync(file), out pid);
                if (pid == 0) await Task.Delay(20);
            }
            Assert.IsTrue(pid > 0, "The fixture process did not start.");
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                Assert.IsTrue(process.HasExited, "The runner returned cancellation before the child exited.");
            }
            catch (ArgumentException) { /* An exited process may already have disappeared. */ }
        }
        finally
        {
            cancel.Cancel();
            try { await running; } catch (OperationCanceledException) { }
        }
    }
}
