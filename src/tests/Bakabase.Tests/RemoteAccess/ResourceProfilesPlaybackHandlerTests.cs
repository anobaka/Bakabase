using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class ResourceProfilesPlaybackHandlerTests
{
    private string _directory = null!;
    private ActiveConnection _connection = null!;
    private Profiles _profiles = null!;

    private sealed class DataDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class Profiles : ISourceResourceProfilePlaybackService
    {
        public List<(string Source, string? Epoch, int[] Ids)> Calls { get; } = [];
        public Exception? Failure { get; set; }

        public async Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
            int resourceId, CancellationToken cancellationToken = default) =>
            (await ResolveManyAsync(sourceDeviceId, libraryEpoch, [resourceId], cancellationToken))[resourceId];

        public Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
            string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((sourceDeviceId, libraryEpoch, resourceIds.ToArray()));
            if (Failure != null) throw Failure;
            return Task.FromResult<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>>(
                resourceIds.ToDictionary(id => id, _ => new ResourceProfileOpeningOptions
                {
                    OpeningMode = "native", ProfileIds = [7],
                    PlayerOptions = new() {Players = [new()
                        {ExecutablePath = "private-local-viewer.exe", Command = "--private-argument {0}"}]}
                }));
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bakabase-profile-handler", Guid.NewGuid().ToString("N"));
        _connection = new ActiveConnection(new ClientConnectionStore(new DataDirectory(_directory)));
        await _connection.SaveAsync("source-one", "Source", "http://192.168.1.8:34567",
            new ClientCredentials("client", "key"), DateTime.UtcNow);
        _profiles = new();
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    private async Task<(DefaultHttpContext Context, string Body)> Invoke(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Response.Body = new MemoryStream();
        await new ResourceProfilesPlaybackHandler(_connection, _profiles).HandleAsync(context,
            new Dictionary<string, string>());
        context.Response.Body.Position = 0;
        return (context, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [TestMethod]
    public async Task BatchesBindToTheRelaySourceAndForwardEpochWithoutExposingLocalPlayers()
    {
        var (context, body) = await Invoke("""
            {"sourceDeviceId":"source-one","libraryEpoch":"library-one","resourceIds":[41,42,41]}
            """);
        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual(1, _profiles.Calls.Count);
        var call = _profiles.Calls.Single();
        Assert.AreEqual("source-one", call.Source);
        Assert.AreEqual("library-one", call.Epoch);
        CollectionAssert.AreEqual(new[] {41, 42}, call.Ids);
        using var document = JsonDocument.Parse(body);
        var results = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.AreEqual(2, results.Length);
        CollectionAssert.AreEqual(new[] {41, 42}, results.Select(r => r.GetProperty("resourceId").GetInt32()).ToArray());
        Assert.IsTrue(results.All(r => r.GetProperty("openingMode").GetString() == "native"));
        Assert.IsFalse(body.Contains("private-", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("playerOptions", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"sourceDeviceId\":\"other-source\",\"resourceIds\":[41]}")]
    [DataRow("{\"resourceIds\":[41]}")]
    [DataRow("{\"sourceDeviceId\":\"source-one\",\"resourceIds\":[]}")]
    [DataRow("{\"sourceDeviceId\":\"source-one\",\"resourceIds\":[0]}")]
    [DataRow("{not-json")]
    public async Task InvalidSourceOrResourceRequestsNeverReachTheLocalProfiles(string json)
    {
        var (context, _) = await Invoke(json);
        Assert.AreEqual(400, context.Response.StatusCode);
        Assert.AreEqual(0, _profiles.Calls.Count);
    }

    [TestMethod]
    public async Task ResourceCountAndActualBodyBytesAreBothBounded()
    {
        var (tooMany, _) = await Invoke(JsonSerializer.Serialize(new
            {sourceDeviceId = "source-one", resourceIds = Enumerable.Range(1, 257).ToArray()}));
        Assert.AreEqual(400, tooMany.Response.StatusCode);
        // Content-Length is intentionally absent: streamed/chunked input must have the same limit.
        var (oversized, _) = await Invoke(new string(' ', 16 * 1024 + 1));
        Assert.AreEqual(413, oversized.Response.StatusCode);
        Assert.AreEqual(0, _profiles.Calls.Count);
    }

    [TestMethod]
    public async Task FailedSourceEvaluationDoesNotExposeSensitiveLocalFailureDetails()
    {
        _profiles.Failure = new InvalidOperationException("private-local-viewer.exe contains private-argument");
        var (context, body) = await Invoke("""
            {"sourceDeviceId":"source-one","resourceIds":[41]}
            """);
        Assert.AreEqual(409, context.Response.StatusCode);
        Assert.IsFalse(body.Contains("private-", StringComparison.Ordinal));
    }
}
