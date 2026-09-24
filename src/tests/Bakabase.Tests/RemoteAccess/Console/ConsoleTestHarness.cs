using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// The desktop app's console, composed the way the app composes it — a container with this
/// device's server identity and listening addresses, and <c>AddRemoteConsole</c> — minus the
/// server itself, which none of this touches.
/// </summary>
internal sealed class ConsoleHarness : IAsyncDisposable
{
    public const string OwnServerId = "this-device";

    /// <summary>Where the harness's relays start looking for a port, clear of the real app's 34650.</summary>
    public const int FirstRelayPort = 47300;

    private ServiceProvider _provider = null!;

    public string Root { get; private init; } = null!;
    public int ServicePort { get; private init; }
    public RemoteConsoleManager Manager { get; private set; } = null!;
    public ManagedServerStore Store { get; private set; } = null!;
    public RemoteConsoleLocalOrigin LocalOrigin { get; private set; } = null!;
    public List<string> Logs { get; } = [];

    /// <summary>
    /// What looking around the network finds. Stands in for the beacons so no test sends a
    /// broadcast, and records every search so a test can tell nothing searched on its own.
    /// </summary>
    public FakeDiscovery Discovery { get; } = new();

    public string ManagedDirectory => Path.Combine(Root, "managed");
    public string ManagedFile => Path.Combine(ManagedDirectory, "connection.json");

    /// <param name="root">Reuse a previous harness's directory, i.e. restart the app.</param>
    /// <param name="legacyFile">Where the removed thin client's connection file is, if anywhere.</param>
    /// <param name="services">Anything else the app's container should hold, e.g. its GUI adapter.</param>
    /// <param name="options">Changes to the console's composition-time settings, after the harness's own.</param>
    public static async Task<ConsoleHarness> StartAsync(string? root = null, string? legacyFile = null,
        bool importOnStart = false, int? servicePort = null, Action<IServiceCollection>? services = null,
        Action<RemoteConsoleOptions>? options = null)
    {
        var harness = new ConsoleHarness
        {
            Root = root ?? Path.Combine(Path.GetTempPath(), "bakabase-console-tests", Guid.NewGuid().ToString("N")),
            ServicePort = servicePort ?? LoopbackPortAllocator.Allocate(46800)
        };

        var address = $"http://0.0.0.0:{harness.ServicePort}";
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.AddProvider(new CaptureProvider(harness.Logs)).SetMinimumLevel(LogLevel.Debug));
        collection.AddSingleton<IRemoteAccessService>(new FakeRemoteAccess());
        collection.AddSingleton<IServerDiscovery>(harness.Discovery);
        collection.AddSingleton(new AppContext
        {
            ListeningAddresses = [address],
            ApiEndpoints = [$"http://localhost:{harness.ServicePort}"],
            ApiEndpoint = $"http://localhost:{harness.ServicePort}"
        });
        services?.Invoke(collection);
        collection.AddRemoteConsole(o =>
        {
            o.ManagedDirectory = harness.ManagedDirectory;
            o.FirstRelayPort = FirstRelayPort;
            o.LegacyClientConnectionFile = () => legacyFile;
            o.ImportLegacyClientOnStart = importOnStart;
            o.ClaimPollInterval = TimeSpan.FromMilliseconds(50);
            options?.Invoke(o);
        });

        harness._provider = collection.BuildServiceProvider();
        harness.Manager = harness._provider.GetRequiredService<RemoteConsoleManager>();
        harness.Store = harness._provider.GetRequiredService<ManagedServerStore>();
        harness.LocalOrigin = harness._provider.GetRequiredService<RemoteConsoleLocalOrigin>();

        foreach (var hosted in harness._provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        await harness.Manager.Startup;

        return harness;
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Adds a server as if it had been paired here, with a fresh key.</summary>
    public async Task<(string DeviceId, string Key)> AddManagedAsync(FakeServer server, string? deviceId = null)
    {
        deviceId ??= $"device-for-{server.ServerId}";
        var key = RemoteRequestSignature.ToBase64Url(RandomNumberGenerator.GetBytes(32));

        server.KnownDevices[deviceId] = key;

        await Store.MutateAsync(data => data.Servers.Add(new ClientServerConnection
        {
            ServerId = server.ServerId,
            ServerName = server.Name,
            BaseAddress = server.BaseAddress,
            DeviceId = deviceId,
            DeviceKey = key,
            PairedAt = DateTime.UtcNow
        }));

        return (deviceId, key);
    }

    /// <summary>Stops the app, leaving its data where it was.</summary>
    /// <remarks>
    /// Idempotent, as a host's own stop is: a test that stops the app to restart it inside an
    /// <c>await using</c> gets a second stop from the disposal, and that one must be a no-op
    /// rather than a resolution against a container that is already gone.
    /// </remarks>
    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        foreach (var hosted in _provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await _provider.DisposeAsync();
    }

    private int _stopped;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        DeleteRoot(Root);
    }

    public static void DeleteRoot(string root)
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Sends a request the way this device's window would: to the relay, naming it as Host.</summary>
    public static async Task<HttpResponseMessage> SendToRelayAsync(int port, string pathAndQuery,
        HttpMethod? method = null, string? body = null)
    {
        using var handler = new SocketsHttpHandler {AllowAutoRedirect = false, UseProxy = false, UseCookies = false};
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"http://127.0.0.1:{port}{pathAndQuery}");

        request.Headers.Host = $"127.0.0.1:{port}";

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();

        return response;
    }

    /// <summary>
    /// A top-level navigation as a browser sends it: <c>GET</c>, document, with fetch
    /// metadata and whatever cookies the browser holds for <c>127.0.0.1</c> — which are
    /// every loopback listener's, since cookies ignore the port.
    /// </summary>
    /// <param name="site">
    /// <c>cross-site</c> for a window arriving from this device's own origin
    /// (<c>localhost</c>) or another relay, <c>same-origin</c> for the relay's own page
    /// moving on.
    /// </param>
    public static async Task<HttpResponseMessage> NavigateAsync(string url, string site, string? cookie = null)
    {
        using var handler = new SocketsHttpHandler {AllowAutoRedirect = false, UseProxy = false, UseCookies = false};
        using var client = new HttpClient(handler);
        var uri = new Uri(url);

        // The fragment is the browser's own and never sent; HttpClient would not send it
        // either, but it has no place on the wire, so it is stripped here to say so.
        var request = new HttpRequestMessage(HttpMethod.Get, uri.GetLeftPart(UriPartial.Query));
        request.Headers.Host = uri.Authority;
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");

        if (cookie != null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();

        return response;
    }

    /// <summary>
    /// Where the page a switch ticket is answered with sends the window, fragment included —
    /// what the page's script does with <c>location.hash</c> in a browser.
    /// </summary>
    public static async Task<string> ContinuationOfAsync(HttpResponseMessage landing, string? fragment = null)
    {
        var body = await landing.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(body,
            @"location\.replace\((?<url>""[^""]*"")(?<hash>\s*\+\s*location\.hash)?\)");

        if (!match.Success)
        {
            throw new InvalidOperationException($"Not a continuation page: {body}");
        }

        // The page carries the fragment over only by appending location.hash to the
        // address it navigates to; a page that did not would land a hash route on the UI's
        // root. Required whether or not this call has a fragment to append.
        if (!match.Groups["hash"].Success)
        {
            throw new InvalidOperationException($"The continuation drops the fragment: {body}");
        }

        return JsonSerializer.Deserialize<string>(match.Groups["url"].Value)! + fragment;
    }

    public static int PortOf(string url) => new Uri(url).Port;

    private sealed class FakeRemoteAccess : IRemoteAccessService
    {
        public RemoteAccessMode GetEffectiveMode() => RemoteAccessMode.Enabled;
        public Task SetModeAsync(RemoteAccessMode? mode) => throw new NotSupportedException("never changed by the console");
        public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() => [];
        public Task<string> GetOrCreateServerIdAsync() => Task.FromResult(OwnServerId);
        public bool GetAllowLiveTranscode() => false;
        public Task SetAllowLiveTranscodeAsync(bool allow) => throw new NotSupportedException();
        public bool GetRequirePairing() => true;
        public Task SetRequirePairingAsync(bool require) => throw new NotSupportedException();

        public Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync() =>
            Task.FromResult(new RemoteAccessServerDescriptor(OwnServerId, Environment.MachineName, null, "1.0.0", 1));
    }

    private sealed class CaptureProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(sink, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capture(List<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                {
                    sink.Add($"{logLevel} {category}: {formatter(state, exception)}{(exception == null ? "" : " | " + exception)}");
                }
            }
        }
    }
}

/// <summary>The network's answer to "which servers are out there", as the test sets it.</summary>
internal sealed class FakeDiscovery : IServerDiscovery
{
    private int _searches;

    public IReadOnlyList<DiscoveredServer> Found { get; set; } = [];

    public int Searches => Volatile.Read(ref _searches);

    public TimeSpan? LastTimeout { get; private set; }

    public Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _searches);
        LastTimeout = timeout;

        return Task.FromResult(Found);
    }
}

/// <summary>
/// Stands in for another Bakabase server: answers the remote-access routes the console and
/// the relay use, checks every device signature against the keys it issued, and records
/// everything that reached it.
/// </summary>
internal sealed class FakeServer : IAsyncDisposable
{
    private WebApplication _app = null!;
    private int _issued;

    public string ServerId { get; private init; } = null!;
    public string Name { get; private init; } = null!;
    public int Port { get; private init; }
    public string BaseAddress => $"http://127.0.0.1:{Port}";

    public RemoteAccessMode Mode { get; set; } = RemoteAccessMode.Enabled;

    /// <summary>Devices this server accepts, by id, with their keys.</summary>
    public ConcurrentDictionary<string, string> KnownDevices { get; } = new();

    public string? PairingCode { get; set; }

    /// <summary>Whether somebody at this server approved the filed request.</summary>
    public bool Approved { get; set; }

    /// <summary>Whether somebody at this server rejected the filed request.</summary>
    public bool Rejected { get; set; }

    /// <summary>
    /// An HTTP status every claim is answered with instead: a proxy's 502 while the server is
    /// briefly away, or the server's own 429.
    /// </summary>
    public HttpStatusCode? ClaimStatus { get; set; }

    /// <summary>How long a filed request is valid for, by this server's clock.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>How long <c>/remote-access/server-info</c> takes to answer: a slow or wedged server.</summary>
    public TimeSpan ServerInfoDelay { get; set; }

    /// <summary>
    /// Whether a request signed by a device this server does not know is refused on every
    /// path, as a real server's gate refuses it, rather than only on
    /// <c>/remote-access/context</c>. Off by default so a test about where a request went
    /// can still see it answered.
    /// </summary>
    public bool RefusesUnknownDevices { get; set; }

    public sealed record Received(string Method, string Path, string Query, string? DeviceId, bool? SignatureValid,
        string? Cookie = null, string? Origin = null);

    public static async Task<FakeServer> StartAsync(string serverId, string name, int preferredPort)
    {
        var server = new FakeServer
        {
            ServerId = serverId,
            Name = name,
            Port = LoopbackPortAllocator.Allocate(preferredPort)
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions {Args = []});
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, server.Port));

        server._app = builder.Build();
        server._app.Urls.Clear();
        server._app.UseWebSockets();
        server._app.Run(server.HandleAsync);

        await server._app.StartAsync();

        return server;
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? string.Empty;
        var rawQuery = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty;

        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer);
        request.Body.Position = 0;
        var body = buffer.ToArray();

        var parsed = RemoteRequestSignature.TryParseHeader(request.Headers.Authorization.ToString());
        bool? valid = null;

        if (parsed != null)
        {
            var digest = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
                         request.ContentLength is > 0 and <= RemoteRequestSignature.MaxHashedBodyBytes
                ? RemoteRequestSignature.HashBody(body)
                : string.Empty;

            valid = KnownDevices.TryGetValue(parsed.DeviceId, out var key) &&
                    RemoteRequestSignature.Verify(RemoteRequestSignature.FromBase64Url(key),
                        RemoteRequestSignature.BuildCanonicalString(parsed.DeviceId, request.Method, path, rawQuery,
                            parsed.TimestampSeconds, parsed.Nonce, digest),
                        parsed.Signature);
        }

        Requests.Enqueue(new Received(request.Method, path, rawQuery, parsed?.DeviceId, valid,
            request.Headers.Cookie.Count > 0 ? request.Headers.Cookie.ToString() : null,
            request.Headers.Origin.Count > 0 ? request.Headers.Origin.ToString() : null));

        if (RefusesUnknownDevices && valid == false)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["X-Bakabase-Remote-Access"] = nameof(RemoteAccessDenialReason.DeviceRevoked);
            await WriteJsonAsync(context, "{\"code\":401}");
            return;
        }

        switch (request.Method, path)
        {
            case ("GET", "/remote-access/server-info"):
                if (ServerInfoDelay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(ServerInfoDelay, context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                await WriteJsonAsync(context,
                    "{\"code\":0,\"data\":{" +
                    $"\"id\":\"{ServerId}\",\"name\":\"{Name}\",\"appVersion\":\"9.9.9\",\"protocolVersion\":1," +
                    $"\"mode\":{(int) Mode},\"pairingSupported\":true," +
                    $"\"serverTime\":\"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}\"" +
                    "}}");
                return;

            case ("GET", "/remote-access/context"):
                if (parsed != null && valid != true)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers["X-Bakabase-Remote-Access"] = nameof(RemoteAccessDenialReason.DeviceRevoked);
                    await WriteJsonAsync(context, "{\"code\":401}");
                    return;
                }

                await WriteJsonAsync(context,
                    $"{{\"code\":0,\"data\":{{\"isLocal\":false,\"mode\":{(int) Mode},\"paired\":{(valid == true ? "true" : "false")}}}}}");
                return;

            case ("POST", "/remote-access/pair/code"):
            {
                var code = JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();

                if (PairingCode == null || code != PairingCode)
                {
                    await WriteJsonAsync(context, "{\"code\":0,\"data\":{\"failure\":1}}");
                    return;
                }

                await IssueAsync(context);
                return;
            }

            case ("POST", "/remote-access/pair/request"):
                await WriteJsonAsync(context,
                    "{\"code\":0,\"data\":{\"requestId\":\"request-1\",\"expiresAt\":\"" +
                    DateTime.UtcNow.Add(RequestLifetime).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    "\"}}");
                return;

            case ("POST", "/remote-access/pair/claim"):
                if (ClaimStatus is { } status)
                {
                    context.Response.StatusCode = (int) status;
                    await WriteJsonAsync(context, $"{{\"code\":{(int) status}}}");
                    return;
                }

                if (Rejected)
                {
                    await WriteJsonAsync(context, "{\"code\":0,\"data\":{\"failure\":2}}");
                    return;
                }

                if (!Approved)
                {
                    await WriteJsonAsync(context, "{\"code\":0,\"data\":{\"failure\":3}}");
                    return;
                }

                await IssueAsync(context);
                return;
        }

        if (path == "/hub/echo" && context.WebSockets.IsWebSocketRequest)
        {
            // Stands in for the UI hub: one message in, the same message back.
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var received = new byte[1024];
            var message = await socket.ReceiveAsync(received, context.RequestAborted);

            await socket.SendAsync(received.AsMemory(0, message.Count), System.Net.WebSockets.WebSocketMessageType.Text,
                true, context.RequestAborted);
            await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null,
                context.RequestAborted);
            return;
        }

        if (path == "/hang")
        {
            // A response that never ends: a video stream, a hub connection.
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.FlushAsync();

            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        if (HttpMethods.IsDelete(request.Method) && path.StartsWith("/remote-access/devices/", StringComparison.Ordinal))
        {
            if (valid == true)
            {
                KnownDevices.TryRemove(Uri.UnescapeDataString(path["/remote-access/devices/".Length..]), out _);
            }

            await WriteJsonAsync(context, "{\"code\":0}");
            return;
        }

        await WriteJsonAsync(context, $"{{\"code\":0,\"data\":\"{path}\"}}");
    }

    private async Task IssueAsync(HttpContext context)
    {
        var deviceId = $"{ServerId}-device-{Interlocked.Increment(ref _issued)}";
        var key = RemoteRequestSignature.ToBase64Url(RandomNumberGenerator.GetBytes(32));

        KnownDevices[deviceId] = key;

        await WriteJsonAsync(context,
            $"{{\"code\":0,\"data\":{{\"credentials\":{{\"deviceId\":\"{deviceId}\",\"key\":\"{key}\",\"serverId\":\"{ServerId}\"}}}}}}");
    }

    private static async Task WriteJsonAsync(HttpContext context, string json)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(json);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
