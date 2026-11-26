using System.Net;
using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakCliPurgeClientTests
{
    private string _profile = null!;
    private readonly string _identity = PikPakCliStorage.Hash("fixture-user");

    private readonly JsonElement _status = JsonSerializer.SerializeToElement(new {logged_in = true, user_id = "fixture-user"});

    [TestInitialize]
    public void Setup()
    {
        _profile = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pikpak-purge-" + Guid.NewGuid().ToString("N"))).FullName;
        SaveCredential(null, DateTimeOffset.UtcNow.AddHours(1));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_profile, true);

    [TestMethod]
    public async Task SendsOnlyBoundFileIdsAndAccessTokenToFixedEndpoint()
    {
        using var handler = PikPakCliPurgeClient.CreateHandler(new WebProxy());
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseCookies);
        var calls = 0;
        var client = Client(async (request, ct) =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://api-drive.mypikpak.com/drive/v1/files:batchDelete", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Bearer fixture-access", request.Headers.Authorization?.ToString());
            Assert.AreEqual("fixture-device", request.Headers.GetValues("X-Device-Id").Single());
            var payload = await request.Content!.ReadAsStringAsync(ct);
            using var body = JsonDocument.Parse(payload);
            CollectionAssert.AreEqual(new[] {"file-1", "file_2"}, body.RootElement.GetProperty("ids")
                .EnumerateArray().Select(v => v.GetString()!).ToArray());
            Assert.IsFalse((payload + request.Headers).Contains("private-refresh-canary"));
            return new(HttpStatusCode.OK) {Content = new StringContent("{}")};
        });
        await client.DeleteFilesAsync(_profile, _identity, _status, ["file-1", "file_2"], default);
        Assert.AreEqual(1, calls);
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.OK, "{\"error\":\"captcha_required\"}", "PIKPAK_VERIFICATION_REQUIRED")]
    [DataRow(HttpStatusCode.Forbidden, "{\"error\":\"insufficient_scope\"}", "PIKPAK_PURGE_FILES_SCOPE_REQUIRED")]
    [DataRow(HttpStatusCode.TemporaryRedirect, "external-url-canary", "PIKPAK_PURGE_UNAVAILABLE")]
    [DataRow(HttpStatusCode.ServiceUnavailable, "upstream-error-canary", "PIKPAK_NETWORK_UNAVAILABLE")]
    public async Task ReturnsOnlyClassifiedFailures(HttpStatusCode status, string response, string code)
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(status) {Content = new StringContent(response)}));
        var error = await Assert.ThrowsExceptionAsync<PikPakCliException>(() =>
            client.DeleteFilesAsync(_profile, _identity, _status, ["own-file"], default));
        Assert.AreEqual(code, error.Code);
    }

    [TestMethod]
    public async Task MismatchedProfileExpiredCredentialsAndEmptyOrInvalidIdsNeverSend()
    {
        var client = Client((_, _) => throw new AssertFailedException("Unexpected request"));
        var otherStatus = JsonSerializer.SerializeToElement(new {logged_in = true, user_id = "other-user"});
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", (await Assert.ThrowsExceptionAsync<PikPakCliException>(() =>
            client.DeleteFilesAsync(_profile, _identity, otherStatus, ["one"], default))).Code);
        SaveCredential("other-user", DateTimeOffset.UtcNow.AddHours(1));
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", (await Assert.ThrowsExceptionAsync<PikPakCliException>(() =>
            client.DeleteFilesAsync(_profile, _identity, _status, ["one"], default))).Code);
        SaveCredential("fixture-user", DateTimeOffset.UtcNow.AddHours(-1));
        Assert.AreEqual("PIKPAK_ACCOUNT_REQUIRED", (await Assert.ThrowsExceptionAsync<PikPakCliException>(() =>
            client.DeleteFilesAsync(_profile, _identity, _status, ["one"], default))).Code);
        foreach (var ids in new[] {Array.Empty<string>(), ["*"], ["one", "one"]})
            Assert.AreEqual("PIKPAK_REMOTE_MANIFEST_INVALID", (await Assert.ThrowsExceptionAsync<PikPakCliException>(() =>
                client.DeleteFilesAsync(_profile, _identity, _status, ids, default))).Code);
    }

    private static PikPakCliPurgeClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(() => new HttpClient(new Handler(send)));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    private void SaveCredential(string? userId, DateTimeOffset expires) =>
        File.WriteAllText(Path.Combine(_profile, "credentials.json"), JsonSerializer.Serialize(new
        {
            user_id = userId, token_expiry = expires, access_token = "fixture-access", refresh_token = "private-refresh-canary",
            client_id = "fixture-client", device_id = "fixture-device"
        }));
}
