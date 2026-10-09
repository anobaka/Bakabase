using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // PATH is process-wide.
public class ExecutableDiscovererTests
{
    private string _root = null!;
    private string? _previousPath;
    private string Default => Path.Combine(_root, "imported components");
    private string Native => Path.Combine(_root, "native bin");
    private const string Tool = "bakabase-test-component";

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Default);
        Directory.CreateDirectory(Native);
        _previousPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", Native);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath);
        Directory.Delete(_root, true);
    }

    private static void RequireUnix()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix execution/fallback regression.");
    }

    private static void Script(string path, string output)
    {
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' '" + output + "'\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [TestMethod]
    public async Task ImportedWindowsExecutableDoesNotMasqueradeAsUnixBinary()
    {
        RequireUnix();
        var imported = Path.Combine(Default, Tool + ".exe");
        File.WriteAllText(imported, "Windows executable remains untouched");
        Script(Path.Combine(Native, Tool), "native");
        Assert.IsFalse(Probe.HasFiles(Default));
        var result = await new Probe().Discover(Default, default);
        Assert.AreEqual(Native, result!.Value.Location);
        Assert.AreEqual("native", result.Value.Version);
        Assert.AreEqual("Windows executable remains untouched", File.ReadAllText(imported));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImportedFfMpegPairUsesNativePathWhenItsProbeIsForWindows(bool hasNativeFfMpeg)
    {
        RequireUnix();
        var importedProbe = Path.Combine(Default, "ffprobe.exe");
        File.WriteAllText(importedProbe, "Imported Windows ffprobe stays intact");
        if (hasNativeFfMpeg)
            Script(Path.Combine(Default, "ffmpeg"), "ffmpeg version 7.0-imported Copyright FFmpeg");
        else
            File.WriteAllText(Path.Combine(Default, "ffmpeg.exe"), "Imported Windows ffmpeg");
        Script(Path.Combine(Native, "ffmpeg"), "ffmpeg version 6.1.1-native Copyright FFmpeg");
        Script(Path.Combine(Native, "ffprobe"), "ffprobe version 6.1.1-native Copyright FFmpeg");

        var result = await new FfMpegDiscoverer(NullLoggerFactory.Instance).Discover(Default, default);

        Assert.IsNotNull(result);
        Assert.AreEqual(Native, result.Value.Location);
        Assert.AreEqual("6.1.1-native", result.Value.Version);
        Assert.AreEqual("Imported Windows ffprobe stays intact", File.ReadAllText(importedProbe));
    }

    [TestMethod]
    public async Task FfMpegWithoutANativeFfProbeIsNotReportedAsInstalled()
    {
        RequireUnix();
        Script(Path.Combine(Native, "ffmpeg"), "ffmpeg version 6.1.1-native Copyright FFmpeg");
        File.WriteAllText(Path.Combine(Native, "ffprobe.exe"), "Windows ffprobe is not a Unix executable");

        var result = await new FfMpegDiscoverer(NullLoggerFactory.Instance).Discover(Default, default);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task NonExecutableImportedFileFallsBackToPath()
    {
        RequireUnix();
        var file = Path.Combine(Default, Tool);
        Script(file, "imported");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Script(Path.Combine(Native, Tool), "native");
        Assert.IsFalse(Probe.HasFiles(Default));
        Assert.AreEqual(Native, (await new Probe().Discover(Default, default))!.Value.Location);
    }

    [TestMethod]
    public async Task UnlaunchableBinaryFallsBackToPathWithoutThrowing()
    {
        RequireUnix();
        var file = Path.Combine(Default, Tool);
        File.WriteAllBytes(file, [0x7f, 0x45, 0x4c, 0x46, 0, 0, 0]);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Script(Path.Combine(Native, Tool), "native");
        Assert.AreEqual(Native, (await new Probe().Discover(Default, default))!.Value.Location);
    }

    [TestMethod]
    public async Task UnexpectedVersionFallsBackButValidDefaultTakesPrecedence()
    {
        RequireUnix();
        Script(Path.Combine(Default, Tool), "invalid");
        Script(Path.Combine(Native, Tool), "native");
        Assert.AreEqual(Native, (await new Probe().Discover(Default, default))!.Value.Location);
        Script(Path.Combine(Default, Tool), "imported");
        Assert.AreEqual(Default, (await new Probe().Discover(Default, default))!.Value.Location);
    }

    [TestMethod]
    public async Task NoCompatibleBinaryIsReportedAsUnavailableAndCancellationIsPreserved()
    {
        RequireUnix();
        File.WriteAllText(Path.Combine(Default, Tool + ".exe"), "foreign");
        Assert.IsNull(await new Probe().Discover(Default, default));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new Probe().Discover(Default, new CancellationToken(true)));
    }

    private sealed class Probe() : ExecutableDiscoverer(NullLoggerFactory.Instance)
    {
        protected override HashSet<string> RequiredRelativeFileNamesWithoutExtensions { get; } = [Tool];
        protected override string RelativeFileNameWithoutExtensionForAcquiringVersion => Tool;
        protected override string? ArgumentsForAcquiringVersion => null;
        protected override string ParseVersion(string output) => output.Trim() == "invalid"
            ? throw new FormatException("Unrecognized version") : output.Trim();
        public static bool HasFiles(string directory) => DiscoverByDirectory(directory, [Tool]);
    }
}
