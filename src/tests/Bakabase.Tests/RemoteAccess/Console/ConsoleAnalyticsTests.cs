using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.RemoteAccess.Console;

[TestClass]
public sealed class ConsoleAnalyticsTests
{
    [TestMethod]
    public async Task DesktopIdentityStaysLocalAcrossServerSwitchAndConsentIsLive()
    {
        var provider = new ContextProvider();
        await using var console = await ConsoleHarness.StartAsync(services: s =>
            s.AddSingleton<IDesktopAnalyticsContextProvider>(provider));
        await using var first = await FakeServer.StartAsync("analytics-first", "First", 46900);
        await using var second = await FakeServer.StartAsync("analytics-second", "Second", 46900);
        await console.AddManagedAsync(first);
        await console.AddManagedAsync(second);
        var firstPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(first.ServerId, null))!.Url);
        var secondPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(second.ServerId, null))!.Url);
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        async Task<HttpResponseMessage> Request(int port, bool native)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/client/analytics-context");
            request.Headers.UserAgent.ParseAdd(native ? "BakabaseDesktop/1.0" : "Mozilla/5.0");
            return await client.SendAsync(request);
        }
        foreach (var port in new[] { firstPort, secondPort })
        {
            using var refused = await Request(port, false);
            Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
            using var response = await Request(port, true);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.Headers.CacheControl!.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(provider.Id, json.RootElement.GetProperty("data").GetProperty("clientId").GetString());
        }
        provider.Enabled = false;
        using var disabled = await Request(secondPort, true);
        using var body = JsonDocument.Parse(await disabled.Content.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.GetProperty("data").GetProperty("enableAnonymousDataTracking").GetBoolean());
        Assert.IsFalse(body.RootElement.GetProperty("data").TryGetProperty("clientId", out _));
        foreach (var request in first.Requests)
            Assert.IsFalse(request.Path.StartsWith("/client/analytics-context", StringComparison.Ordinal));
        foreach (var request in second.Requests)
            Assert.IsFalse(request.Path.StartsWith("/client/analytics-context", StringComparison.Ordinal));
    }

    private sealed class ContextProvider : IDesktopAnalyticsContextProvider
    {
        public string Id { get; } = Guid.NewGuid().ToString("D");
        public bool Enabled { get; set; } = true;
        public DesktopAnalyticsContext Read() => new(Enabled ? Id : null, "1.0.0", Enabled);
    }
}
