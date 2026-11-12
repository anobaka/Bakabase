using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.WebUtilities;
using Bakabase.TestKit.Implementations;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneBrowserAuthorizationSecurityTests
{
    private const string State = "expected-state-canary";
    private const string Code = "private-code-canary";
    private const string Callback = "http://localhost:53682/?code=" + Code + "&state=" + State;
    private const string Token = """{"access_token":"access-token-canary","refresh_token":"refresh-token-canary"}""";
    private string _root = null!;

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "rclone-browser-security-" + Guid.NewGuid().ToString("N"));
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [DataTestMethod]
    [DataRow("http://localhost:34567", "same-origin", true)]
    [DataRow("http://localhost:34567/", "", true)]
    [DataRow(null, null, true)]
    [DataRow(null, "cross-site", false)]
    [DataRow("http://localhost:34567", "cross-site", false)]
    [DataRow("null", "", false)]
    [DataRow("https://www.north-plus.net", "", false)]
    [DataRow("https://exhentai.org", "same-origin", false)]
    [DataRow("https://attacker.invalid", "", false)]
    [DataRow("http://localhost:34568", "same-site", false)]
    [DataRow("https://localhost:34567", "same-origin", true)]
    [DataRow("https://localhost:34567", "same-site", false)]
    [DataRow("https://localhost:34567", "", false)]
    [DataRow("https://localhost:34567", "cross-site", false)]
    [DataRow("https://localhost:34568", "same-origin", false)]
    [DataRow("https://attacker.invalid:34567", "same-origin", false)]
    [DataRow("http://localhost:34567/../", "same-origin", false)]
    [DataRow("http://localhost:34567/?code=private-code-canary", "same-origin", false)]
    public void BrowserCredentialFlowUsesItsOwnOriginBoundary(string? origin, string? fetchSite, bool allowed)
    {
        var http = Context(origin, fetchSite, RuntimeMode.Docker);
        Assert.AreEqual(allowed, RcloneBrowserOriginGuard.IsAllowed(http));
        var action = AuthorizationContext(http);
        new RcloneBrowserOriginAttribute().OnAuthorization(action);
        Assert.AreEqual(allowed, action.Result == null);
        Assert.AreEqual("no-store", http.Response.Headers.CacheControl.ToString());
        if (!allowed)
        {
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)action.Result!).StatusCode);
            var error = Assert.ThrowsExactly<RcloneRemoteException>(() => RcloneBrowserOriginGuard.Ensure(http));
            Assert.AreEqual("RCLONE_BROWSER_ORIGIN_REJECTED", error.Code);
            Assert.IsFalse(error.ToString().Contains(Code, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void OnlyTheExplicitDevelopmentUiIsTrustedInADevelopmentBuild()
    {
        Assert.IsTrue(ServiceCorsOrigins.DevServerOrigins.Count > 0);
        var origin = ServiceCorsOrigins.DevServerOrigins[0];
        Assert.IsTrue(RcloneBrowserOriginGuard.IsAllowed(Context(origin, "same-site", RuntimeMode.Dev)));
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(Context(origin, "same-site", RuntimeMode.Docker)));
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(Context(origin, "cross-site", RuntimeMode.Dev)));
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(Context("http://unlisted.invalid:3000", "same-site", RuntimeMode.Dev)));
    }

    [TestMethod]
    public void PairedRelayUsesTheServerOriginButPairingDoesNotTrustAnArbitraryWebPage()
    {
        // UpstreamTransformer rewrites admitted relay-page Origin to the destination
        // server's own origin before signing; no list of arbitrary frontend hosts is needed.
        var http = Context("http://localhost:34567", "same-origin", RuntimeMode.Docker);
        http.SetRemoteAccessContext(new() {IsLoopback = false, Mode = RemoteAccessMode.Enabled,
            Device = new RemoteDevice {Id = "paired-owner"}});
        Assert.IsTrue(RcloneBrowserOriginGuard.IsAllowed(http));
        http.Request.Headers.Origin = "https://www.north-plus.net";
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(http));
        http.Request.Headers.Remove("Origin");
        Assert.IsTrue(RcloneBrowserOriginGuard.IsAllowed(http));
    }

    [TestMethod]
    public void OwnOriginDoesNotGrantUnpairedRemoteAdministration()
    {
        var http = Context("http://localhost:34567", "same-origin", RuntimeMode.Docker);
        http.SetRemoteAccessContext(new() {IsLoopback = false, Mode = RemoteAccessMode.Enabled});
        var action = AuthorizationContext(http);
        new RcloneBrowserOriginAttribute().OnAuthorization(action);
        Assert.IsNull(action.Result);
        new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless)).OnAuthorization(action);
        Assert.IsNotNull(action.Result, "The independent owner gate must still reject an unpaired remote caller.");
    }

    [TestMethod]
    public void DuplicateOriginOrFetchMetadataCannotBorrowTheOwnOrigin()
    {
        var http = Context("http://localhost:34567", "same-origin", RuntimeMode.Docker);
        http.Request.Headers.Append("Origin", "https://attacker.invalid");
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(http));
        http = Context("http://localhost:34567", "same-origin", RuntimeMode.Docker);
        http.Request.Headers.Append("Sec-Fetch-Site", "cross-site");
        Assert.IsFalse(RcloneBrowserOriginGuard.IsAllowed(http));
    }

    [DataTestMethod]
    [DataRow(nameof(RcloneAccountsController.Start))]
    [DataRow(nameof(RcloneAccountsController.Get))]
    [DataRow(nameof(RcloneAccountsController.Answer))]
    [DataRow(nameof(RcloneAccountsController.Cancel))]
    [DataRow(nameof(RcloneAccountsController.StartBrowser))]
    [DataRow(nameof(RcloneAccountsController.SubmitBrowserCallback))]
    [DataRow(nameof(RcloneAccountsController.StopBrowser))]
    public void EveryAuthorizationLifecycleActionRequiresTheBrowserOriginGate(string action)
    {
        var method = typeof(RcloneAccountsController).GetMethod(action)!;
        Assert.IsNotNull(method.GetCustomAttribute<RcloneBrowserOriginAttribute>(),
            "Authorization session responses may contain the native authorization state and must use the same UI-only gate.");
        Assert.IsNotNull(typeof(RcloneAccountsController).GetCustomAttribute<RemoteAccessibleAttribute>(),
            "The browser-origin gate must supplement the existing owner authorization.");
    }

    [TestMethod]
    public void ExistingExceptionRequestMetadataDoesNotReadACallbackPostedInTheBody()
    {
        var http = Context("http://localhost:34567", "same-origin", RuntimeMode.Docker);
        http.Request.Path = "/rclone/accounts/authorization/session/browser/callback";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"callbackUrl\":\"" + Callback + "\"}"));
        var logged = http.BuildRequestInformation();
        Assert.IsFalse(logged.Contains(Code, StringComparison.Ordinal));
        Assert.IsFalse(logged.Contains(State, StringComparison.Ordinal));
        Assert.AreEqual(0L, http.Request.Body.Position);
    }

    private static DefaultHttpContext Context(string? origin, string? fetchSite, RuntimeMode runtime)
    {
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection().AddSingleton(new ServiceCorsOrigins(runtime)).BuildServiceProvider();
        http.Request.Method = "POST"; http.Request.Scheme = "http"; http.Request.Host = new("localhost", 34567);
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (origin != null) http.Request.Headers.Origin = origin;
        if (fetchSite != null) http.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        return http;
    }

    private static AuthorizationFilterContext AuthorizationContext(HttpContext http) => new(new ActionContext(http,
        new RouteData(), new ControllerActionDescriptor {ControllerTypeInfo = typeof(RcloneAccountsController).GetTypeInfo(),
            MethodInfo = typeof(RcloneAccountsController).GetMethod(nameof(RcloneAccountsController.Start))!}), []);

    [TestMethod]
    public void NativeCallbackAcceptsOnlyTheMatchingStateAndPreservesTheOpaqueCode()
    {
        var values = RcloneBrowserAuthorizationProcess.ValidateCallbackUrl(Callback, State);
        Assert.AreEqual(Code, values["code"]);
        Assert.AreEqual(State, values["state"]);
        var encoded = RcloneBrowserAuthorizationProcess.ValidateCallbackUrl(
            "http://localhost:53682/?code=opaque%2Bcode%2Fvalue%3D&state=" + State, State);
        Assert.AreEqual("opaque+code/value=", encoded["code"]);
    }

    [TestMethod]
    public void MicrosoftErrorCallbackIsSeparateFromAnAuthorizationCode()
    {
        var values = RcloneBrowserAuthorizationProcess.ValidateCallbackUrl(
            "http://localhost:53682/?error=access_denied&error_description=User%20cancelled&state=" + State, State);
        Assert.AreEqual("access_denied", values["error"]);
        Assert.AreEqual("User cancelled", values["error_description"]);
        Assert.IsFalse(values.ContainsKey("code"));
    }

    [DataTestMethod]
    [DataRow("https://localhost:53682/?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://127.0.0.1:53682/?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost.evil.invalid:53682/?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://user@localhost:53682/?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost:53683/?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost:53682/other?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost:53682/../?code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost:53682/?code=private-code-canary&state=expected-state-canary#fragment")]
    [DataRow("http://localhost:53682/?code=private-code-canary&state=expected-state-canary&url=https%3A%2F%2Fevil.invalid")]
    [DataRow("http://localhost:53682/?code=private-code-canary&code=private-code-canary&state=expected-state-canary")]
    [DataRow("http://localhost:53682/?code=private-code-canary&c%6fde=other&state=expected-state-canary")]
    [DataRow("http://localhost:53682/?code=private-code-canary&state=expected-state-canary&state=other")]
    [DataRow("http://localhost:53682/?code=private-code-canary&state=other")]
    [DataRow("http://localhost:53682/?code=private-code-canary")]
    [DataRow("http://localhost:53682/?state=expected-state-canary")]
    [DataRow("http://localhost:53682/?code=&state=expected-state-canary")]
    [DataRow("http://localhost:53682/?code=private-code-canary&error=access_denied&state=expected-state-canary")]
    public void InvalidCallbackCannotChangeTheDestinationOrLeakTheAuthorizationCode(string callback)
    {
        var error = Assert.ThrowsExactly<RcloneRemoteException>(() =>
            RcloneBrowserAuthorizationProcess.ValidateCallbackUrl(callback, State));
        Assert.IsFalse(error.ToString().Contains(Code, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(callback, StringComparison.Ordinal));
    }

    [TestMethod]
    public void NativeProcessArgumentsContainNoUserCallbackCodeOrToken()
    {
        var directory = Path.Combine(_root, "argv");
        var info = RcloneBrowserAuthorizationProcess.StartInfo("rclone-fixture", directory);
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsTrue(info.RedirectStandardOutput);
        Assert.IsTrue(info.RedirectStandardError);
        Assert.IsTrue(info.ArgumentList.Contains("authorize"));
        Assert.IsTrue(info.ArgumentList.Contains("onedrive"));
        Assert.IsTrue(info.ArgumentList.Contains("--auth-no-open-browser"));
        var arguments = string.Join(' ', info.ArgumentList);
        Assert.IsFalse(arguments.Contains(Code, StringComparison.Ordinal));
        Assert.IsFalse(arguments.Contains(Callback, StringComparison.Ordinal));
        Assert.IsFalse(arguments.Contains("access_token", StringComparison.Ordinal));
        Assert.IsFalse(info.Environment.Values.Any(v => v?.Contains(Code, StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public async Task WrongStateOrWrongSessionDoesNotPostOrTerminateTheNativeFlow()
    {
        var directory = Path.Combine(_root, "native");
        var executable = await FakeExecutable();
        var handler = new NativeListener(directory);
        await using var browser = new RcloneBrowserAuthorizationProcess(_ => Task.FromResult(executable), Proxy(), handler);
        var started = await browser.StartAsync("session", "question", directory, DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.AreEqual("awaitingCallback", started.Status);
        var pid = await ChildPid(directory);
        var busy = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => browser.StartAsync("another-session", "another-question",
            Path.Combine(_root, "other"), DateTimeOffset.UtcNow.AddMinutes(1), default));
        Assert.AreEqual("RCLONE_BROWSER_AUTHORIZATION_BUSY", busy.Code);
        Assert.AreEqual(started.AuthorizationUrl, (await browser.StartAsync("session", "question", directory,
            DateTimeOffset.UtcNow.AddMinutes(1), default)).AuthorizationUrl);
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => browser.SubmitAsync("other-session", "question", Callback, default));
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => browser.SubmitAsync("session", "other-question", Callback, default));
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => browser.SubmitAsync("session", "question", Callback.Replace(State, "other-state"), default));
        Assert.AreEqual(0, handler.Posts);
        Assert.AreEqual("awaitingCallback", (await browser.GetAsync("session", "question", default))!.Status);
        using (var process = Process.GetProcessById(pid)) Assert.IsFalse(process.HasExited);
        Assert.IsNull(await browser.StopAsync("unrelated-session", default));
        var result = await browser.SubmitAsync("session", "question", Callback, default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(Token, result);
        Assert.AreEqual(1, handler.Posts);
        Assert.AreEqual("http://127.0.0.1:53682/", handler.LastPostUri!.AbsoluteUri);
        Assert.AreEqual(Code, QueryHelpers.ParseQuery(handler.LastBody!)["code"].ToString());
        AssertChildExited(pid);
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => browser.SubmitAsync("session", "question", Callback, default));
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    public async Task StopDuringExchangeWaitsForChildExitAndPreservesCancelledState()
    {
        var directory = Path.Combine(_root, "native");
        var executable = await FakeExecutable();
        var handler = new NativeListener(directory) {BlockPost = true};
        await using var browser = new RcloneBrowserAuthorizationProcess(_ => Task.FromResult(executable), Proxy(), handler);
        await browser.StartAsync("session", "question", directory, DateTimeOffset.UtcNow.AddMinutes(1), default);
        var pid = await ChildPid(directory);
        var submitted = browser.SubmitAsync("session", "question", Callback, default);
        await handler.PostStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await browser.StopAsync("session", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("cancelled", stopped!.Status);
        try {await submitted.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Fail("A stopped exchange must not return credentials.");}
        catch (RcloneRemoteException error) { Assert.IsFalse(error.ToString().Contains(Code, StringComparison.Ordinal)); }
        AssertChildExited(pid);
        Assert.IsNull(await browser.GetAsync("session", "question", default));
    }

    [TestMethod]
    public async Task ExpiryWhileResolvingTheExecutableDoesNotStartAChild()
    {
        var executable = await FakeExecutable();
        var directory = Path.Combine(_root, "native");
        await using var browser = new RcloneBrowserAuthorizationProcess(async ct => {await Task.Delay(100, ct); return executable;},
            Proxy(), new NativeListener(directory));
        try
        {
            var result = await browser.StartAsync("session", "question", directory, DateTimeOffset.UtcNow.AddMilliseconds(20), default);
            Assert.AreEqual("expired", result.Status);
        }
        catch (RcloneRemoteException error) {Assert.AreEqual("RCLONE_BROWSER_AUTHORIZATION_EXPIRED", error.Code);}
        Assert.IsFalse(File.Exists(Path.Combine(directory, "pid")));
    }

    private static BakabaseWebProxy Proxy() => new(new TestBOptionsManager<NetworkOptions>(new()));
    private async Task<string> FakeExecutable()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix child-process fixture; parser and origin cases are portable.");
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(a => a.Port == 53682))
            Assert.Inconclusive("An unrelated process owns the native OAuth port; do not interfere with it.");
        Directory.CreateDirectory(_root); File.SetUnixFileMode(_root, (UnixFileMode)0x1c0);
        var executable = Path.Combine(_root, "fake-rclone");
        await File.WriteAllTextAsync(executable, """
            #!/bin/sh
            printf '%s' "$$" > pid
            printf '%s\n' "$@" > arguments.log
            printf 'NOTICE: http://127.0.0.1:53682/auth?state=expected-state-canary\n' >&2
            while [ ! -f complete ]; do sleep 0.05; done
            printf 'Paste the following into your remote machine --->\n'
            printf '{"access_token":"access-token-canary","refresh_token":"refresh-token-canary"}\n'
            printf '<---End paste\n'
            """);
        File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
        return executable;
    }
    private static async Task<int> ChildPid(string directory)
    {
        for (var i = 0; i < 100; i++)
        {
            var path = Path.Combine(directory, "pid");
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out var pid)) return pid;
            await Task.Delay(20);
        }
        throw new AssertFailedException("The fake native process did not publish its PID.");
    }
    private static void AssertChildExited(int pid)
    {
        try {using var process = Process.GetProcessById(pid); Assert.IsTrue(process.HasExited);}
        catch (ArgumentException) { }
    }
    private sealed class NativeListener(string directory) : HttpMessageHandler
    {
        public bool BlockPost { get; init; }
        public int Posts { get; private set; }
        public Uri? LastPostUri { get; private set; }
        public string? LastBody { get; private set; }
        public TaskCompletionSource PostStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual("http", request.RequestUri!.Scheme);
            Assert.AreEqual("127.0.0.1", request.RequestUri.Host);
            Assert.AreEqual(53682, request.RequestUri.Port);
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.TemporaryRedirect) {Headers = {Location = new(QueryHelpers.AddQueryString(
                    "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", new Dictionary<string, string?>
                    {{"client_id", "b15665d9-eda6-4092-8539-0eec376afd59"}, {"redirect_uri", "http://localhost:53682/"},
                        {"response_type", "code"}, {"scope", RcloneAuthorizationService.ReadScopes}, {"state", State}}))}};
            Posts++; LastPostUri = request.RequestUri;
            LastBody = await request.Content!.ReadAsStringAsync(ct); PostStarted.TrySetResult();
            if (BlockPost) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "complete"), "complete", ct);
            return new(HttpStatusCode.OK);
        }
    }
}
