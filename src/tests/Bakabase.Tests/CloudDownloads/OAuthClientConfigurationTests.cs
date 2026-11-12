using System.Text.Json;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.OAuth;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class OAuthClientConfigurationTests
{
    private const string Provider = MicrosoftGraphOAuthProvider.ProviderId;
    private const string Client = "8104d443-43df-4d4f-b071-a241b1e4c73a";
    private const string OtherClient = "11b0c365-0803-436b-81af-36fbe106be0e";
    private const string Secret = "private-client-secret-canary";
    private const string Callback = "https://bakabase.example/oauth/callback/microsoftGraph";
    private string _root = null!;
    private OAuthProviderRegistry _registry = null!;
    private OAuthClientConfigurationStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "oauth-configuration-" + Guid.NewGuid().ToString("N"));
        _registry = new([new MicrosoftGraphOAuthProvider()]); _store = new(_root, _registry);
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task ReadingTheDefaultManualConfigurationDoesNotWriteOrCreatePrivateStorage()
    {
        var view = await _store.GetAsync(Provider, default);
        Assert.AreEqual("manual", view.Mode); Assert.AreEqual("common", view.TenantId);
        Assert.IsFalse(view.IsReady); Assert.IsFalse(view.HasClientSecret); Assert.IsNull(view.Problem);
        Assert.IsNull(await _store.CaptureAsync(Provider, default)); Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task IncompleteAutomaticConfigurationIsSavedButCannotSilentlyUseTheManualFlow()
    {
        var view = await _store.SaveAsync(Provider, new() {Mode = "automatic"}, default);
        Assert.IsFalse(view.IsReady); Assert.AreEqual("OAUTH_CONFIGURATION_NOT_READY", view.Problem);
        var error = await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.CaptureAsync(Provider, default));
        Assert.AreEqual("OAUTH_CONFIGURATION_NOT_READY", error.Code);
        Assert.AreEqual("automatic", (await new OAuthClientConfigurationStore(_root, _registry).GetAsync(Provider, default)).Mode);
    }

    [TestMethod]
    public async Task PrivateSnapshotUsesOnlyReviewedEndpointsAndDoesNotLeakTheSecretInPublicViewsOrDiagnostics()
    {
        var view = await _store.SaveAsync(Provider, Complete(), default);
        Assert.IsTrue(view.IsReady); Assert.IsTrue(view.HasClientSecret);
        var serialized = JsonSerializer.Serialize(view);
        Assert.IsFalse(serialized.Contains(Secret));
        using (var json = JsonDocument.Parse(serialized)) Assert.IsFalse(json.RootElement.TryGetProperty("ClientSecret", out _));
        var snapshot = (await _store.CaptureAsync(Provider, default))!;
        Assert.AreEqual(Client, snapshot.ClientId); Assert.AreEqual(Secret, snapshot.ClientSecret);
        Assert.AreEqual("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", snapshot.AuthEndpoint);
        Assert.AreEqual("https://login.microsoftonline.com/common/oauth2/v2.0/token", snapshot.TokenEndpoint);
        Assert.AreEqual("Files.Read Files.Read.All Sites.Read.All offline_access", snapshot.Scopes);
        Assert.IsFalse(snapshot.ToString().Contains(Secret));
        Assert.IsFalse(snapshot.ToString().Contains(Callback));
        Assert.IsFalse(Complete().ToString()!.Contains(Secret));
        Assert.IsTrue(Guid.TryParseExact(snapshot.ConfigurationVersion, "N", out _));
    }

    [TestMethod]
    public async Task OmittedAndEmptySecretsPreserveOnlyTheSameApplicationWhileExplicitClearIsWriteOnly()
    {
        await _store.SaveAsync(Provider, Complete(), default);
        var original = (await _store.CaptureAsync(Provider, default))!;
        await _store.SaveAsync(Provider, new() {Mode = "automatic"}, default);
        var omitted = (await _store.CaptureAsync(Provider, default))!;
        Assert.AreEqual(Secret, omitted.ClientSecret); Assert.AreNotEqual(original.ConfigurationVersion, omitted.ConfigurationVersion);
        await _store.SaveAsync(Provider, new() {Mode = "automatic", ClientSecret = ""}, default);
        Assert.AreEqual(Secret, (await _store.CaptureAsync(Provider, default))!.ClientSecret);
        var cleared = await _store.SaveAsync(Provider, new() {Mode = "automatic", ClearClientSecret = true}, default);
        Assert.IsFalse(cleared.HasClientSecret); Assert.IsFalse(cleared.IsReady);
        Assert.IsFalse((await File.ReadAllTextAsync(Path.Combine(_root, Provider + ".json"))).Contains(Secret));
        Assert.AreEqual(1, Directory.GetFiles(_root).Length, "Atomic writes must not retain temporary secret copies.");
    }

    [TestMethod]
    public async Task ChangingApplicationWithoutItsNewSecretClearsTheOldCredentialInsteadOfMisBindingIt()
    {
        await _store.SaveAsync(Provider, Complete(), default);
        var before = (await _store.CaptureAsync(Provider, default))!;
        var changed = await _store.SaveAsync(Provider, new() {Mode = "automatic", ClientId = OtherClient}, default);
        Assert.AreEqual(OtherClient, changed.ClientId); Assert.IsFalse(changed.HasClientSecret); Assert.IsFalse(changed.IsReady);
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.CaptureAsync(Provider, default));
        Assert.AreEqual(Client, before.ClientId); Assert.AreEqual(Secret, before.ClientSecret, "An in-flight snapshot is immutable.");
        var repaired = await _store.SaveAsync(Provider, new() {Mode = "automatic", ClientSecret = "new-secret-canary"}, default);
        Assert.IsTrue(repaired.IsReady); Assert.AreEqual("new-secret-canary", (await _store.CaptureAsync(Provider, default))!.ClientSecret);
    }

    [TestMethod]
    public async Task SwitchingToManualDoesNotExposeOrDeleteTheConfiguredApplicationAndNeverCapturesIt()
    {
        await _store.SaveAsync(Provider, Complete(), default);
        var manual = await _store.SaveAsync(Provider, new() {Mode = "manual"}, default);
        Assert.IsTrue(manual.HasClientSecret); Assert.IsFalse(manual.IsReady); Assert.IsNull(manual.Problem);
        Assert.IsNull(await _store.CaptureAsync(Provider, default));
        Assert.IsTrue((await _store.SaveAsync(Provider, new() {Mode = "automatic"}, default)).IsReady);
    }

    [DataTestMethod]
    [DataRow("common")]
    [DataRow("organizations")]
    [DataRow("consumers")]
    [DataRow("0bf91d93-58d5-4795-8a33-7fbf9cbd2fde")]
    public async Task ReviewedTenantIdentifiersNeverBecomeArbitraryEndpointUrls(string tenant)
    {
        await _store.SaveAsync(Provider, Complete(tenant: tenant), default);
        var snapshot = (await _store.CaptureAsync(Provider, default))!;
        Assert.AreEqual("https://login.microsoftonline.com/" + tenant + "/oauth2/v2.0/token", snapshot.TokenEndpoint);
    }

    [DataTestMethod]
    [DataRow("https://attacker.invalid", "OAUTH_TENANT_INVALID")]
    [DataRow("common/../../consumers", "OAUTH_TENANT_INVALID")]
    [DataRow("common?client_secret=private-client-secret-canary", "OAUTH_TENANT_INVALID")]
    [DataRow("{0bf91d93-58d5-4795-8a33-7fbf9cbd2fde}", "OAUTH_TENANT_INVALID")]
    public async Task RejectedTenantCannotAlterAnExistingConfigurationOrEchoTheInput(string tenant, string code)
    {
        await _store.SaveAsync(Provider, Complete(), default);
        var before = (await _store.CaptureAsync(Provider, default))!;
        var error = await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.SaveAsync(Provider, Complete(tenant: tenant), default));
        Assert.AreEqual(code, error.Code); Assert.IsFalse(error.ToString().Contains(Secret));
        Assert.AreEqual(before.ConfigurationVersion, (await _store.CaptureAsync(Provider, default))!.ConfigurationVersion);
    }

    [DataTestMethod]
    [DataRow("http://bakabase.example/oauth/callback/microsoftGraph")]
    [DataRow("https://user:password@bakabase.example/oauth/callback/microsoftGraph")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph?state=private-client-secret-canary")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph#state")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph?")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph#")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph/")]
    [DataRow("https://bakabase.example/oauth/callback/microsoftGraph/extra")]
    [DataRow("https://bakabase.example/oauth/callback/microsoft%47raph")]
    [DataRow("https://bakabase.example/ignored/../oauth/callback/microsoftGraph")]
    [DataRow("https://bakabase.example/ignored/%2e%2e/oauth/callback/microsoftGraph")]
    [DataRow("https://bakabase.example/oauth/callback/unknown")]
    public async Task FixedCallbackRejectsUnregisteredOrNormalizedAwayPathVariations(string callback)
    {
        var error = await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.SaveAsync(Provider, Complete(callback: callback), default));
        Assert.AreEqual("OAUTH_CALLBACK_URL_INVALID", error.Code); Assert.IsFalse(error.ToString().Contains(Secret));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task InvalidModesClientIdentifiersAndAmbiguousSecretChangesAreStableAndDoNotMutateStorage()
    {
        await _store.SaveAsync(Provider, Complete(), default);
        var before = (await _store.CaptureAsync(Provider, default))!;
        foreach (var input in new OAuthProviderConfigurationInputModel[]
                 {
                     new() {Mode = "unknown"}, new() {ClientId = "not-a-guid-" + Secret},
                     new() {ClearClientSecret = true, ClientSecret = Secret},
                     new() {ClientSecret = "\n" + Secret}, new() {ClientSecret = "  "}
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.SaveAsync(Provider, input, default));
            Assert.IsFalse(error.ToString().Contains(Secret));
            Assert.AreEqual(before.ConfigurationVersion, (await _store.CaptureAsync(Provider, default))!.ConfigurationVersion);
        }
    }

    [DataTestMethod]
    [DataRow("GET", "/oauth/callback/microsoftGraph", true)]
    [DataRow("POST", "/oauth/callback/microsoftGraph", false)]
    [DataRow("DELETE", "/oauth/callback/microsoftGraph", false)]
    [DataRow("GET", "/oauth/callback/microsoftGraph/", false)]
    [DataRow("GET", "/oauth/callback/microsoftGraph/extra", false)]
    [DataRow("GET", "/oauth/callback/microsoft%47raph", false)]
    [DataRow("GET", "/oauth/callback/unknown", false)]
    [DataRow("GET", "/oauth/configuration/microsoftGraph", false)]
    [DataRow("GET", "/rclone/accounts", false)]
    public void PublicIngressMatchesOnlyTheReviewedMethodAndWholeCallbackPath(string method, string path, bool allowed)
    {
        var context = new DefaultHttpContext(); context.Request.Method = method; context.Request.Path = new PathString(path);
        Assert.AreEqual(allowed, OAuthProviderRegistry.IsCallbackRequest(context.Request));
    }

    [TestMethod]
    public async Task UnknownProvidersCannotEscapeThePrivateRootOrCreateConfigurationFiles()
    {
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.GetAsync("../accounts", default));
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.SaveAsync("unknown", Complete(), default));
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => _store.CaptureAsync("unknown", default));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task AnotherProviderOwnsItsClientAndTenantSyntaxInsteadOfInheritingMicrosoftRestrictions()
    {
        var provider = new ExampleProvider();
        var store = new OAuthClientConfigurationStore(_root, new([provider]));
        var view = await store.SaveAsync(provider.Id, new()
        {
            Mode = "automatic", ClientId = "opaque-client-not-a-guid", ClientSecret = Secret,
            CallbackUrl = "https://bakabase.example" + provider.CallbackPath
        }, default);
        Assert.IsTrue(view.IsReady); Assert.AreEqual("", view.TenantId);
        var snapshot = (await store.CaptureAsync(provider.Id, default))!;
        Assert.AreEqual("opaque-client-not-a-guid", snapshot.ClientId);
        Assert.AreEqual("https://provider.example/token", snapshot.TokenEndpoint);
        Assert.AreEqual("read", snapshot.Scopes);
        Assert.IsFalse(OAuthProviderRegistry.IsRegisteredCallbackPath(provider.CallbackPath),
            "Adding a config provider must not silently expand the reviewed public ingress allowlist.");
    }

    [DataTestMethod]
    [DataRow("Files.Read Files.Read.All Sites.Read.All", true)]
    [DataRow("https://graph.microsoft.com/Files.Read https://graph.microsoft.com/Sites.Read.All", true)]
    [DataRow("Files.Read offline_access", true)]
    [DataRow("Files.ReadWrite", false)]
    [DataRow("Files.Read https://attacker.invalid/Files.Read", false)]
    [DataRow("Files.Read User.Read", true)]
    [DataRow("Files.Read https://graph.microsoft.com/User.Read openid profile email", true)]
    [DataRow("Files.Read User.ReadWrite", false)]
    [DataRow("Files.Read User.Read.All", false)]
    [DataRow("Files.Read https://attacker.invalid/User.Read", false)]
    [DataRow("https://graph.microsoft.com/.default", false)]
    [DataRow("", false)]
    public void ReturnedMicrosoftScopesPermitOnlyTheRequestedReadOnlyPermissions(string scopes, bool allowed) =>
        Assert.AreEqual(allowed, new MicrosoftGraphOAuthProvider().AreReturnedScopesAllowed(scopes));

    [TestMethod]
    public async Task ConcurrentSavesAreAtomicAndThePrivateRecordSurvivesStoreRestart()
    {
        await _store.SaveAsync(Provider, Complete(), default);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => _store.SaveAsync(Provider,
            new() {Mode = "automatic", TenantId = i % 2 == 0 ? "common" : "organizations"}, default)));
        var restarted = new OAuthClientConfigurationStore(_root, _registry);
        var snapshot = (await restarted.CaptureAsync(Provider, default))!;
        Assert.AreEqual(Secret, snapshot.ClientSecret); Assert.AreEqual(Client, snapshot.ClientId);
        Assert.IsTrue(snapshot.TenantId is "common" or "organizations");
        Assert.AreEqual(1, Directory.GetFiles(_root).Length);
        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_root));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_root, Provider + ".json")));
        }
    }

    [TestMethod]
    public async Task ALinkInThePrivateConfigurationPathCannotReadOrReplaceAnotherFile()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Symlink permission varies on Windows; lexical callback and provider checks remain portable.");
        Directory.CreateDirectory(_root);
        var external = Path.Combine(_root, "unrelated.json"); await File.WriteAllTextAsync(external, "keep-unchanged");
        var linkedRoot = Path.Combine(_root, "linked"); Directory.CreateDirectory(linkedRoot);
        File.CreateSymbolicLink(Path.Combine(linkedRoot, Provider + ".json"), external);
        var store = new OAuthClientConfigurationStore(linkedRoot, _registry);
        var read = await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => store.GetAsync(Provider, default));
        Assert.AreEqual("OAUTH_STORAGE_PATH_INVALID", read.Code);
        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(() => store.SaveAsync(Provider, Complete(), default));
        Assert.AreEqual("keep-unchanged", await File.ReadAllTextAsync(external));
    }

    private static OAuthProviderConfigurationInputModel Complete(string? tenant = null, string? callback = null) => new()
    {Mode = "automatic", ClientId = Client, ClientSecret = Secret, TenantId = tenant ?? "common", CallbackUrl = callback ?? Callback};

    private sealed class ExampleProvider : IOAuthProvider
    {
        public string Id => "exampleProvider";
        public string Scopes => "read";
        public string CallbackPath => "/oauth/callback/exampleProvider";
        public string ProxyToolId => DownloadToolProxyTargets.Rclone;
        public ThirdPartyId ProxyThirdPartyId => ThirdPartyId.OneDrive;
        public string? NormalizeClientId(string? clientId) => string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        public string NormalizeTenant(string? tenantId) => tenantId?.Trim() ?? "";
        public Uri GetAuthorizationEndpoint(string tenantId) => new("https://provider.example/authorize");
        public Uri GetTokenEndpoint(string tenantId) => new("https://provider.example/token");
        public bool AreReturnedScopesAllowed(string scopes) => scopes == "read";
    }
}
