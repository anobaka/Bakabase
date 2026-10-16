using System.Reflection;
using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Providers.PlayableItem;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.Modules.Player.Abstractions.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public class LocalFilePlaybackTests
{
    public class ProfileProxy : DispatchProxy
    {
        public ResourceProfilePlayerOptions? Options { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IResourceProfileService.GetEffectivePlayerOptions)
                ? Task.FromResult(Options) : throw new NotSupportedException(method?.Name);
    }

    private sealed class Launcher : IBatchPlayProcessLauncher
    {
        public readonly List<(string Executable, string Arguments)> Started = [];
        public Exception? Failure { get; set; }
        public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Failure != null) throw Failure;
            Started.Add((executablePath, arguments));
            return Task.CompletedTask;
        }
    }

    private sealed class DefaultPlayer : Bakabase.InsideWorld.Business.Components.ISystemPlayer
    {
        public readonly List<string> Opened = [];
        public Task Play(string file)
        {
            Opened.Add(file);
            return Task.CompletedTask;
        }
    }

    private string _file = null!;
    private Resource _resource = null!;
    private IResourceProfileService _profiles = null!;
    private Launcher _launcher = null!;
    private DefaultPlayer _default = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"bakabase-local-play-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(_file, "test");
        _resource = new Resource {Id = 1, Path = _file, IsFile = true};
        _profiles = DispatchProxy.Create<IResourceProfileService, ProfileProxy>();
        _launcher = new Launcher();
        _default = new DefaultPlayer();
    }

    [TestCleanup]
    public void Cleanup() => File.Delete(_file);

    private LocalFilePlayableItemProvider Provider() =>
        new(_profiles, _default, null!, null!, NullLogger<LocalFilePlayableItemProvider>.Instance, _launcher);

    private void Configure(params MediaLibraryPlayer[] players) =>
        ((ProfileProxy) _profiles).Options = new() {Players = players.ToList()};

    private Task Play() => Provider().PlayAsync(_resource, new PlayableItem {Key = _file}, CancellationToken.None);

    [TestMethod]
    public async Task ExistingCustomPlayerKeepsExtensionPriorityAndDoesNotNeedHttpSetting()
    {
        Configure(new MediaLibraryPlayer {ExecutablePath = "fallback.exe"},
            new MediaLibraryPlayer {ExecutablePath = "custom-viewer.exe", Extensions = ["MP4"], Command = "--open {0}"});
        await Play();
        Assert.AreEqual(("custom-viewer.exe", $"--open \"{_file}\""), _launcher.Started.Single());
        Assert.AreEqual(0, _default.Opened.Count);
    }

    [TestMethod]
    public async Task ExistingUnmatchedExtensionsStillUseTheSystemDefault()
    {
        Configure(new MediaLibraryPlayer {ExecutablePath = "audio.exe", Extensions = [".mp3"]});
        await Play();
        Assert.AreEqual(_file, _default.Opened.Single());
        Assert.AreEqual(0, _launcher.Started.Count);
    }

    [TestMethod]
    public async Task AWindowsShortcutRemainsAConfiguredLocalOpener()
    {
        Configure(new MediaLibraryPlayer {ExecutablePath = @"C:\Tools\My Player.lnk", SupportsHttp = false});
        await Play();
        Assert.AreEqual(@"C:\Tools\My Player.lnk", _launcher.Started.Single().Executable);
    }

    [TestMethod]
    public async Task AFailedConfiguredLaunchIsReportedAndNeverFallsBackSilently()
    {
        Configure(new MediaLibraryPlayer {ExecutablePath = "missing.exe"});
        _launcher.Failure = new System.ComponentModel.Win32Exception("Executable is missing");
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(Play);
        StringAssert.Contains(error.Message, "Executable is missing");
        Assert.AreEqual(0, _default.Opened.Count);
    }

    [TestMethod]
    public async Task AKnownAudioOnlyPlayerCannotBeForcedToOpenVideoByAProfile()
    {
        Configure(new MediaLibraryPlayer {ExecutablePath = "foobar2000.exe", Extensions = [".mp4"]});
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(Play);
        Assert.AreEqual(0, _launcher.Started.Count);
        Assert.AreEqual(0, _default.Opened.Count);
    }

    [TestMethod]
    public void SystemDefaultUsesTheCurrentPlatformsPathAndContainingDirectory()
    {
        var posix = SelfPlayer.BuildStartInfo("/Volumes/NAS/My movie.mp4", false);
        Assert.AreEqual("/Volumes/NAS/My movie.mp4", posix.FileName);
        Assert.AreEqual("/Volumes/NAS", posix.WorkingDirectory);
        Assert.IsTrue(posix.UseShellExecute);
        var windows = SelfPlayer.BuildStartInfo("C:/Media/My movie.mp4", true);
        Assert.AreEqual(@"C:\Media\My movie.mp4", windows.FileName);
        Assert.IsTrue(windows.UseShellExecute);
    }
}
