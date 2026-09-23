using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Client.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// A request going all the way from the client's window to a server and back.
/// </summary>
/// <remarks>
/// <para>
/// Everything up to this point was tested with no server attached. The guard, the
/// redirect, the refusals, the route table — every one of them exercises the path where
/// there is nothing upstream, because that path needs no second listener. The result was
/// that the forwarding layer's actual job, the one thing the thin client exists to do,
/// had no test at all: a client that paired successfully and then answered every request
/// with "the server is not answering" would have gone out green.
/// </para>
/// <para>
/// So this runs two real hosts on two real ports. The upstream is deliberately dumb — it
/// records what arrived and answers 200 — because what is under test is the client's half
/// of the exchange: that it reaches the address it stored, that it signs what it sends,
/// and that what comes back reaches the caller intact.
/// </para>
/// </remarks>
[TestClass]
public class ClientToServerForwardingTests
{
    private IHost _client = null!;
    private IHost _server = null!;
    private int _clientPort;
    private int _serverPort;
    private string _root = null!;
    private readonly List<HttpRequestRecord> _received = [];

    private sealed record HttpRequestRecord(string Method, string Path, string? Query, string? Authorization);

    private readonly List<string> _logs = [];

    /// <summary>
    /// Keeps the client's own warnings, so a failure here reports why the forwarder gave
    /// up instead of only that it did.
    /// </summary>
    private sealed class CaptureProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(sink, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capture(List<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                lock (sink)
                {
                    sink.Add($"{logLevel} {category}: {formatter(state, exception)}{(exception == null ? "" : " | " + exception)}");
                }
            }
        }
    }

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-client-forwarding", Guid.NewGuid().ToString("N"));
        _serverPort = LoopbackPortAllocator.Allocate(46000);
        _clientPort = LoopbackPortAllocator.Allocate(_serverPort + 1);

        _server = await StartUpstream();
        _client = await StartClient();
    }

    /// <summary>
    /// Stands in for a Bakabase server: records the request and answers. Not the real
    /// pipeline — the signature's agreement with the server is already covered by
    /// <see cref="ClientSigningRoundTripTests"/>, and what is unproven here is whether a
    /// forwarded request arrives at all.
    /// </summary>
    private async Task<IHost> StartUpstream()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls($"http://127.0.0.1:{_serverPort}")
                .Configure(app => app.Run(async context =>
                {
                    lock (_received)
                    {
                        _received.Add(new HttpRequestRecord(context.Request.Method, context.Request.Path,
                            context.Request.QueryString.Value,
                            context.Request.Headers.Authorization.ToString() is {Length: > 0} auth ? auth : null));
                    }

                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"code\":0,\"data\":\"{context.Request.Path}\"}}");
                })))
            .Build();

        await host.StartAsync();

        return host;
    }

    private async Task<IHost> StartClient()
    {
        var address = $"http://127.0.0.1:{_clientPort}";

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(address)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IClientDataDirectory>(new TempDirectory(_root));
                    services.AddSingleton(new AppContext
                    {
                        ListeningAddresses = [address],
                        ApiEndpoints = [address],
                        ApiEndpoint = address
                    });
                    services.AddLogging(b => b.AddProvider(new CaptureProvider(_logs)));
                })
                .UseStartup<ClientStartup>())
            .Build();

        await host.StartAsync();

        return host;
    }

    /// <summary>
    /// Records a paired server the way the pairing endpoints do, through the same object
    /// they use, so what is stored is what a real pairing would have stored.
    /// </summary>
    private async Task Pair()
    {
        var key = RemoteRequestSignature.ToBase64Url(RandomNumberGenerator.GetBytes(32));

        await _client.Services.GetRequiredService<ActiveConnection>().SaveAsync(
            "server-1", "Desk", $"127.0.0.1:{_serverPort}",
            new ClientCredentials("device-1", key), DateTime.UtcNow);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _client.StopAsync();
        _client.Dispose();
        await _server.StopAsync();
        _server.Dispose();

        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private async Task<HttpResponseMessage> Send(string path, HttpMethod? method = null, string? body = null)
    {
        using var handler = new HttpClientHandler {AllowAutoRedirect = false};
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"http://127.0.0.1:{_clientPort}{path}");

        request.Headers.Host = $"127.0.0.1:{_clientPort}";

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    [TestMethod]
    public async Task A_paired_client_actually_reaches_its_server()
    {
        await Pair();

        var response = await Send("/resource/search?page=2");

        // The failure this is here to catch reads as HTTP 503 with
        // X-Bakabase-Client: ServerUnreachable — the client's own "it is not answering",
        // which is what a user sees in place of their library.
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, string.Join("\n", _logs));

        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "/resource/search");

        var arrived = _received.Single();
        Assert.AreEqual("/resource/search", arrived.Path);
        Assert.AreEqual("?page=2", arrived.Query);
    }

    [TestMethod]
    public async Task What_reaches_the_server_is_signed_as_this_device()
    {
        await Pair();

        await Send("/resource/search");

        // Without this the server answers every forwarded request as an unpaired
        // stranger, and the client looks connected while showing nothing.
        var arrived = _received.Single();
        Assert.IsNotNull(arrived.Authorization);
        StringAssert.StartsWith(arrived.Authorization, RemoteRequestSignature.Scheme);
        StringAssert.Contains(arrived.Authorization, "device-1");
    }

    [TestMethod]
    public async Task A_post_reaches_the_server_too()
    {
        // A body is where the signing path does its extra work — it buffers and hashes
        // one — so a GET passing says less than it looks like it does.
        await Pair();

        var response = await Send("/resource/search", HttpMethod.Post, "{\"keyword\":\"x\"}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, string.Join("\n", _logs));
        Assert.AreEqual("POST", _received.Single().Method);
    }
}
