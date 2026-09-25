using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.TestKit.Implementations;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// The Service's own request gates on a real loopback listener, in front of a chosen set of
/// controllers.
/// </summary>
/// <remarks>
/// <para>
/// The gates run through <see cref="BakabaseStartup.UseRequestGates"/> — the method the
/// Service itself calls — and CORS through the same <c>UseBootstrapCors</c> composition
/// <c>AppStartup</c> uses, so a test here exercises the production order rather than a copy
/// of it. What is left out is what the gates do not depend on: the database, the real hub's
/// services, and every controller a test did not ask for.
/// </para>
/// <para>
/// Kestrel on a real port rather than a fake context, because what is under test is what a
/// browser's request looks like when it arrives: its <c>Host</c>, its loopback peer
/// address, its WebSocket handshake. A caller on another machine is stood in for by
/// <see cref="RemoteIpHeader"/>, which only this harness reads.
/// </para>
/// </remarks>
internal sealed class ServiceGateHost : IAsyncDisposable
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private readonly IHost _host;

    private ServiceGateHost(IHost host, int port, string root)
    {
        _host = host;
        Port = port;
        Root = root;
    }

    public int Port { get; }

    /// <summary>Where this device's own UI would be served from.</summary>
    public string Origin => $"http://127.0.0.1:{Port}";

    /// <summary>A relay the desktop app runs for another server, one port away.</summary>
    public const string RelayOrigin = "http://127.0.0.1:34650";

    /// <summary><c>yarn dev</c>'s server.</summary>
    public const string DevOrigin = "http://localhost:3000";

    /// <summary>
    /// The SignalR hubs the Service maps, each of which accepts WebSocket, server-sent events
    /// and long polling: <c>WebGuiHub</c> and <c>SimpleProgressorHub</c>. Nothing else in the
    /// Service accepts a WebSocket.
    /// </summary>
    public static readonly string[] Hubs = ["/hub/ui", "/hub/progressor"];

    /// <summary>
    /// Opens a WebSocket to <paramref name="pathAndQuery"/> the way Chromium — and so
    /// WebView2 — does: <c>Origin</c> when given, and no fetch metadata. Failing to open is
    /// not an error here; the socket's state and the answer's status say what happened.
    /// </summary>
    public async Task<ClientWebSocket> OpenSocketAsync(string pathAndQuery, string? origin, string? host = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.Proxy = null;
        if (origin != null)
        {
            socket.Options.SetRequestHeader("Origin", origin);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            socket.Options.SetRequestHeader(name, value);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await socket.ConnectAsync(new Uri($"ws://{host ?? $"127.0.0.1:{Port}"}{pathAndQuery}"), timeout.Token);
        }
        catch (WebSocketException)
        {
        }

        return socket;
    }

    /// <summary>
    /// SignalR's JSON handshake and one invocation of <see cref="StubHub.Ping"/> over an open
    /// socket: that the socket carries the hub itself, not merely an upgrade.
    /// </summary>
    public static async Task<string> PingOverSocketAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await SendFramesAsync(socket, """{"protocol":"json","version":1}""", timeout.Token);
        var handshake = await ReceiveRecordAsync(socket, timeout.Token);
        if (handshake != "{}")
        {
            throw new InvalidOperationException($"Unexpected handshake answer: {handshake}");
        }

        await SendFramesAsync(socket, """{"type":1,"invocationId":"1","target":"Ping","arguments":[]}""", timeout.Token);
        while (true)
        {
            var record = await ReceiveRecordAsync(socket, timeout.Token);
            if (record.Contains("\"invocationId\":\"1\"", StringComparison.Ordinal))
            {
                return record;
            }
        }
    }

    private static Task SendFramesAsync(ClientWebSocket socket, string record, CancellationToken ct) =>
        socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(record + '\u001e'), WebSocketMessageType.Text, true, ct);

    private static async Task<string> ReceiveRecordAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            text.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage && text.Length > 0 && text[^1] == '\u001e')
            {
                return text.ToString(0, text.Length - 1);
            }
        }
    }

    public string Root { get; }

    public IServiceProvider Services => _host.Services;

    public FakeRemoteAccessService Remote => Services.GetRequiredService<FakeRemoteAccessService>();

    public RecordingNotificationService Notifications => Services.GetRequiredService<RecordingNotificationService>();

    /// <param name="controllers">The only controllers the host serves.</param>
    /// <param name="configure">Last word on the container.</param>
    /// <param name="origins">
    /// Which pages CORS, the guard and the framing headers trust. Defaults to what this
    /// build runs with — a development build, for the tests — so a test that needs a
    /// packaged build's answer passes one for that runtime instead.
    /// </param>
    public static async Task<ServiceGateHost> StartAsync(IReadOnlyCollection<Type> controllers,
        Action<IServiceCollection>? configure = null, ServiceCorsOrigins? origins = null)
    {
        var trusted = origins ?? ServiceCorsOrigins.ForThisBuild;
        var root = Path.Combine(Path.GetTempPath(), "bakabase-service-gates", Guid.NewGuid().ToString("N"));
        var port = LoopbackPortAllocator.Allocate(47000 + Random.Shared.Next(0, 2000));
        string[] apiEndpoints = [$"http://localhost:{port}"];

        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseKestrel()
                .UseUrls($"http://127.0.0.1:{port}")
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new AppContext
                    {
                        ListeningAddresses = [$"http://127.0.0.1:{port}"],
                        ApiEndpoints = apiEndpoints,
                        ApiEndpoint = apiEndpoints[0]
                    });

                    // As BakabaseStartup registers it; the guard and the framing headers
                    // read it from here.
                    services.AddSingleton(trusted);

                    // Legacy remote access: everything its middleware and the pairing
                    // controller resolve, over a throwaway directory.
                    services.AddSingleton<FakeRemoteAccessService>();
                    services.AddSingleton<IRemoteAccessService>(sp => sp.GetRequiredService<FakeRemoteAccessService>());
                    services.AddSingleton<IRemoteAccessDataDirectory>(new TempDirectory(Path.Combine(root, "remote-access")));
                    services.AddSingleton<IRemoteDeviceStore, RemoteDeviceStore>();
                    services.AddSingleton<IRemoteDeviceService>(sp =>
                        new RemoteDeviceService(sp.GetRequiredService<IRemoteDeviceStore>()));
                    services.AddSingleton(new NonceCache());
                    services.AddSingleton(sp => new RemoteDeviceAuthenticator(
                        sp.GetRequiredService<IRemoteDeviceService>(), sp.GetRequiredService<NonceCache>()));
                    services.AddSingleton(new PairingRequestRateLimiter());
                    services.AddSingleton<RemoteConnectionRegistry>();
                    services.AddSingleton<RecordingNotificationService>();
                    services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<RecordingNotificationService>());
                    services.AddTransient<IBakabaseLocalizer, TestBakabaseLocalizer>();

                    // Federation: the real state store and grant machinery, which its
                    // middleware resolves on every request.
                    services.AddSingleton<IFederationDataDirectory>(new TempDirectory(Path.Combine(root, "federation")));
                    services.AddSingleton<INodeIdSource>(new FixedNodeId());
                    services.AddFederationPeers();
                    services.AddSingleton(new FederationMediaSessions());
                    services.AddSingleton(sp => new FederationBrowsingControl(
                        sp.GetRequiredService<FederationStateStore>(), null!,
                        sp.GetRequiredService<FederationMediaSessions>()));

                    services.AddRouting();
                    services.AddSignalR();
                    services.AddControllers(o =>
                        {
                            // The Service's global filters that the gates rely on.
                            o.Filters.Add<RemoteAccessAuthorizationFilter>();
                            o.Filters.Add<LoopbackCrossSiteUserMachineFilter>();
                            o.Filters.Add<FederationLocalAccessFilter>();
                            o.Filters.Add<FederationExceptionFilter>();
                        })
                        .AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new CamelCaseNamingStrategy {ProcessDictionaryKeys = false}
                        })
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            foreach (var provider in parts.FeatureProviders.OfType<ControllerFeatureProvider>().ToList())
                            {
                                parts.FeatureProviders.Remove(provider);
                            }

                            parts.FeatureProviders.Add(new OnlyTheseControllers(controllers));
                        });

                    configure?.Invoke(services);
                })
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip))
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                        }

                        return next(context);
                    });

                    BakabaseStartup.UseRequestGates(app);
                    app.UseBootstrapCors(trusted.Configure, apiEndpoints);
                    app.UseRouting();

                    // Stands in for the SPA shell AppStartup serves at the root.
                    app.MapWhen(context => context.Request.Path == "/", spa => spa.Run(async context =>
                    {
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync("<!doctype html><title>Bakabase</title>");
                    }));

                    app.UseEndpoints(endpoints =>
                    {
                        // Every hub the Service maps: WebGuiHub (BakabaseStartup) and the
                        // progressor hub (AppStartup), at their production paths.
                        foreach (var hub in Hubs)
                        {
                            endpoints.MapHub<StubHub>(hub);
                        }

                        endpoints.MapControllers();
                    });
                }))
            .Build();

        await host.StartAsync();
        return new ServiceGateHost(host, port, root);
    }

    /// <summary>
    /// A request shaped the way a browser shapes it. <paramref name="site"/> and
    /// <paramref name="origin"/> are left off when null, as a player or a script would.
    /// </summary>
    public HttpRequestMessage Request(HttpMethod method, string path, string? site = null, string? origin = null,
        string? json = null)
    {
        var request = new HttpRequestMessage(method, $"{Origin}{path}");
        if (site != null)
        {
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        }

        if (origin != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (json != null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var handler = new SocketsHttpHandler {UseProxy = false, AllowAutoRedirect = false, UseCookies = false};
        using var client = new HttpClient(handler);
        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? site = null,
        string? origin = null, string? json = null) =>
        SendAsync(Request(method, path, site, origin, json));

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        try
        {
            Directory.Delete(Root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory, IFederationDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class FixedNodeId : INodeIdSource
    {
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("this-node");
    }

    private sealed class OnlyTheseControllers(IEnumerable<Type> controllers)
        : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
            {
                feature.Controllers.Add(controller.GetTypeInfo());
            }
        }
    }
}

/// <summary>Stands in for <c>WebGuiHub</c>, whose own services the gates never touch.</summary>
public sealed class StubHub : Hub
{
    public string Ping() => "pong";
}

/// <summary>An action of every shape the guard distinguishes, and nothing else.</summary>
[Route("~/test-probe")]
public sealed class GuardProbeController : ControllerBase
{
    [HttpGet("read")]
    public SingletonResponse<string> Read() => new("read");

    [HttpPost("write")]
    [HttpPut("write")]
    [HttpPatch("write")]
    [HttpDelete("write")]
    public SingletonResponse<string> Write() => new("written");

    [RunsOnUserMachine(Reason = "Test action that would open something on this machine.")]
    [HttpGet("open")]
    public SingletonResponse<string> Open() => new("opened");

    /// <summary>Sets a policy of its own, as the federation media endpoints do.</summary>
    [HttpGet("sandboxed")]
    public SingletonResponse<string> Sandboxed()
    {
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        return new SingletonResponse<string>("sandboxed");
    }
}

public sealed class FakeRemoteAccessService : IRemoteAccessService
{
    public RemoteAccessMode Mode { get; set; } = RemoteAccessMode.Enabled;
    public bool RequirePairing { get; set; }
    public RemoteAccessMode GetEffectiveMode() => Mode;

    public Task SetModeAsync(RemoteAccessMode? mode)
    {
        Mode = mode ?? RemoteAccessMode.Enabled;
        return Task.CompletedTask;
    }

    public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() => [];
    public Task<string> GetOrCreateServerIdAsync() => Task.FromResult("this-server");
    public bool GetAllowLiveTranscode() => false;
    public Task SetAllowLiveTranscodeAsync(bool allow) => Task.CompletedTask;
    public bool GetRequirePairing() => RequirePairing;

    public Task SetRequirePairingAsync(bool require)
    {
        RequirePairing = require;
        return Task.CompletedTask;
    }

    /// <summary>What <see cref="GetServerDescriptorAsync"/> answers: a server that does not say what it is, by default.</summary>
    public RemoteAccessServerDescriptor Descriptor { get; set; } = new("this-server", "Desk", null, "0.0.0", 1);

    public Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync() => Task.FromResult(Descriptor);
}

public sealed class RecordingNotificationService : INotificationService
{
    public ConcurrentQueue<NotificationCreationInputModel> Created { get; } = new();

    /// <summary>Set to make the next creations throw, as a failing database would.</summary>
    public bool Fail { get; set; }

    public Task<NotificationRecord> CreateAsync(NotificationCreationInputModel input)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Notification store unavailable.");
        }

        Created.Enqueue(input);
        return Task.FromResult(new NotificationRecord
        {
            Id = Created.Count, Source = input.Source, Title = input.Title, Body = input.Body,
            PayloadJson = input.PayloadJson, Severity = input.Severity, CreatedAt = DateTime.UtcNow
        });
    }

    public Task<SearchResponse<NotificationRecord>> SearchAsync(NotificationSearchInputModel input) =>
        throw new NotSupportedException();

    public Task<int> GetUnreadCountAsync() => Task.FromResult(Created.Count);
    public Task MarkAsReadAsync(int[]? ids) => Task.CompletedTask;
    public Task DeleteAsync(int[] ids) => Task.CompletedTask;
    public Task ClearReadAsync() => Task.CompletedTask;
}
