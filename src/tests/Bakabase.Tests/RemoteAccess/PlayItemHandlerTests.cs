using Bakabase.Modules.Player.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Playing something on this machine when the library is on another.
/// </summary>
[TestClass]
public class PlayItemHandlerTests
{
    private string _root = null!;
    private ActiveConnection _connection = null!;
    private RecordingShell _shell = null!;
    private StubUpstreamApi _upstream = null!;
    private StubLocator _locator = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class RecordingShell : IShellOpener
    {
        public readonly List<string> Launched = [];
        public readonly List<(string Executable, string Arguments, bool Shell)> Processes = [];

        public void Reveal(string path, bool inParentDirectory) => throw new NotSupportedException();
        public void Launch(string target) => Launched.Add(target);

        public void LaunchProcess(string executable, string arguments, bool useShellExecute) =>
            Processes.Add((executable, arguments, useShellExecute));

        public void LaunchProgram(string path, string? workingDirectory) =>
            throw new NotSupportedException();
    }

    /// <summary>Stands in for this machine's installed players.</summary>
    private sealed class StubLocator : IPlayerExecutableLocator
    {
        public readonly Dictionary<string, string> Installed = [];

        public IReadOnlyList<string> Locate(KnownPlayerDefinition definition) =>
            Installed.TryGetValue(definition.Id, out var path) ? [path] : [];
    }

    private sealed class StubLoopback : ILoopbackAddressProvider
    {
        public string BuildRawFileUrl(string serverPath) =>
            $"http://127.0.0.1:34568/file/raw?fullname={Uri.EscapeDataString(serverPath)}";

        public string BuildUrl(string pathAndQuery) => $"http://127.0.0.1:34568/{pathAndQuery.TrimStart('/')}";
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-play-tests", Guid.NewGuid().ToString("N"));
        _connection = new ActiveConnection(new ClientConnectionStore(new TempDirectory(_root)));
        _shell = new RecordingShell();
        _upstream = new StubUpstreamApi();
        _locator = new StubLocator();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private LocalPlayback Playback() =>
        new(_connection, _upstream, new LocalPlayerResolver(_locator), _shell, new StubLoopback(),
            NullLogger<LocalPlayback>.Instance);

    private PlayItemHandler Handler() => new(Playback());

    private async Task<string> WithLibraryAt(string serverPath)
    {
        var local = Path.Combine(_root, "library");
        Directory.CreateDirectory(local);

        await _connection.SaveAsync("server-1", "Desk", "http://192.168.1.5:34567",
            new ClientCredentials("d1", "k1"), DateTime.UtcNow);
        await _connection.SetPathMappingsAsync("server-1",
            [new ClientPathMapping {ServerPath = serverPath, LocalPath = local}]);

        return local;
    }

    private static DefaultHttpContext Request(string queryString)
    {
        var context = new DefaultHttpContext();

        context.Request.QueryString = new QueryString(queryString);
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static (int Status, JsonElement Body) Read(HttpContext context)
    {
        context.Response.Body.Position = 0;

        return (context.Response.StatusCode,
            JsonDocument.Parse(new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd()).RootElement);
    }

    private Task Play(HttpContext context, string resourceId = "42") =>
        Handler().HandleAsync(context, new Dictionary<string, string> {["resourceId"] = resourceId});

    // ---- local files ----

    [TestMethod]
    public async Task A_mapped_file_is_played_from_disk()
    {
        // Reading from the local mount beats streaming it back from the server it
        // already lives on.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "a.mkv")}, _shell.Launched);
    }

    [TestMethod]
    public async Task An_unmapped_file_still_plays_by_streaming_through_this_client()
    {
        // The difference between "set up a path mapping first" and "it plays". The
        // player opens a plain loopback URL; the forwarding layer signs and relays it,
        // so the player needs no credentials of its own.
        _locator.Installed["Vlc"] = @"C:\Program Files\VideoLAN\VLC\vlc.exe";

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual(1, _shell.Processes.Count);
        Assert.AreEqual(@"C:\Program Files\VideoLAN\VLC\vlc.exe", _shell.Processes[0].Executable);
        StringAssert.Contains(_shell.Processes[0].Arguments, "http://127.0.0.1:34568/file/raw?fullname=");
    }

    [TestMethod]
    public async Task A_stream_is_never_handed_to_the_system_default()
    {
        // ShellExecute on an http URL starts whatever answers for http, which is a
        // browser, and a browser meets a video stream with a download prompt. Reporting
        // that as playback is how "play" turned into "your download has started".
        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        var (status, body) = Read(context);
        var message = body.GetProperty("message").GetString()!;

        Assert.AreEqual((int) HttpStatusCode.NotImplemented, status);
        Assert.AreEqual(0, _shell.Launched.Count);
        Assert.AreEqual(0, _shell.Processes.Count);

        // Sends the user to where the mapping is set in this window, and never to the
        // settings of a client that no longer exists.
        StringAssert.Contains(message, "This computer → Path mapping");
        Assert.IsFalse(message.Contains("client", StringComparison.OrdinalIgnoreCase), message);
    }

    [TestMethod]
    public async Task A_mapping_pointing_at_a_file_that_is_gone_falls_back_to_streaming()
    {
        // A stale mount looks perfectly healthy from the server, and refusing here would
        // strand the user over something the stream can serve anyway.
        await WithLibraryAt("/data/media");
        _locator.Installed["Mpv"] = "/usr/bin/mpv";

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fmissing.mkv");

        await Play(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual(0, _shell.Launched.Count);
        StringAssert.Contains(_shell.Processes[0].Arguments, "http://127.0.0.1:34568/file/raw");
    }

    [TestMethod]
    public async Task An_audio_only_player_is_not_offered_a_video_stream()
    {
        // Starting foobar2000 on an mkv is not an improvement on saying there is no
        // player here — the user would get a confused error from a program they did not
        // choose, and no hint about the mapping that would actually fix it.
        _locator.Installed["Foobar2000"] = @"C:\Program Files\foobar2000\foobar2000.exe";

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual((int) HttpStatusCode.NotImplemented, Read(context).Status);
        Assert.AreEqual(0, _shell.Processes.Count);
    }

    // ---- which player ----

    [TestMethod]
    public async Task A_player_the_server_configured_is_re_resolved_against_this_machine()
    {
        // The stored path belongs to whichever machine configured it. What carries over
        // is *which* player, not where it is.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");
        _locator.Installed["Vlc"] = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
        _upstream.PlayerOptions = new ResourceProfilePlayerOptions
        {
            Players =
            [
                new MediaLibraryPlayer
                {
                    Extensions = [".mkv"],
                    ExecutablePath = "/usr/bin/vlc",
                    Command = "\"{0}\" --fullscreen"
                }
            ]
        };

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual(1, _shell.Processes.Count);
        Assert.AreEqual(@"C:\Program Files\VideoLAN\VLC\vlc.exe", _shell.Processes[0].Executable);
        StringAssert.Contains(_shell.Processes[0].Arguments, "--fullscreen");
        StringAssert.Contains(_shell.Processes[0].Arguments, Path.Combine(local, "a.mkv"));
    }

    [TestMethod]
    public async Task A_player_that_is_not_installed_here_falls_back_to_the_system_default()
    {
        // Playing with the wrong program beats not playing, and running the server's
        // path would at best fail and at worst start whatever sits there.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");
        _upstream.PlayerOptions = new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer {Extensions = [".mkv"], ExecutablePath = "/usr/bin/vlc"}]
        };

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual(0, _shell.Processes.Count);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "a.mkv")}, _shell.Launched);
    }

    [TestMethod]
    public async Task An_unrecognised_executable_is_never_run()
    {
        // The one case where honouring the stored path could start something arbitrary
        // on the user's machine at a server's suggestion.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");
        _upstream.PlayerOptions = new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer {Extensions = [".mkv"], ExecutablePath = "/tmp/evil.sh"}]
        };

        var context = Request("?origin=FileSystem&key=%2Fdata%2Fmedia%2Fa.mkv");

        await Play(context);

        Assert.AreEqual(0, _shell.Processes.Count);
    }

    // ---- other origins ----

    [TestMethod]
    public async Task A_steam_app_opens_through_its_protocol()
    {
        var context = Request("?origin=Steam&key=440");

        await Play(context);

        CollectionAssert.AreEqual(new[] {"steam://rungameid/440"}, _shell.Launched);
    }

    [TestMethod]
    public void A_steam_key_that_is_not_a_number_is_refused()
    {
        // The key reaches the OS as a URI to act on, and it came from a server that is
        // not necessarily the user's.
        Assert.IsNull(LocalPlayback.SteamUri("440 --exec"));
        Assert.IsNull(LocalPlayback.SteamUri("../../etc"));
        Assert.IsNull(LocalPlayback.SteamUri(""));
        Assert.AreEqual("steam://rungameid/440", LocalPlayback.SteamUri("440"));
    }

    [TestMethod]
    public async Task A_web_source_opens_its_page()
    {
        var dlsite = Request("?origin=DLsite&key=RJ01234567");

        await Play(dlsite);
        StringAssert.Contains(_shell.Launched[0], "dlsite.com");
        StringAssert.Contains(_shell.Launched[0], "RJ01234567");

        var exhentai = Request("?origin=ExHentai&key=12345%2Fabcdef");

        await Play(exhentai);
        StringAssert.Contains(_shell.Launched[1], "exhentai.org/g/12345/abcdef");
    }

    // ---- history ----

    [TestMethod]
    public async Task The_server_is_told_what_was_played()
    {
        // It owns play history, and it has to record this the same way it records a play
        // it started itself — otherwise the two halves write the same event differently.
        var context = Request("?origin=Steam&key=440");

        await Play(context, "7");

        CollectionAssert.AreEqual(new[] {(7, "Steam:440")}, _upstream.Played);
    }

    [TestMethod]
    public async Task Nothing_is_recorded_when_nothing_played()
    {
        var context = Request("?origin=Steam&key=not-a-number");

        await Play(context);

        Assert.AreEqual(0, _upstream.Played.Count);
        Assert.AreEqual((int) HttpStatusCode.BadRequest, Read(context).Status);
    }

    [TestMethod]
    public async Task A_request_with_nothing_to_play_says_so()
    {
        var context = Request("?origin=FileSystem");

        await Play(context);

        Assert.AreEqual((int) HttpStatusCode.BadRequest, Read(context).Status);
        Assert.AreEqual(0, _shell.Launched.Count);
    }

    // ---- a whole resource ----

    private Task PlayResource(HttpContext context, string resourceId = "42") =>
        new PlayResourceHandler(_upstream, Playback())
            .HandleAsync(context, new Dictionary<string, string> {["resourceId"] = resourceId});

    [TestMethod]
    public async Task A_named_file_is_played_without_asking_the_server_what_is_playable()
    {
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");

        // PlayableItems left null: reaching for it would be an unreachable server here,
        // and this must not need the round trip at all.
        var context = Request("?file=%2Fdata%2Fmedia%2Fa.mkv");

        await PlayResource(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "a.mkv")}, _shell.Launched);
    }

    [TestMethod]
    public async Task A_resource_with_no_file_named_plays_the_first_the_server_offers()
    {
        // Which file counts as playable is decided by profile rules and a cache that only
        // the server has. Guessing from a path mapping would see only mounted libraries.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "ep1.mkv"), "x");
        _upstream.PlayableItems =
        [
            new PlayableItem {Origin = DataOrigin.FileSystem, Key = "/data/media/ep1.mkv"},
            new PlayableItem {Origin = DataOrigin.FileSystem, Key = "/data/media/ep2.mkv"}
        ];

        var context = Request("");

        await PlayResource(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "ep1.mkv")}, _shell.Launched);
    }

    [TestMethod]
    public async Task A_store_page_is_not_opened_when_the_user_asked_to_play_the_files()
    {
        // The server's own /play looks specifically for a local file. Opening a DLsite
        // page instead would be a different action wearing the same name.
        _upstream.PlayableItems = [new PlayableItem {Origin = DataOrigin.DLsite, Key = "RJ01234567"}];

        var context = Request("");

        await PlayResource(context);

        Assert.AreEqual((int) HttpStatusCode.BadRequest, Read(context).Status);
        Assert.AreEqual(0, _shell.Launched.Count);
    }

    [TestMethod]
    public async Task A_server_that_cannot_be_asked_what_is_playable_is_reported_as_such()
    {
        var context = Request("");

        await PlayResource(context);

        Assert.AreEqual((int) HttpStatusCode.ServiceUnavailable, Read(context).Status);
    }

    // ---- something at random ----

    private Task PlayRandom(HttpContext context) =>
        new PlayRandomResourceHandler(_upstream, Playback())
            .HandleAsync(context, new Dictionary<string, string>());

    [TestMethod]
    public async Task A_random_pick_is_played_and_recorded_against_the_resource_it_came_from()
    {
        // The client never chose this resource, so the id has to travel with the pick —
        // history written against the wrong one would be worse than none.
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");
        _upstream.RandomPick = new UpstreamRandomPick(
            new PlayableItemPick(99, DataOrigin.FileSystem, "/data/media/a.mkv"));

        var context = Request("");

        await PlayRandom(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "a.mkv")}, _shell.Launched);
        CollectionAssert.AreEqual(new[] {(99, "FileSystem:/data/media/a.mkv")}, _upstream.Played);
    }

    [TestMethod]
    public async Task Nothing_playable_and_no_answer_at_all_are_reported_differently()
    {
        // Both end in "nothing happened", and only one of them is something the user can
        // do anything about.
        var nothingPlayable = Request("");
        _upstream.RandomPick = new UpstreamRandomPick(null);

        await PlayRandom(nothingPlayable);

        var (status, body) = Read(nothingPlayable);
        Assert.AreEqual((int) HttpStatusCode.BadRequest, status);
        StringAssert.Contains(body.GetProperty("message").GetString()!, "No playable resource");

        var unreachable = Request("");
        _upstream.RandomPick = null;

        await PlayRandom(unreachable);

        Assert.AreEqual((int) HttpStatusCode.ServiceUnavailable, Read(unreachable).Status);
        Assert.AreEqual(0, _shell.Launched.Count);
    }
}
