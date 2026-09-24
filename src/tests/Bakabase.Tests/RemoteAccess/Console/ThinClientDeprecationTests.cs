using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Client.Remoting.Components.Forwarding;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.TestKit.Implementations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// The retired thin client says so, wherever its user looks — and the desktop app's relay,
/// which answers the same <c>/client</c> API, does not.
/// </summary>
[TestClass]
public class ThinClientDeprecationTests
{
    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestMethod]
    public async Task The_thin_clients_status_says_it_is_deprecated()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-thin-client", Guid.NewGuid().ToString("N"));
        var port = LoopbackPortAllocator.Allocate(45300);
        var address = $"http://127.0.0.1:{port}";

        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(address)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IClientDataDirectory>(new TempDirectory(root));
                    services.AddSingleton<IGuiAdapter>(new TestGuiAdapter());
                    services.AddSingleton(new AppContext
                    {
                        ListeningAddresses = [address], ApiEndpoints = [address], ApiEndpoint = address
                    });
                })
                .UseStartup<ClientStartup>())
            .Build();

        await host.StartAsync();
        try
        {
            var response = await ConsoleHarness.SendToRelayAsync(port, "/client/status");
            var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(data.GetProperty("deprecated").GetBoolean());
            Assert.IsFalse(data.TryGetProperty("host", out _), "the thin client must not claim to be the console");
        }
        finally
        {
            await host.StopAsync();
            ConsoleHarness.DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task The_console_is_not_deprecated_and_says_what_it_is()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        await console.AddManagedAsync(desk);

        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(desk.ServerId, null))!.Url);
        var data = JsonDocument.Parse(await (await ConsoleHarness.SendToRelayAsync(port, "/client/status")).Content
            .ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.IsFalse(data.TryGetProperty("deprecated", out _));
        Assert.AreEqual("console", data.GetProperty("host").GetString());
    }

    [TestMethod]
    public void The_connect_page_tells_its_user_to_move_in_both_languages()
    {
        var html = ConnectPage.Html;

        // Shown unconditionally: no script decides whether a retired client is retired.
        var panel = Regex.Match(html, @"<section class=""panel retired"" id=""retired""(?<attributes>[^>]*)>");
        Assert.IsTrue(panel.Success, "no retirement notice on the connect page");
        Assert.IsFalse(panel.Groups["attributes"].Value.Contains("hidden", StringComparison.Ordinal));

        foreach (var key in new[] {"retired.heading", "retired.body"})
        {
            StringAssert.Contains(html, $"data-t=\"{key}\"");
            Assert.AreEqual(2, Regex.Matches(html, $@"^\s*'{Regex.Escape(key)}':", RegexOptions.Multiline).Count,
                $"{key} is not in both dictionaries");
        }

        StringAssert.Contains(html, "desktop app");
        StringAssert.Contains(html, "桌面版");
    }
}
