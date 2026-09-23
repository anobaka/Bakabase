using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class DefaultPlayerExecutableLocatorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "player-locator-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private string MakeExecutable(string relative)
    {
        // Match the locator's absolute native paths even when the portable fixture
        // name contains forward slashes on Windows.
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsWindows()) File.Copy(Environment.ProcessPath!, path);
        else
        {
            File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private TestLocator Locator(bool mac = true, string path = "") => new(_root, mac, path);

    // Bundle/PATH tests exclude the catalog's Windows installation directories:
    // a real VLC in Program Files on the test host must not enter this fixture.

    [TestMethod]
    public void Mac_FindsSystemAndUserBundlesWithoutDependingOnPath()
    {
        var systemVlc = MakeExecutable("system/VLC.app/Contents/MacOS/VLC");
        var userVlc = MakeExecutable("home/Applications/VLC.app/Contents/MacOS/VLC");
        var userIina = MakeExecutable("home/Applications/IINA.app/Contents/MacOS/iina-cli");
        Locator().Locate(KnownPlayerDefinitions.Vlc with { CandidateDirectories = [] }).Should().Equal(systemVlc, userVlc);
        Locator().Locate(KnownPlayerDefinitions.Iina).Should().Equal(userIina);
    }

    [TestMethod]
    public void Mac_IinaRequiresCliRatherThanAppMainExecutable()
    {
        MakeExecutable("system/IINA.app/Contents/MacOS/IINA");
        Locator().Locate(KnownPlayerDefinitions.Iina).Should().BeEmpty();
        var cli = MakeExecutable("system/IINA.app/Contents/MacOS/iina-cli");
        Locator().Locate(KnownPlayerDefinitions.Iina).Should().Equal(cli);
    }

    [TestMethod]
    public void UnixPath_RemovesExeSuffixAndDeduplicatesRepeatedDirectories()
    {
        var vlc = MakeExecutable("bin/vlc");
        var bin = Path.GetDirectoryName(vlc)!;
        Locator(false, string.Join(Path.PathSeparator, bin, bin)).Locate(KnownPlayerDefinitions.Vlc with { CandidateDirectories = [] }).Should().Equal(vlc);
        Locator(false).Locate(KnownPlayerDefinitions.Vlc with { CandidateDirectories = [] }).Should().BeEmpty();
    }

    [TestMethod]
    public void ExistingNonExecutableFile_IsNotDiscovered()
    {
        var directory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(directory);
        var fake = Path.Combine(directory, "vlc");
        File.WriteAllText(fake, "not an executable");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Locator(false, directory).Locate(KnownPlayerDefinitions.Vlc with { CandidateDirectories = [] }).Should().BeEmpty();
    }

    [TestMethod]
    public void InstalledIina_StreamTargetRetainsUrlAndNoStdinArgument()
    {
        var locator = new FakePlayerExecutableLocator();
        const string cli = "/Applications/IINA.app/Contents/MacOS/iina-cli";
        locator.Set("Iina", cli);
        var resolved = new LocalPlayerResolver(locator).ResolveInstalled("movie.mp4");
        resolved!.ExecutablePath.Should().Be(cli);
        const string url = "http://127.0.0.1:321/ticket?secret=opaque&file=1";
        BatchPlayArguments.BuildFromTemplate(resolved.CommandTemplate, url).Should().Be($"--no-stdin \"{url}\"");
    }

    [TestMethod]
    public void ConfiguredIina_UsesLocalCliAndKeepsExplicitUserTemplate()
    {
        var locator = new FakePlayerExecutableLocator();
        const string cli = "/Users/test/Applications/IINA.app/Contents/MacOS/iina-cli";
        locator.Set("Iina", cli);
        var resolved = new LocalPlayerResolver(locator).Resolve(new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = "/other/iina-cli", Command = "--mpv-volume=20 {0}" }]
        }, "movie.mp4");
        resolved.ExecutablePath.Should().Be(cli);
        BatchPlayArguments.BuildFromTemplate(resolved.CommandTemplate, "http://localhost/media").Should()
            .Be("--no-stdin --mpv-volume=20 \"http://localhost/media\"");
    }

    private sealed class TestLocator(string root, bool mac, string path) : DefaultPlayerExecutableLocator
    {
        protected override bool IsWindows => false;
        protected override bool IsMacOS => mac;
        protected override string UserHomeDirectory => Path.Combine(root, "home");
        protected override string SystemApplicationsDirectory => Path.Combine(root, "system");
        protected override string SearchPath => path;
    }
}
