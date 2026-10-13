using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Services;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Components.BatchPlay;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>Batch candidates and launches use this computer's installations,
/// while the managed server supplies profile choices and playable files.</summary>
[TestClass]
public class RemoteBatchPlayPlayerTests
{
    private const string RemotePotPlayer = @"D:\server-tools\PotPlayerMini64.exe";
    private const string LocalPotPlayer = @"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe";
    private const string LocalVlc = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
    private string _root = null!;
    private StubUpstreamApi _upstream = null!;
    private Locator _locator = null!;
    private Launcher _launcher = null!;
    private ActiveConnection _connection = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class Locator : IPlayerExecutableLocator
    {
        public readonly Dictionary<string, string> Installed = [];
        public IReadOnlyList<string> Locate(KnownPlayerDefinition definition) =>
            Installed.TryGetValue(definition.Id, out var path) ? [path] : [];
    }

    private sealed class Launcher : IBatchPlayProcessLauncher
    {
        public readonly List<(string Executable, string Arguments)> Launches = [];
        public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
        {
            Launches.Add((executablePath, arguments));
            return Task.CompletedTask;
        }
    }

    private sealed class Loopback : ILoopbackAddressProvider
    {
        public string BuildRawFileUrl(string serverPath) =>
            $"http://127.0.0.1:34568/file/raw?fullname={Uri.EscapeDataString(serverPath)}";
        public string BuildUrl(string pathAndQuery) => $"http://127.0.0.1:34568/{pathAndQuery.TrimStart('/')}";
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-remote-batch-player", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _upstream = new StubUpstreamApi();
        _locator = new Locator();
        _launcher = new Launcher();
        _connection = new ActiveConnection(new ClientConnectionStore(new TempDirectory(_root)));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private UpstreamBatchPlayResourceSource Source() =>
        new(_upstream, _locator, NullLogger<UpstreamBatchPlayResourceSource>.Instance);

    private BatchPlayService Service() => new(
        Source(),
        new ClientBatchPlayFileResolver(_connection, new Loopback()),
        new PlayerDiscoveryService(_locator, NullLogger<PlayerDiscoveryService>.Instance),
        new UpstreamBatchPlayPlaylistSource(_upstream),
        _launcher,
        Options.Create(new PlayerModuleOptions {TempPlaylistDirectory = Path.Combine(_root, "playlists")}),
        NullLogger<BatchPlayService>.Instance);

    private void Seed(MediaLibraryPlayer[] players, params string[] files) =>
        _upstream.BatchPlayResources = new BatchPlayResourceSnapshot([new(42, files.ToList())], players.ToList());

    private static BatchPlayInputModel Input(string key) => new()
    {
        ResourceIds = [42], PlayerKey = key, FileSelectionMode = BatchPlayFileSelectionMode.AllFiles
    };

    [TestMethod]
    public async Task A_windows_server_profile_is_relocated_without_mutating_its_snapshot()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        var profile = new MediaLibraryPlayer
        {
            ExecutablePath = RemotePotPlayer, Command = "/fullscreen {0}", Extensions = [" MKV ", ".MP4"]
        };
        Seed([profile], "/srv/movie.mkv");

        var snapshot = await Source().GetSnapshotAsync([42], CancellationToken.None);
        var resolved = snapshot.ConfiguredPlayers.Single();
        Assert.AreEqual(LocalPotPlayer, resolved.ExecutablePath);
        Assert.AreEqual(profile.Command, resolved.Command);
        CollectionAssert.AreEquivalent(new[] {".MKV", ".MP4"}, resolved.Extensions!.ToArray());
        Assert.AreEqual(RemotePotPlayer, profile.ExecutablePath);
        CollectionAssert.AreEquivalent(new[] {" MKV ", ".MP4"}, profile.Extensions!.ToArray());
        Assert.AreSame(_upstream.BatchPlayResources!.Resources, snapshot.Resources);
    }

    [TestMethod]
    public async Task A_macos_server_profile_uses_the_windows_clients_installation()
    {
        _locator.Installed["Vlc"] = LocalVlc;
        Seed([new() {ExecutablePath = "/Applications/VLC.app/Contents/MacOS/VLC", Command = "--fullscreen {0}"}],
            "/srv/movie.mp4");
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();

        Assert.AreEqual(BatchPlayCandidateType.ProfilePlayer, candidate.Type);
        Assert.AreEqual(LocalVlc, candidate.ExecutablePath);
        await service.PlayAsync(Input(candidate.Key), CancellationToken.None);
        Assert.AreEqual(LocalVlc, _launcher.Launches.Single().Executable);
        StringAssert.StartsWith(_launcher.Launches.Single().Arguments, "--fullscreen \"");
        StringAssert.Contains(_launcher.Launches.Single().Arguments, Uri.EscapeDataString("/srv/movie.mp4"));
    }

    [TestMethod]
    [DataRow(@"D:\tools\UnrecognisedViewer.exe")]
    [DataRow(RemotePotPlayer)]
    public async Task Unknown_or_uninstalled_remote_players_are_not_advertised(string configuredExecutable)
    {
        _locator.Installed["Vlc"] = LocalVlc;
        Seed([new() {ExecutablePath = configuredExecutable}], "/srv/movie.mkv");
        var candidate = (await Service().GetCandidatesAsync([42], CancellationToken.None)).Single();

        Assert.AreEqual("known|Vlc", candidate.Key);
        Assert.AreEqual(BatchPlayCandidateType.KnownPlayer, candidate.Type);
        Assert.AreEqual(LocalVlc, candidate.ExecutablePath);
    }

    [TestMethod]
    public async Task Profiles_that_resolve_to_one_installation_keep_the_first_choice_and_stable_key()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([
            new() {ExecutablePath = RemotePotPlayer, Command = "/fullscreen {0}", Extensions = [".mkv"]},
            new() {ExecutablePath = @"E:\portable\PotPlayer.exe", Command = "/second {0}", Extensions = [".mp4"]}
        ], "/srv/movie.mkv", "/srv/clip.mp4");

        var first = (await Service().GetCandidatesAsync([42], CancellationToken.None)).Single();
        var again = (await Service().GetCandidatesAsync([42], CancellationToken.None)).Single();
        Assert.AreEqual($"profile|{LocalPotPlayer.ToLowerInvariant()}", first.Key);
        Assert.AreEqual(first.Key, again.Key);
        Assert.AreEqual(BatchPlayCandidateType.ProfilePlayer, first.Type);
        Assert.AreEqual("/fullscreen {0}", first.CommandTemplate);
        CollectionAssert.AreEquivalent(new[] {".mkv"}, first.SupportedExtensions!.ToArray());
    }

    [TestMethod]
    public async Task PotPlayer_keeps_the_profile_template_and_stream_playlist_but_never_receives_images()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new()
        {
            ExecutablePath = RemotePotPlayer, Command = "/fullscreen {0}", Extensions = ["mkv", " .MP4 ", ".jpg"]
        }], "/srv/a.mkv", "/srv/cover.jpg", "/srv/b.mp4");
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();
        var result = await service.PlayAsync(Input(candidate.Key), CancellationToken.None);

        Assert.AreEqual(2, result.FileCount);
        Assert.AreEqual(BatchPlayLaunchMethod.PlaylistFile, result.LaunchMethod);
        Assert.IsFalse(candidate.SupportedExtensions!.Contains(".jpg"));
        var playlist = Directory.EnumerateFiles(Path.Combine(_root, "playlists"), "*.m3u8").Single();
        var content = await File.ReadAllTextAsync(playlist);
        StringAssert.Contains(content, new Loopback().BuildRawFileUrl("/srv/a.mkv"));
        StringAssert.Contains(content, new Loopback().BuildRawFileUrl("/srv/b.mp4"));
        Assert.IsFalse(content.Contains("cover.jpg", StringComparison.Ordinal));
        Assert.AreEqual(LocalPotPlayer, _launcher.Launches.Single().Executable);
        Assert.AreEqual($"/fullscreen \"{playlist}\"", _launcher.Launches.Single().Arguments);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task Unspecified_extensions_use_the_known_players_AV_support(int extensionShape)
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new()
        {
            ExecutablePath = RemotePotPlayer,
            Extensions = extensionShape switch {0 => null, 1 => [], _ => [" "]}
        }], "/srv/a.mkv", "/srv/cover.jpg");
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();
        var result = await service.PlayAsync(Input(candidate.Key), CancellationToken.None);

        Assert.AreEqual(BatchPlayCandidateType.ProfilePlayer, candidate.Type);
        Assert.IsFalse(candidate.SupportedExtensions!.Contains(".jpg"));
        Assert.AreEqual(1, result.FileCount);
        Assert.IsFalse(_launcher.Launches.Single().Arguments.Contains("cover.jpg", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task An_image_only_PotPlayer_profile_is_omitted_instead_of_becoming_unrestricted()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new() {ExecutablePath = RemotePotPlayer, Extensions = [".jpg"]}], "/srv/cover.jpg");
        Assert.AreEqual(0, (await Source().GetSnapshotAsync([42], CancellationToken.None)).ConfiguredPlayers.Count);
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();
        Assert.AreEqual("known|PotPlayer", candidate.Key);
        Assert.AreEqual(0, candidate.MatchedResourceCount);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.PlayAsync(Input(candidate.Key), CancellationToken.None));
        Assert.AreEqual(0, _launcher.Launches.Count);
    }

    [TestMethod]
    public async Task A_remote_executable_key_is_not_a_command_the_caller_can_supply()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new() {ExecutablePath = RemotePotPlayer}], "/srv/movie.mkv");
        var e = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service().PlayAsync(Input($"profile|{RemotePotPlayer.ToLowerInvariant()}"), CancellationToken.None));
        StringAssert.Contains(e.Message, "no longer available");
        Assert.AreEqual(0, _launcher.Launches.Count);
    }

    [TestMethod]
    public async Task A_player_removed_after_the_menu_was_opened_is_not_launched_from_a_stale_profile_key()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new() {ExecutablePath = RemotePotPlayer}], "/srv/movie.mkv");
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();
        _locator.Installed.Clear();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.PlayAsync(Input(candidate.Key), CancellationToken.None));
        Assert.AreEqual(0, _launcher.Launches.Count);
    }

    [TestMethod]
    public async Task Iina_is_found_on_macos_and_retains_the_single_file_template_and_one_required_prefix()
    {
        const string localIina = "/Applications/IINA.app/Contents/MacOS/iina-cli";
        _locator.Installed["Iina"] = localIina;
        Seed([new() {ExecutablePath = @"D:\server-tools\iina-cli", Command = "--fullscreen {0}"}], "/srv/movie.mp4");
        var service = Service();
        var candidate = (await service.GetCandidatesAsync([42], CancellationToken.None)).Single();
        await service.PlayAsync(Input(candidate.Key), CancellationToken.None);

        var launch = _launcher.Launches.Single();
        Assert.AreEqual(localIina, launch.Executable);
        StringAssert.StartsWith(launch.Arguments, "--no-stdin --fullscreen \"");
        Assert.AreEqual(1, launch.Arguments.Split("--no-stdin").Length - 1);
    }

    [TestMethod]
    public async Task Playlist_batch_play_resolves_the_same_profile_and_excludes_image_entries()
    {
        _locator.Installed["PotPlayer"] = LocalPotPlayer;
        Seed([new() {ExecutablePath = RemotePotPlayer, Command = "/playlist {0}", Extensions = ["mkv", "jpg"]}],
            "/srv/a.mkv", "/srv/cover.jpg", "/srv/b.mkv");
        _upstream.BatchPlayPlaylist = new UpstreamPlaylistSnapshot(new("Evening",
            [new("/srv/a.mkv", 42), new("/srv/cover.jpg", 42), new("/srv/b.mkv", 42)]));
        var service = Service();
        var candidate = (await service.GetPlaylistCandidatesAsync(7, CancellationToken.None)).Single();
        Assert.AreEqual(LocalPotPlayer, candidate.ExecutablePath);
        Assert.AreEqual(2, candidate.MatchedFileCount);

        var result = await service.PlayPlaylistAsync(7, candidate.Key, CancellationToken.None);
        Assert.AreEqual(2, result.FileCount);
        Assert.AreEqual(LocalPotPlayer, _launcher.Launches.Single().Executable);
        StringAssert.StartsWith(_launcher.Launches.Single().Arguments, "/playlist \"");
        var playlist = Directory.EnumerateFiles(Path.Combine(_root, "playlists"), "*.m3u8").Single();
        Assert.IsFalse((await File.ReadAllTextAsync(playlist)).Contains("cover.jpg", StringComparison.Ordinal));
    }
}
