using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class Aria2DownloaderTests
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
    private const string Secret = "local-test-secret";
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BakabaseAria2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    private static Aria2Downloader Downloader() =>
        new(new ClientFactory(), NullLogger<Aria2Downloader>.Instance);

    private static Aria2DownloadOptions Options(Server server, TimeSpan? timeout = null,
        TimeSpan? pollInterval = null) => new()
    {
        RpcUrl = server.Url,
        Secret = Secret,
        Timeout = timeout ?? TimeSpan.FromSeconds(10),
        PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(10)
    };

    [TestMethod]
    [Timeout(15000)]
    public async Task ACompletedMagnetFollowsItsPayloadAndKeepsFilesWithTheSameNameInTheirDirectories()
    {
        await using var server = new Server(Scenario.Complete);
        var progress = new List<int>();

        var result = await Downloader().DownloadMagnetAsync(Magnet, _directory, Options(server),
            (percent, _) => { progress.Add(percent); return Task.CompletedTask; }, CancellationToken.None);

        Assert.AreEqual(server.DownloadDirectory, result.Directory);
        Assert.IsTrue(Path.GetFullPath(result.Directory).StartsWith(
            Path.GetFullPath(_directory) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(Server.Payload.Keys.ToArray(), result.Files.Select(path =>
            Path.GetRelativePath(result.Directory, path).Replace(Path.DirectorySeparatorChar, '/')).ToArray());
        foreach (var (relativePath, expected) in Server.Payload)
            Assert.AreEqual(expected, await File.ReadAllTextAsync(Path.Combine(result.Directory, relativePath)));
        Assert.IsFalse(result.Files.Any(file => file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)),
            "the completed metadata job must not become the downloaded result");
        Assert.IsTrue(progress.Contains(25));
        Assert.AreEqual(100, progress.Last());
        CollectionAssert.AreEqual(new[] {Server.MetadataGid, Server.PayloadGid, Server.PayloadGid},
            server.Requests.Where(request => request.Method == "aria2.tellStatus")
                .Select(request => request.Gid).ToArray());
        Assert.AreEqual(0, server.Requests.Count(request => request.Method == "aria2.forceRemove"));
        AssertProtocol(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AProgressFailureAtMetadataCompletionStopsTheNewPayloadJob()
    {
        await using var server = new Server(Scenario.KeepPolling);
        var failure = new InvalidOperationException("The progress consumer failed.");

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Downloader().DownloadMagnetAsync(Magnet, _directory, Options(server),
                (_, _) => Task.FromException(failure), CancellationToken.None));

        Assert.AreSame(failure, actual);
        AssertCurrentDownloadWasStopped(server);
        AssertProtocol(server);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(15000)]
    public async Task CancellationDuringPollingOrAnIncompleteBodyStopsOnlyTheCurrentOwnedDownload(bool incompleteBody)
    {
        await using var server = new Server(incompleteBody ? Scenario.IncompleteBody : Scenario.KeepPolling);
        using var cancellation = new CancellationTokenSource();
        var payloadProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = Downloader().DownloadMagnetAsync(Magnet, _directory,
            Options(server, pollInterval: TimeSpan.FromMilliseconds(incompleteBody ? 10 : 500)),
            (percent, _) =>
            {
                if (percent == 25) payloadProgress.TrySetResult();
                return Task.CompletedTask;
            }, cancellation.Token);
        await (incompleteBody ? server.BodyStarted.Task : payloadProgress.Task).WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        try
        {
            await download.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("Cancellation must interrupt both the polling delay and an unfinished RPC response body.");
        }
        catch (OperationCanceledException) { }
        AssertCurrentDownloadWasStopped(server);
        AssertProtocol(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ATimeoutDuringTheRpcBodyStillStopsTheCurrentOwnedDownload()
    {
        await using var server = new Server(Scenario.IncompleteBody);
        var download = Downloader().DownloadMagnetAsync(Magnet, _directory,
            Options(server, timeout: TimeSpan.FromSeconds(1)), null, CancellationToken.None);
        await server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
            await download.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsTrue(download.IsCompleted, "the downloader's deadline, not the test guard, must end the operation");
        AssertCurrentDownloadWasStopped(server);
        AssertProtocol(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ACompletedDownloadCannotReturnAFileFromASiblingDirectory()
    {
        await using var server = new Server(Scenario.OutsideDirectory);

        try
        {
            await Downloader().DownloadMagnetAsync(Magnet, _directory, Options(server), null,
                CancellationToken.None);
            Assert.Fail("aria2's completed status must not allow a file outside the requested download directory.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Assert.IsFalse(ex is Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException);
        }

        Assert.IsNotNull(server.OutsideFile);
        Assert.AreEqual("unrelated file", await File.ReadAllTextAsync(server.OutsideFile));
        AssertCurrentDownloadWasStopped(server);
        AssertProtocol(server);
    }

    private static void AssertCurrentDownloadWasStopped(Server server) =>
        CollectionAssert.AreEqual(new[] {Server.PayloadGid}, server.Requests
                .Where(request => request.Method == "aria2.forceRemove").Select(request => request.Gid).ToArray(),
            "cleanup must use its own token and stop only the payload GID returned by this magnet's metadata job");

    private static void AssertProtocol(Server server)
    {
        Assert.AreEqual(0, server.Errors.Count, string.Join(Environment.NewLine, server.Errors));
        var requests = server.Requests.ToArray();
        Assert.AreEqual(1, requests.Count(request => request.Method == "aria2.addUri"));
        foreach (var request in requests)
        {
            Assert.AreEqual("token:" + Secret, request.Body["params"]![0]!.GetValue<string>());
            Assert.IsTrue(request.Method is "aria2.addUri" or "aria2.tellStatus" or "aria2.forceRemove",
                "the downloader must never stop unrelated daemon jobs");
            if (request.Method != "aria2.addUri")
                Assert.IsTrue(request.Gid is Server.MetadataGid or Server.PayloadGid);
        }
        var addParameters = requests.Single(request => request.Method == "aria2.addUri").Body["params"]!.AsArray();
        Assert.AreEqual(Magnet, addParameters[1]![0]!.GetValue<string>());
        Assert.AreEqual("0", addParameters[2]!["seed-time"]!.GetValue<string>(),
            "the completed download must release its files rather than keep seeding them");
    }

    private sealed class ClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() {Timeout = System.Threading.Timeout.InfiniteTimeSpan};
    }

    private enum Scenario {Complete, KeepPolling, IncompleteBody, OutsideDirectory}

    private sealed record RpcRequest(string Method, string? Gid, JsonObject Body);

    /// <summary>
    /// A loopback JSON-RPC peer. It writes the files an aria2 daemon would write and serves real
    /// HTTP responses, including a deliberately unfinished body; no remote torrent is requested.
    /// </summary>
    private sealed class Server : IAsyncDisposable
    {
        public const string MetadataGid = "0123456789abcdef";
        public const string PayloadGid = "fedcba9876543210";
        public static readonly IReadOnlyDictionary<string, string> Payload = new Dictionary<string, string>
        {
            ["release/disc1/payload.bin"] = "first disc content",
            ["release/disc2/nested/payload.bin"] = "second disc content"
        };

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentBag<Task> _handlers = [];
        private readonly Task _requests;
        private readonly Scenario _scenario;
        private int _payloadPolls;
        public readonly ConcurrentQueue<RpcRequest> Requests = [];
        public readonly ConcurrentQueue<Exception> Errors = [];
        public readonly TaskCompletionSource BodyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; }
        public string DownloadDirectory { get; private set; } = null!;
        public string? OutsideFile { get; private set; }

        public Server(Scenario scenario)
        {
            _scenario = scenario;
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            var number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            Url = $"http://127.0.0.1:{number}/jsonrpc";
            _listener.Prefixes.Add($"http://127.0.0.1:{number}/");
            _listener.Start();
            _requests = ServeAsync();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                _handlers.Add(HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream);
                var body = JsonNode.Parse(await reader.ReadToEndAsync(_stopping.Token))!.AsObject();
                var method = body["method"]!.GetValue<string>();
                var parameters = body["params"]!.AsArray();
                var gid = method == "aria2.addUri" ? null : parameters[1]!.GetValue<string>();
                Requests.Enqueue(new RpcRequest(method, gid, body));
                JsonNode result;
                switch (method)
                {
                    case "aria2.addUri":
                        DownloadDirectory = parameters[2]!["dir"]!.GetValue<string>();
                        Directory.CreateDirectory(DownloadDirectory);
                        result = JsonValue.Create(MetadataGid)!;
                        break;
                    case "aria2.forceRemove":
                        result = JsonValue.Create(gid)!;
                        break;
                    case "aria2.tellStatus" when gid == MetadataGid:
                        var metadataPath = Path.Combine(DownloadDirectory, "magnet-metadata.torrent");
                        await File.WriteAllTextAsync(metadataPath, "metadata, not payload", _stopping.Token);
                        result = Status(MetadataGid, "complete", 100, [metadataPath], PayloadGid);
                        break;
                    case "aria2.tellStatus" when gid == PayloadGid:
                        var poll = Interlocked.Increment(ref _payloadPolls);
                        if (_scenario == Scenario.IncompleteBody)
                        {
                            await IncompleteBodyAsync(context.Response, body["id"]);
                            return;
                        }
                        if (_scenario == Scenario.KeepPolling || _scenario == Scenario.Complete && poll == 1)
                        {
                            result = Status(PayloadGid, "active", 25, []);
                            break;
                        }
                        if (_scenario == Scenario.OutsideDirectory)
                        {
                            // This shares the requested directory's text prefix but is a sibling.
                            OutsideFile = Path.Combine(DownloadDirectory + "-unrelated", "payload.bin");
                            Directory.CreateDirectory(Path.GetDirectoryName(OutsideFile)!);
                            await File.WriteAllTextAsync(OutsideFile, "unrelated file", _stopping.Token);
                            result = Status(PayloadGid, "complete", 100, [OutsideFile]);
                            break;
                        }
                        foreach (var (relativePath, content) in Payload)
                        {
                            var path = Path.Combine(DownloadDirectory, relativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            await File.WriteAllTextAsync(path, content, _stopping.Token);
                        }
                        result = Status(PayloadGid, "complete", 100,
                            Payload.Keys.Select(path => Path.Combine(DownloadDirectory, path)).ToArray());
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected RPC request: {method} ({gid}).");
                }
                await ReplyAsync(context.Response, body["id"], result);
            }
            catch (IOException) { context.Response.Abort(); }
            catch (HttpListenerException) { context.Response.Abort(); }
            catch (ObjectDisposedException) { context.Response.Abort(); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { context.Response.Abort(); }
            catch (Exception ex)
            {
                Errors.Enqueue(ex);
                context.Response.Abort();
            }
        }

        private static JsonObject Status(string gid, string state, int percent, string[] files,
            string? followedBy = null)
        {
            var result = new JsonObject
            {
                ["gid"] = gid,
                ["status"] = state,
                ["completedLength"] = percent.ToString(),
                ["totalLength"] = "100",
                ["files"] = new JsonArray(files.Select(path => (JsonNode)new JsonObject {["path"] = path}).ToArray())
            };
            if (followedBy != null) result["followedBy"] = new JsonArray(JsonValue.Create(followedBy));
            return result;
        }

        private static byte[] ResponseBytes(JsonNode? id, JsonNode result) => Encoding.UTF8.GetBytes(
            new JsonObject {["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result}.ToJsonString());

        private async Task ReplyAsync(HttpListenerResponse response, JsonNode? id, JsonNode result)
        {
            var bytes = ResponseBytes(id, result);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, _stopping.Token);
            response.Close();
        }

        private async Task IncompleteBodyAsync(HttpListenerResponse response, JsonNode? id)
        {
            var bytes = ResponseBytes(id, Status(PayloadGid, "active", 25, []));
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes.AsMemory(0, 8), _stopping.Token);
            await response.OutputStream.FlushAsync(_stopping.Token);
            BodyStarted.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, _stopping.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            _listener.Close();
            await _requests;
            await Task.WhenAll(_handlers);
            _stopping.Dispose();
        }
    }
}
