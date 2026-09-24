using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.BatchPlay;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Modules.Player.Abstractions.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The three things batch play needs from a client, and nothing else.
/// </summary>
/// <remarks>
/// The orchestration itself — candidates, file selection, m3u8 versus arguments — is the
/// server's own and has its own tests. What is new here is only where its inputs come
/// from: a library read over HTTP instead of a database, and files reached through a
/// mount or a stream instead of straight off the disk.
/// </remarks>
[TestClass]
public class ClientBatchPlayTests
{
    private string _root = null!;
    private ActiveConnection _connection = null!;
    private StubUpstreamApi _upstream = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
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
        _root = Path.Combine(Path.GetTempPath(), "bakabase-client-batch-play", Guid.NewGuid().ToString("N"));
        _connection = new ActiveConnection(new ClientConnectionStore(new TempDirectory(_root)));
        _upstream = new StubUpstreamApi();
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

    private ClientBatchPlayFileResolver Resolver() => new(_connection, new StubLoopback());

    // ---- resolving files ----

    [TestMethod]
    public async Task A_mapped_file_that_is_there_is_played_from_disk()
    {
        var local = await WithLibraryAt("/data/media");

        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");

        Assert.AreEqual(Path.Combine(local, "a.mkv"), Resolver().Resolve("/data/media/a.mkv"));
    }

    [TestMethod]
    public async Task A_file_this_machine_cannot_reach_is_streamed_rather_than_dropped()
    {
        // On the server a file it cannot find is a missing file and the resource gets
        // skipped. Here "cannot find" usually means "is on the other computer", and
        // skipping it would empty out a batch play that had every reason to work.
        await WithLibraryAt("/data/media");

        var unmapped = Resolver().Resolve("/srv/other/a.mkv");
        var staleMount = Resolver().Resolve("/data/media/gone.mkv");

        StringAssert.StartsWith(unmapped!, "http://127.0.0.1:34568/file/raw?fullname=");
        StringAssert.StartsWith(staleMount!, "http://127.0.0.1:34568/file/raw?fullname=");
    }

    [TestMethod]
    public void Nothing_is_ever_unresolvable_on_a_client()
    {
        // Not paired at all, so there are no mappings whatsoever — still a URL, because
        // the forwarding layer can reach anything the server can.
        Assert.IsNotNull(Resolver().Resolve("/anywhere/at/all.mkv"));
    }

    // ---- reading the library ----

    private UpstreamBatchPlayResourceSource ResourceSource() =>
        new(_upstream, NullLogger<UpstreamBatchPlayResourceSource>.Instance);

    [TestMethod]
    public async Task A_server_that_cannot_be_asked_fails_the_batch_play_rather_than_emptying_it()
    {
        // An empty snapshot would read as "none of these has anything playable" and the
        // user would be told their selection is unplayable, which is a lie.
        var e = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => ResourceSource().GetSnapshotAsync([1, 2], CancellationToken.None));

        StringAssert.Contains(e.Message, "reachable");
    }

    [TestMethod]
    public async Task History_is_written_in_one_request_for_the_whole_batch()
    {
        _upstream.BatchPlayResources = new BatchPlayResourceSnapshot([], []);

        await ResourceSource().MarkPlayedAsync(
            new Dictionary<int, string> {[1] = "/data/a.mkv", [2] = "/data/b.mkv"}, CancellationToken.None);

        Assert.AreEqual(1, _upstream.BulkPlayed.Count);
        Assert.AreEqual(2, _upstream.BulkPlayed[0].Count);
    }

    [TestMethod]
    public async Task A_failed_history_write_does_not_undo_a_successful_play()
    {
        // The players are already running by then. Throwing here would turn a batch play
        // the user watched start into an error message.
        _upstream.MarkManyPlayedThrows = new InvalidOperationException("server went away");

        await ResourceSource().MarkPlayedAsync(new Dictionary<int, string> {[1] = "/data/a.mkv"},
            CancellationToken.None);
    }

    // ---- reading a playlist ----

    private UpstreamBatchPlayPlaylistSource PlaylistSource() => new(_upstream);

    [TestMethod]
    public async Task A_playlist_that_does_not_exist_is_told_apart_from_a_server_that_did_not_answer()
    {
        // Null from this port means "no such playlist", which the orchestration reports
        // properly. An unreachable server must not be quietly reported as that.
        _upstream.BatchPlayPlaylist = new UpstreamPlaylistSnapshot(null);
        Assert.IsNull(await PlaylistSource().GetSnapshotAsync(7, CancellationToken.None));

        _upstream.BatchPlayPlaylist = null;
        var e = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => PlaylistSource().GetSnapshotAsync(7, CancellationToken.None));

        StringAssert.Contains(e.Message, "reachable");
    }

    [TestMethod]
    public async Task A_playlist_arrives_already_resolved_to_its_files()
    {
        _upstream.BatchPlayPlaylist = new UpstreamPlaylistSnapshot(
            new BatchPlayPlaylistSnapshot("Evening", [new BatchPlayPlaylistEntry("/data/media/a.mkv", 42)]));

        var snapshot = await PlaylistSource().GetSnapshotAsync(7, CancellationToken.None);

        Assert.AreEqual("Evening", snapshot!.Name);
        Assert.AreEqual(42, snapshot.Entries[0].ResourceId);
    }
}
