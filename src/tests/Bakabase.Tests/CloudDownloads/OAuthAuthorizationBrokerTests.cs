using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Service.Components.OAuth;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class OAuthAuthorizationBrokerTests
{
    private const string Secret = "oauth-client-secret-canary";
    private const string Code = "oauth-code-canary";
    private const string Access = "oauth-access-canary";
    private const string Refresh = "oauth-refresh-canary";
    private string _root = null!;
    private OAuthProviderRegistry _registry = null!;
    private static readonly string ValidToken = JsonSerializer.Serialize(new
        {access_token = Access, refresh_token = Refresh, token_type = "Bearer", expires_in = 3600});

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "oauth-broker-" + Guid.NewGuid().ToString("N"));
        _registry = new([new MicrosoftGraphOAuthProvider()]);
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task AuthorizationUsesS256AndAnImmutableClientThenRedeemsOnlyOnceWithoutSecretReceipts()
    {
        var transport = new Transport();
        await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        OAuthAuthorizationGrant? received = null;
        var snapshot = Snapshot();
        var view = await broker.StartAsync("consumer", "question", snapshot, DateTimeOffset.UtcNow.AddMinutes(5),
            (grant, _) => { received = grant; return Task.CompletedTask; }, default);
        var url = new Uri(view.AuthorizationUrl!); var query = QueryHelpers.ParseQuery(url.Query);
        Assert.AreEqual("login.microsoftonline.com", url.Host); Assert.AreEqual("S256", query["code_challenge_method"].ToString());
        Assert.AreEqual(snapshot.CallbackUrl, query["redirect_uri"].ToString());
        Assert.AreEqual(snapshot.Scopes, query["scope"].ToString());
        Assert.AreEqual(43, query["state"].ToString().Length);
        Assert.IsFalse(view.AuthorizationUrl!.Contains(Secret));
        var same = await broker.StartAsync("consumer", "question", snapshot, view.ExpiresAt,
            (_, _) => throw new AssertFailedException("Idempotent start replaced the owner."), default);
        Assert.AreEqual(view.AuthorizationUrl, same.AuthorizationUrl);
        Assert.IsTrue(await broker.CompleteAsync(snapshot.ProviderId, MicrosoftGraphOAuthProvider.RedirectPath,
            Callback(view), default));
        Assert.IsNotNull(received); Assert.AreEqual(Secret, received.Client.ClientSecret);
        Assert.AreEqual(snapshot.ConfigurationVersion, received.Client.ConfigurationVersion);
        Assert.AreEqual(Access, JsonDocument.Parse(received.TokenJson).RootElement.GetProperty("access_token").GetString());
        var form = QueryHelpers.ParseQuery(transport.Body!);
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.AreEqual(query["code_challenge"].ToString(), challenge);
        Assert.AreEqual(Secret, form["client_secret"].ToString()); Assert.AreEqual(Code, form["code"].ToString());
        Assert.AreEqual(snapshot.TokenEndpoint, transport.Endpoint); Assert.AreEqual(1, transport.Requests);
        Assert.IsFalse(transport.HasOriginOrAuthorization);
        Assert.AreEqual("completed", (await broker.GetAsync("consumer", "question", default))!.Status);
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync(snapshot.ProviderId,
            MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
        Assert.AreEqual(1, transport.Requests);
        var receipt = await File.ReadAllTextAsync(Directory.GetFiles(_root).Single());
        foreach (var privateValue in new[] {Secret, Code, Access, Refresh, form["code_verifier"].ToString(), query["state"].ToString()})
            Assert.IsFalse(receipt.Contains(privateValue), "Receipt leaked authorization material.");
        Assert.IsTrue(receipt.Contains("completed"));
    }

    [TestMethod]
    public async Task MalformedOrForeignCallbacksDoNotConsumeTheCurrentAuthorization()
    {
        var transport = new Transport(); await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        var view = await Begin(broker);
        var query = Callback(view).ToDictionary(v => v.Key, v => v.Value);
        var invalid = new[]
        {
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", new string('a', 43)}, {"code", Code}}),
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", query["state"]}, {"code", ""}}),
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", query["state"]}, {"code", new StringValues([Code, Code])}}),
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", query["state"]}, {"code", Code}, {"unexpected", "x"}}),
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", query["state"]}, {"code", Code}, {"error", "denied"}})
        };
        foreach (var callback in invalid)
            await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, callback, default));
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync("notRegistered", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
        Assert.AreEqual("awaitingCallback", (await broker.GetAsync("consumer", "question", default))!.Status);
        Assert.AreEqual(0, transport.Requests);
        Assert.IsTrue(await broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
    }

    [TestMethod]
    public async Task MatchingAccessDeniedConsumesOnlyThatFlowWithoutMakingATokenRequest()
    {
        var transport = new Transport(); await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        var view = await Begin(broker); var state = Callback(view)["state"];
        Assert.IsFalse(await broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath,
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", state}, {"error", "access_denied"}, {"error_description", Secret}}), default));
        var final = (await broker.GetAsync("consumer", "question", default))!;
        Assert.AreEqual("failed", final.Status); Assert.AreEqual("OAUTH_AUTHORIZATION_DENIED", final.Problem);
        Assert.IsNull(final.AuthorizationUrl); Assert.AreEqual(0, transport.Requests);
        var retry = await Begin(broker); Assert.AreNotEqual(view.AuthorizationUrl, retry.AuthorizationUrl);
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
    }

    [DataTestMethod]
    [DataRow("{\"access_token\":\"x\",\"token_type\":\"Bearer\",\"expires_in\":60}", "OAUTH_TOKEN_RESPONSE_INVALID")]
    [DataRow("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"token_type\":\"Basic\",\"expires_in\":60}", "OAUTH_TOKEN_RESPONSE_INVALID")]
    [DataRow("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"token_type\":\"Bearer\",\"expires_in\":0}", "OAUTH_TOKEN_RESPONSE_INVALID")]
    [DataRow("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"token_type\":\"Bearer\",\"expires_in\":60,\"scope\":\"Files.ReadWrite.All\"}", "OAUTH_TOKEN_SCOPE_MISMATCH")]
    [DataRow("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"token_type\":\"Bearer\",\"expires_in\":60,\"scope\":5}", "OAUTH_TOKEN_SCOPE_MISMATCH")]
    public async Task InvalidTokensNeverReachNativeConfiguration(string response, string expected)
    {
        var transport = new Transport {Response = response}; await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        var view = await broker.StartAsync("consumer", "question", Snapshot(), DateTimeOffset.UtcNow.AddMinutes(5),
            (_, _) => throw new AssertFailedException("Invalid token was accepted."), default);
        Assert.IsFalse(await broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
        Assert.AreEqual(expected, (await broker.GetAsync("consumer", "question", default))!.Problem);
    }

    [TestMethod]
    public async Task CancelDuringExchangeNeverPublishesCredentialsAndCannotReviveByLateCompletion()
    {
        var transport = new Transport {Block = true}; await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        var published = false;
        var view = await broker.StartAsync("consumer", "question", Snapshot(), DateTimeOffset.UtcNow.AddMinutes(5),
            (_, _) => { published = true; return Task.CompletedTask; }, default);
        var callback = broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("cancelled", (await broker.StopAsync("consumer", default))!.Status);
        Assert.IsFalse(await callback.WaitAsync(TimeSpan.FromSeconds(2))); Assert.IsFalse(published);
        Assert.AreEqual("cancelled", (await broker.GetAsync("consumer", "question", default))!.Status);
    }

    [TestMethod]
    public async Task ExpiryAndRestartRejectOldStatesInsteadOfRestoringPersistedAuthorization()
    {
        var now = DateTimeOffset.UtcNow;
        await using var broker = new OAuthAuthorizationBroker(_root, _registry, new Transport(), () => now);
        var view = await Begin(broker);
        await using (var restarted = new OAuthAuthorizationBroker(_root, _registry, new Transport(), () => now))
            await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => restarted.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
        now = now.AddMinutes(30); await broker.CleanupExpiredAsync(default);
        Assert.IsNull(await broker.GetAsync("consumer", "question", default));
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(view), default));
    }

    [TestMethod]
    public async Task StopAndReplacementCanRaceWithoutDisposingAnActiveLifetimeOrRevivingOldState()
    {
        await using var broker = new OAuthAuthorizationBroker(_root, _registry, new Transport());
        for (var index = 0; index < 20; index++)
        {
            var previous = await broker.StartAsync("consumer", "old-" + index, Snapshot(), DateTimeOffset.UtcNow.AddMinutes(1), (_, _) => Task.CompletedTask, default);
            await Task.WhenAll(broker.StopAsync("consumer", default), broker.StartAsync("consumer", "new-" + index,
                Snapshot(), DateTimeOffset.UtcNow.AddMinutes(1), (_, _) => Task.CompletedTask, default));
            await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath, Callback(previous), default));
        }
    }

    [TestMethod]
    public async Task CallbackHtmlDoesNotReflectQueryOrExceptionDataAndDisablesCachingAndReferrers()
    {
        await using var broker = new OAuthAuthorizationBroker(_root, _registry, new Transport());
        var http = new DefaultHttpContext(); http.Request.Method = "GET"; http.Request.Path = MicrosoftGraphOAuthProvider.RedirectPath;
        http.Request.QueryString = new QueryString("?code=" + Secret + "&state=" + new string('x', 43));
        var controller = new OAuthCallbackController(broker) {ControllerContext = new ControllerContext {HttpContext = http}};
        var result = await controller.Complete("microsoftGraph", default);
        Assert.AreEqual(400, result.StatusCode); Assert.IsFalse(result.Content!.Contains(Secret));
        Assert.IsTrue(result.Content.Contains("OAUTH_CALLBACK_FAILED")); Assert.IsTrue(result.Content.Contains("Bakabase"));
        Assert.AreEqual("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("no-referrer", http.Response.Headers["Referrer-Policy"].ToString());
        Assert.IsTrue(http.Response.Headers["Content-Security-Policy"].ToString().Contains("default-src 'none'"));
    }

    [TestMethod]
    public async Task ArbitraryEndpointsAndExpandedScopesAreRejectedBeforeAnyNetworkRequest()
    {
        var transport = new Transport(); await using var broker = new OAuthAuthorizationBroker(_root, _registry, transport);
        foreach (var snapshot in new[] {Snapshot() with {TokenEndpoint = "https://attacker.invalid/token"}, Snapshot() with {Scopes = "Files.ReadWrite.All"}})
            await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => broker.StartAsync("consumer", "question", snapshot,
                DateTimeOffset.UtcNow.AddMinutes(1), (_, _) => Task.CompletedTask, default));
        Assert.AreEqual(0, transport.Requests);
    }

    private Task<OAuthAuthorizationView> Begin(OAuthAuthorizationBroker broker) => broker.StartAsync("consumer", "question", Snapshot(),
        DateTimeOffset.UtcNow.AddMinutes(5), (_, _) => Task.CompletedTask, default);
    private static QueryCollection Callback(OAuthAuthorizationView view) => new(new Dictionary<string, StringValues>()
    { {"state", QueryHelpers.ParseQuery(new Uri(view.AuthorizationUrl!).Query)["state"]}, {"code", Code} });
    private static OAuthClientSnapshot Snapshot() => new()
    {
        ProviderId = "microsoftGraph", ConfigurationVersion = Guid.NewGuid().ToString("N"),
        ClientId = "11111111-2222-3333-4444-555555555555", ClientSecret = Secret, TenantId = "common",
        CallbackUrl = "https://bakabase.example/oauth/callback/microsoftGraph", Scopes = new MicrosoftGraphOAuthProvider().Scopes,
        AuthEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token"
    };
    private sealed class Transport : HttpMessageHandler
    {
        public string Response { get; init; } = ValidToken;
        public bool Block { get; init; }
        public int Requests { get; private set; }
        public string? Body { get; private set; }
        public string? Endpoint { get; private set; }
        public bool HasOriginOrAuthorization { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; Endpoint = request.RequestUri!.AbsoluteUri; Body = await request.Content!.ReadAsStringAsync(ct);
            HasOriginOrAuthorization = request.Headers.Contains("Origin") || request.Headers.Authorization != null;
            Started.TrySetResult(); if (Block) await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK) {Content = new StringContent(Response)};
        }
    }
}
