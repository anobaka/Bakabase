using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.SourceAvailability.Probes;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class GoFileAvailabilityProbeTests
{
    private string _root = null!;
    private const string Credential = "fixtureanonymouscredential12345678";
    private static CloudSource Source => new() {Id = "source", ProviderId = "gofile", Url = "https://gofile.io/d/fixture"};
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "GoFileProbe-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [DataTestMethod]
    [DataRow(200, "error-notFound", "confirmedUnavailable", "SOURCE_SHARE_NOT_FOUND")]
    [DataRow(404, "error-notFound", "confirmedUnavailable", "SOURCE_SHARE_NOT_FOUND")]
    [DataRow(410, "error-notFound", "unknown", "SOURCE_RESPONSE_UNRECOGNIZED")]
    [DataRow(401, "error-notFound", "accessRequired", "SOURCE_ACCESS_REQUIRED")]
    [DataRow(403, "error-notFound", "accessRequired", "SOURCE_ACCESS_REQUIRED")]
    [DataRow(404, "not found", "unknown", "SOURCE_RESPONSE_UNRECOGNIZED")]
    [DataRow(404, "error-token", "unknown", "SOURCE_RESPONSE_UNRECOGNIZED")]
    [DataRow(401, "error-token", "unknown", "SOURCE_PROBE_SESSION_UNAVAILABLE")]
    [DataRow(401, "error-notPremium", "unknown", "SOURCE_PROBE_PROTOCOL_CHANGED")]
    [DataRow(429, "error-notFound", "temporarilyUnavailable", "SOURCE_RATE_OR_SERVICE_LIMIT")]
    [DataRow(503, "error-notFound", "temporarilyUnavailable", "SOURCE_RATE_OR_SERVICE_LIMIT")]
    [DataRow(200, "error-passwordRequired", "accessRequired", "SOURCE_ACCESS_CODE_REQUIRED")]
    public void OnlyExplicitContentEnvelopeEstablishesAbsence(int http, string code, string status, string reason)
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new {status = code, data = new {token = "never-leak"}}));
        var result = GoFileAvailabilityProbe.Classify((HttpStatusCode)http, body.RootElement, "fixture");
        Assert.AreEqual(status, result.Status); Assert.AreEqual(reason, result.ReasonCode);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("never-leak"));
    }

    [DataTestMethod]
    [DataRow("\"canAccess\":false,\"expire\":true", "confirmedUnavailable")]
    [DataRow("\"canAccess\":false,\"expire\":1700000000", "confirmedUnavailable")]
    [DataRow("\"canAccess\":false,\"expire\":true,\"password\":true", "accessRequired")]
    [DataRow("\"canAccess\":false,\"expire\":true,\"public\":false", "accessRequired")]
    [DataRow("\"canAccess\":false", "accessRequired")]
    [DataRow("\"canAccess\":true", "available")]
    [DataRow("\"expire\":true", "unknown")]
    public void WebsiteGateOrderPreservesAccessConditions(string fields, string expected)
    {
        using var body = JsonDocument.Parse("{\"status\":\"ok\",\"data\":{\"id\":\"fixture\",\"type\":\"folder\"," + fields + "}}");
        Assert.AreEqual(expected, GoFileAvailabilityProbe.Classify(HttpStatusCode.OK, body.RootElement, "fixture").Status);
        Assert.AreEqual("unknown", GoFileAvailabilityProbe.Classify(HttpStatusCode.OK, body.RootElement, "wrong-source").Status);
    }

    [TestMethod]
    public async Task ChecksReuseOnePrivateGuestAcrossConcurrencyAndNewInstancesAndOnlyReadOneMetadataPage()
    {
        var calls = new ConcurrentQueue<(string Method, string Path)>();
        var factory = new Factory(request =>
        {
            calls.Enqueue((request.Method.Method, request.RequestUri!.AbsolutePath));
            if (request.Method == HttpMethod.Post) return Reply(200, "{\"status\":\"ok\",\"data\":{\"token\":\"" + Credential + "\"}}");
            Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
            Assert.AreEqual(Credential, request.Headers.Authorization.Parameter);
            Assert.AreEqual("?page=1&pageSize=1", request.RequestUri.Query);
            Assert.AreEqual(new string('a', 64), request.Headers.GetValues("X-Website-Token").Single());
            return Reply(404, "{\"status\":\"error-notFound\"}");
        });
        var store = new SourceAvailabilityStore(_root);
        var probe = new GoFileAvailabilityProbe(factory, new(factory, store), new Signer());
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => probe.CheckAsync(Source, default)));
        Assert.IsTrue(results.All(r => r.Status == "confirmedUnavailable"));
        var restarted = new GoFileAvailabilityProbe(factory, new(factory, new SourceAvailabilityStore(_root)), new Signer());
        Assert.AreEqual("confirmedUnavailable", (await restarted.CheckAsync(Source, default)).Status);
        Assert.AreEqual(1, calls.Count(c => c.Method == "POST"));
        Assert.AreEqual(13, calls.Count(c => c.Method == "GET"));
        Assert.IsTrue(calls.All(c => c.Path is "/accounts" or "/contents/fixture"));
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Single()));
    }

    [TestMethod]
    public async Task RejectedPersistedGuestIsRenewedOnceButNewRejectionsNeverCreateAccountPerLink()
    {
        var store = new SourceAvailabilityStore(_root);
        await store.SaveSessionAsync("gofile.guest", new("expiredfixturecredential12345678", DateTimeOffset.UtcNow.AddDays(-1)), default);
        var created = 0;
        var factory = new Factory(request =>
        {
            if (request.Method == HttpMethod.Post)
            { Interlocked.Increment(ref created); return Reply(200, "{\"status\":\"ok\",\"data\":{\"token\":\"" + Credential + "\"}}"); }
            return Reply(401, "{\"status\":\"error-token\"}");
        });
        var probe = new GoFileAvailabilityProbe(factory, new(factory, store), new Signer());
        foreach (var _ in Enumerable.Range(0, 3)) Assert.AreEqual("unknown", (await probe.CheckAsync(Source, default)).Status);
        Assert.AreEqual(1, created);
    }

    [TestMethod]
    public async Task PasswordIsHashedAndNeverStoredInEvidence()
    {
        var store = new SourceAvailabilityStore(_root);
        await store.SaveSessionAsync("gofile.guest", new(Credential, DateTimeOffset.UtcNow), default);
        var factory = new Factory(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            StringAssert.Contains(request.RequestUri!.Query, "password=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("private-pass"))).ToLowerInvariant());
            return Reply(200, "{\"status\":\"error-passwordRequired\"}");
        });
        var source = Source with {AccessCode = "private-pass"};
        var result = await new GoFileAvailabilityProbe(factory, new(factory, store), new Signer()).CheckAsync(source, default);
        Assert.AreEqual("accessRequired", result.Status);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("private-pass"));
    }

    [TestMethod]
    public async Task InvalidWebsiteGeneratorStopsBeforeContentRequestAndDoesNotMarkSourceInvalid()
    {
        var store = new SourceAvailabilityStore(_root);
        await store.SaveSessionAsync("gofile.guest", new(Credential, DateTimeOffset.UtcNow), default);
        var factory = new Factory(_ => throw new AssertFailedException("No content request without a valid current signature"));
        var result = await new GoFileAvailabilityProbe(factory, new(factory, store), new BrokenSigner()).CheckAsync(Source, default);
        Assert.AreEqual("unknown", result.Status);
        Assert.AreEqual("SOURCE_PROBE_PROTOCOL_CHANGED", result.ReasonCode);
    }

    [TestMethod]
    public async Task NonJson404AndOversizedContentNeverProveShareAbsence()
    {
        var store = new SourceAvailabilityStore(_root);
        await store.SaveSessionAsync("gofile.guest", new(Credential, DateTimeOffset.UtcNow), default);
        foreach (var body in new[] {"<h1>404 not found</h1>", new string('x', 128 * 1024 + 1)})
        {
            var factory = new Factory(_ => Reply(404, body));
            var result = await new GoFileAvailabilityProbe(factory, new(factory, store), new Signer()).CheckAsync(Source, default);
            Assert.AreEqual("unknown", result.Status);
        }
    }

    [DataTestMethod]
    [DataRow("https://api.gofile.io/accounts", "GET")]
    [DataRow("https://api.gofile.io/contents/fixture", "DELETE")]
    [DataRow("https://api.gofile.io/contents/fixture", "POST")]
    [DataRow("https://gofile.io/js/wt.obf.js", "POST")]
    public async Task FixedTransportRejectsOtherMethodsBeforeAnyNetwork(string url, string method)
    {
        using var client = new HttpClient(GoFileMetadataHttp.Create(new WebProxy()));
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.SendAsync(request));
    }

    [DataTestMethod]
    [DataRow("https://gofile.io.attacker.test/d/fixture")]
    [DataRow("http://gofile.io/d/fixture")]
    [DataRow("https://user:password@gofile.io/d/fixture")]
    [DataRow("https://gofile.io:8443/d/fixture")]
    [DataRow("https://gofile.io/d/fixture/other")]
    [DataRow("https://gofile.io/d/fixture#unsupported")]
    public async Task UnsafeLocatorsAreRejectedBeforeSessionCreation(string url)
    {
        var factory = new Factory(_ => throw new AssertFailedException("No network request expected"));
        var source = Source with {Url = url};
        Assert.AreEqual("unknown", (await new GoFileAvailabilityProbe(factory, new(factory, new(_root)), new Signer()).CheckAsync(source, default)).Status);
    }

    [DataTestMethod]
    [DataRow("https://api.gofile.io/contents/fixture", true)]
    [DataRow("https://api.gofile.io/accounts", true)]
    [DataRow("https://gofile.io/js/wt.obf.js", true)]
    [DataRow("http://api.gofile.io/contents/fixture", false)]
    [DataRow("https://api.gofile.io.attacker.test/contents/fixture", false)]
    [DataRow("https://127.0.0.1/contents/fixture", false)]
    [DataRow("https://store1.gofile.io/download/web/file", false)]
    [DataRow("https://api.gofile.io/contents/fixture/update", false)]
    [DataRow("https://api.gofile.io/accounts/token", false)]
    public void FixedTransportDoesNotBroadenUserUrlOrPayloadAccess(string url, bool allowed) =>
        Assert.AreEqual(allowed, GoFileMetadataHttp.Allows(new(url)));

    private static HttpResponseMessage Reply(int code, string json) => new((HttpStatusCode)code)
        {Content = new StringContent(json, Encoding.UTF8, "application/json")};
    private sealed class Signer : IGoFileWebsiteToken
    { public Task<string> GenerateAsync(string anonymousCredential, CancellationToken ct) => Task.FromResult(new string('a', 64)); }
    private sealed class BrokenSigner : IGoFileWebsiteToken
    { public Task<string> GenerateAsync(string anonymousCredential, CancellationToken ct) => throw new GoFileProbeProtocolException(); }
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new Handler(send)); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request)); }
}
