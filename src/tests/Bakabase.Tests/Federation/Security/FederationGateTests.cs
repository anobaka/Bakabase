using System.Net;
using System.Reflection;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation.Security;

[TestClass]
public sealed class FederationGateTests
{
    [TestMethod]
    public async Task NodeExportAlwaysRequiresGrantBeforeLegacyLoopbackAndUnrestrictedBypasses()
    {
        using var fixture = await GateFixture.CreateAsync();
        foreach (var ip in new[] { "127.0.0.1", "192.168.20.8" })
        foreach (var mode in new[] { RemoteAccessMode.Enabled, RemoteAccessMode.Unrestricted })
        {
            fixture.Remote.Mode = mode;
            var context = Context("/federation/v1/export/queries", "POST", ip);
            await fixture.RunAsync(context);
            Assert.AreEqual(401, context.Response.StatusCode, $"{ip} {mode}");
            Assert.AreEqual("NodeAuthenticationRequired", Error(context));
            Assert.IsFalse(fixture.ReachedEndpoint);
            Assert.IsFalse(FederationHttpContext.IsHandled(context));
        }
    }

    [TestMethod]
    public async Task ValidNodeSignatureStillHonorsDisabledButLocalManagementRemainsAvailable()
    {
        using var fixture = await GateFixture.CreateAsync();
        fixture.Remote.Mode = RemoteAccessMode.Disabled;
        var context = Context("/federation/v1/export/queries", "POST");
        fixture.Sign(context);
        await fixture.RunAsync(context);
        Assert.AreEqual(403, context.Response.StatusCode);
        Assert.AreEqual("RemoteAccessDisabled", Error(context));
        Assert.IsFalse(fixture.ReachedEndpoint);
        context = Context("/federation/v1/info", "GET");
        await fixture.RunAsync(context);
        Assert.AreEqual("RemoteAccessDisabled", Error(context));
        context = Context("/federation/local/peers", "GET");
        await fixture.RunAsync(context);
        Assert.IsTrue(fixture.ReachedEndpoint);
        Assert.AreEqual(FederationEndpointKind.Local, FederationHttpContext.GetKind(context));
    }

    [TestMethod]
    public async Task ExplicitOrMalformedNodeCredentialsNeverFallBackToLegacyAnonymousRoutes()
    {
        using var fixture = await GateFixture.CreateAsync();
        fixture.Remote.Mode = RemoteAccessMode.Unrestricted;
        foreach (var path in new[] { "/resource", "/hub", "/options", "/federation/local/peers", "/federation/v1/pair/request" })
        foreach (var header in new[] { "Bakabase-Node", " Bakabase-Node malformed", "bakabase-node-suffix payload", "Bearer value, Bakabase-Node malformed" })
        {
            var context = Context(path, "POST");
            context.Request.Headers.Authorization = header;
            await fixture.RunAsync(context);
            Assert.AreEqual(403, context.Response.StatusCode, path);
            Assert.AreEqual("NodeRouteForbidden", Error(context));
            Assert.IsFalse(fixture.ReachedEndpoint);
        }
        var malformedExport = Context("/federation/v1/export/queries", "POST");
        malformedExport.Request.Headers.Authorization = "Bakabase-Node malformed";
        await fixture.RunAsync(malformedExport);
        Assert.AreEqual("InvalidNodeSignature", Error(malformedExport));
        var anonymousLegacy = Context("/resource", "GET");
        await fixture.RunAsync(anonymousLegacy);
        Assert.IsTrue(fixture.ReachedEndpoint);
        Assert.IsFalse(FederationHttpContext.IsHandled(anonymousLegacy));
    }

    [TestMethod]
    public async Task LocalGateRejectsLanForwardedLoopbackForeignHostAndForeignOrigin()
    {
        using var fixture = await GateFixture.CreateAsync();
        foreach (var (ip, host, origin) in new[]
                 {
                     ("192.168.20.8", "localhost:9000", ""), ("127.0.0.1", "attacker.example:9000", ""),
                     ("127.0.0.1", "localhost:9001", ""), ("127.0.0.1", "localhost:9000", "https://attacker.example"),
                     ("127.0.0.1", "localhost:9000", "http://localhost:9001"), ("127.0.0.1", "localhost:9000", "null")
                 })
        {
            var context = Context("/federation/local/peers", "GET", ip);
            context.Request.Host = new(host);
            context.Request.Headers.Origin = origin;
            context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
            await fixture.RunAsync(context);
            Assert.AreEqual("LocalInterfaceOnly", Error(context), $"{ip}/{host}/{origin}");
            Assert.IsFalse(fixture.ReachedEndpoint);
        }
        var allowed = Context("/federation/local/peers", "GET", "::ffff:127.0.0.1");
        allowed.Request.Headers.Origin = "http://localhost:9000";
        await fixture.RunAsync(allowed);
        Assert.IsTrue(fixture.ReachedEndpoint);
    }

    [TestMethod]
    public async Task ValidSignatureCrossesLegacyGateWithoutCreatingFullControlPairedContext()
    {
        using var fixture = await GateFixture.CreateAsync();
        var context = Context("/federation/v1/export/queries", "POST", "192.168.20.8");
        context.Request.QueryString = new QueryString("??name=one&name=two");
        fixture.Sign(context);
        await fixture.RunAsync(context);
        Assert.IsTrue(fixture.ReachedEndpoint);
        Assert.AreEqual(fixture.Grant.GrantId, FederationHttpContext.GetNodePrincipal(context)!.GrantId);
        Assert.IsNull(context.GetRemoteAccessContext());
    }

    [TestMethod]
    [DataRow("/federation/v1/export/resources/resolve", "POST", false)]
    [DataRow("/federation/v1/export/resources/resolve", "POST", true)]
    [DataRow("/federation/v1/export/resources/location", "POST", false)]
    [DataRow("/federation/v1/export/resources/location", "POST", true)]
    [DataRow("/federation/v1/export/mapping-roots", "GET", false)]
    [DataRow("/federation/v1/export/mapping-roots", "GET", true)]
    public async Task RevocationDuringExportCancelsRequestAndRejectsLateSuccessfulControlResults(
        string path, string method, bool disableSharing)
    {
        using var fixture = await GateFixture.CreateAsync();
        using var connection = new CancellationTokenSource();
        var context = Context(path, method);
        context.RequestAborted = connection.Token;
        fixture.Sign(context);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.RunAsync(context, async http =>
        {
            // Model a storage read which began while allowed, then returns without
            // observing cancellation (the existing resource Get API has no token).
            if (disableSharing) await fixture.Peers.SetSharingAsync(false);
            else await fixture.Peers.RevokeAsync(fixture.Grant.GrantId);
            var controller = new LateResultController
                { ControllerContext = new ControllerContext { HttpContext = http } };
            controller.Success();
        }));
        Assert.IsFalse(connection.IsCancellationRequested, "Grant cancellation must not cancel the original connection source.");
        Assert.AreEqual(connection.Token, context.RequestAborted, "The original request token must be restored after unwinding.");
        Assert.AreEqual(0L, context.Response.Body.Length);
    }

    [TestMethod]
    public async Task ChunkedControlBodiesAreBoundedBeforeModelBindingOrAuthentication()
    {
        using var fixture = await GateFixture.CreateAsync();
        foreach (var path in new[] { "/federation/local/peers/connect", "/federation/v1/pair/request", "/federation/v1/export/queries" })
        {
            var context = Context(path, "POST");
            context.Request.ContentLength = null;
            context.Request.Body = new NonSeekableBody(new byte[NodeRequestSignature.MaxControlBodyBytes + 1]);
            context.Request.Headers.TransferEncoding = "chunked";
            if (path.Contains("/export/")) context.Request.Headers.Authorization = "Bakabase-Node malformed";
            await fixture.RunAsync(context);
            Assert.AreEqual(413, context.Response.StatusCode);
            Assert.AreEqual("RequestTooLarge", Error(context));
            Assert.IsFalse(fixture.ReachedEndpoint);
        }
    }

    [TestMethod]
    public async Task MetadataAndEarlyGateMustBothAgreeBeforeLegacyMvcFiltersSkip()
    {
        foreach (var (action, handled, kind, principal, allowed) in new[]
                 {
                     (nameof(Actions.Export), false, FederationEndpointKind.Export, true, false),
                     (nameof(Actions.Export), true, FederationEndpointKind.Public, true, false),
                     (nameof(Actions.Export), true, FederationEndpointKind.Export, false, false),
                     (nameof(Actions.Unmarked), true, FederationEndpointKind.Export, true, false),
                     (nameof(Actions.Export), true, FederationEndpointKind.Export, true, true)
                 })
        {
            var http = Context("/federation/v1/export/queries", "POST");
            if (handled) FederationHttpContext.MarkHandled(http, kind,
                principal ? new NodePrincipal("grant", "reader", "owner", "epoch", 1) : null);
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = typeof(Actions).GetMethod(action)!, ControllerTypeInfo = typeof(Actions).GetTypeInfo()
            };
            var filter = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
            await new FederationLocalAccessFilter().OnAuthorizationAsync(filter);
            Assert.AreEqual(allowed, filter.Result == null, action + "/" + handled + "/" + kind);
        }
    }

    [TestMethod]
    public void EveryRealFederationActionHasAnExactAllowedProtocolRoute()
    {
        var controllers = new[] { typeof(FederationPeerController), typeof(FederationLocalController),
            typeof(FederationExportController), typeof(FederationMediaController) };
        foreach (var type in controllers)
        foreach (var action in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        foreach (var route in action.GetCustomAttributes<HttpMethodAttribute>())
        {
            var prefix = type.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            var path = route.Template?.StartsWith("~/") == true ? route.Template[1..] :
                "/" + prefix.Trim('/') + (string.IsNullOrEmpty(route.Template) ? "" : "/" + route.Template);
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]+\}", "test-id");
            var kind = (action.GetCustomAttribute<FederationEndpointAttribute>() ?? type.GetCustomAttribute<FederationEndpointAttribute>())?.Kind;
            Assert.IsNotNull(kind, type.Name + "." + action.Name);
            Assert.AreEqual(kind, FederationRoutePolicy.Classify(path), path);
            foreach (var method in route.HttpMethods)
                Assert.IsTrue(FederationRoutePolicy.Allows(kind.Value, method, path), method + " " + path);
        }
    }

    private static DefaultHttpContext Context(string path, string method, string ip = "127.0.0.1")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Connection.LocalPort = 9000;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 9000);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        return context;
    }
    private static string Error(HttpContext context) => JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray())
        .RootElement.GetProperty("code").GetString()!;
    private sealed class Actions
    {
        [FederationEndpoint(FederationEndpointKind.Export)] public void Export() { }
        public void Unmarked() { }
    }
    private sealed class LateResultController : FederationControllerBase
    {
        public ContentResult Success() => FederationResult(new { resource = "private result" });
    }
    private sealed class GateFixture : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "federation-gate-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("owner-node");
        public RemoteService Remote { get; } = new();
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; private set; } = null!;
        public NodeGrantAuthenticator Auth { get; private set; } = null!;
        public NodeCredentials Grant { get; private set; } = null!;
        public FederationPeerService Peers { get; private set; } = null!;
        public bool ReachedEndpoint { get; private set; }
        public static async Task<GateFixture> CreateAsync()
        {
            var f = new GateFixture();
            f.Store = new(f, f);
            var identity = new NodeIdentityProvider(f.Store);
            var peers = new FederationPeerService(f.Store, identity, f.Leases, TimeProvider.System);
            f.Peers = peers;
            var grants = new NodeGrantService(f.Store, identity, f.Leases, TimeProvider.System);
            f.Auth = new(grants, new NodeNonceCache(TimeProvider.System), TimeProvider.System);
            await peers.SetSharingAsync(true);
            var invitation = await peers.IssueInvitationAsync();
            f.Grant = (await peers.ExchangeCodeAsync(new("reader-node", "Reader", invitation.Code,
                "test-transaction", NodeRequestSignature.RandomToken()))).Credentials!;
            return f;
        }
        public void Sign(HttpContext context) => context.Request.Headers.Authorization = NodeRequestSignature.Create(
            Grant, context.Request.Method, context.Request.Path, context.Request.QueryString.HasValue ?
                context.Request.QueryString.Value![1..] : "", NodeRequestSignature.Hash([]), DateTimeOffset.UtcNow);
        public async Task RunAsync(HttpContext context, RequestDelegate? endpoint = null)
        {
            ReachedEndpoint = false;
            var legacy = new RemoteAccessMiddleware(ctx => { ReachedEndpoint = true; return endpoint?.Invoke(ctx) ?? Task.CompletedTask; },
                NullLogger<RemoteAccessMiddleware>.Instance);
            // A valid node request skips legacy authentication before its null authenticators
            // are touched; anonymous loopback still follows the existing loopback branch.
            var middleware = new FederationAccessMiddleware(ctx => legacy.InvokeAsync(ctx, Remote, null!, null!));
            await middleware.InvokeAsync(context, Store, Auth, Remote, Leases);
        }
        public void Dispose() { Leases.Dispose(); Directory.Delete(Path, true); }
    }
    private sealed class RemoteService : IRemoteAccessService
    {
        public RemoteAccessMode Mode { get; set; } = RemoteAccessMode.Enabled;
        public RemoteAccessMode GetEffectiveMode() => Mode;
        public Task SetModeAsync(RemoteAccessMode? mode) { Mode = mode ?? RemoteAccessMode.Enabled; return Task.CompletedTask; }
        public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() => [];
        public Task<string> GetOrCreateServerIdAsync() => Task.FromResult("owner-node");
        public bool GetAllowLiveTranscode() => false;
        public Task SetAllowLiveTranscodeAsync(bool allow) => Task.CompletedTask;
        public bool GetRequirePairing() => true;
        public Task SetRequirePairingAsync(bool require) => Task.CompletedTask;
        public Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync() => throw new NotSupportedException();
    }
    private sealed class NonSeekableBody(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
