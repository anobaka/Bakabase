using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.Service.Services;
using Bakabase.TestKit.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.RemoteAccess.Console;

[TestClass]
public sealed class ResourceDiscoveryStreamTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Discovery_stream_is_ready_before_any_subscription_or_heartbeat(bool throughRelay)
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var lifetime = (IAsyncDisposable) services;
        using var discovery = new ResourceDiscoveryService(NullLogger<ResourceDiscoveryService>.Instance,
            services.GetRequiredService<IServiceScopeFactory>());
        await discovery.StartAsync(CancellationToken.None);
        await using var console = await ConsoleHarness.StartAsync();
        await using var server = await FakeServer.StartAsync("discovery-source", "Discovery", 49300);
        server.HandleRequest = async context =>
        {
            if (context.Request.Path != "/resource/discovery/stream") return false;
            await new ResourceDiscoveryController(discovery)
            {
                ControllerContext = new ControllerContext {HttpContext = context}
            }.Stream();
            return true;
        };
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);
        var origin = throughRelay ? $"http://127.0.0.1:{port}" : server.BaseAddress;
        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false});
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // The browser waits for onopen before posting a discovery subscription. It
            // cannot make the initial response arrive by subscribing first. In particular,
            // YARP does not forward an upstream flush containing headers but no body.
            using var response = await http.GetAsync(origin + "/resource/discovery/stream",
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            Assert.AreEqual(": heartbeat", await reader.ReadLineAsync(deadline.Token));
            Assert.AreEqual("", await reader.ReadLineAsync(deadline.Token));

            // A fast first result must not race connection registration after onopen.
            await discovery.EnqueueRequest(int.MaxValue, DataOrigin.FileSystem, ResourceDataType.PlayableItem);
            Assert.AreEqual("event: error", await reader.ReadLineAsync(deadline.Token));
            StringAssert.Contains((await reader.ReadLineAsync(deadline.Token))!, "no longer exists");
            if (throughRelay)
                Assert.IsTrue(server.Requests.Single(r => r.Path == "/resource/discovery/stream").SignatureValid);
        }
        finally
        {
            await discovery.StopAsync(CancellationToken.None);
        }
    }
}
