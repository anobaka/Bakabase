using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// What the other parts of the app rely on the console for: the shell's tray, which asks for
/// the list from its UI thread; the window, which navigates to whatever URL it is handed; and
/// the relay's guard, which has to accept the tickets the app mints.
/// </summary>
[TestClass]
public class ConsoleIntegrationContractTests
{
    private ConsoleHarness _console = null!;
    private readonly List<FakeServer> _servers = [];

    [TestInitialize]
    public async Task Setup() => _console = await ConsoleHarness.StartAsync();

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }

    private async Task<FakeServer> Server(string id, string name)
    {
        var server = await FakeServer.StartAsync(id, name, 46900);
        _servers.Add(server);
        return server;
    }

    // ---- the shell's tray: IMainViewSwitcher.ListTargets ----

    [TestMethod]
    public async Task The_tray_can_list_before_the_host_starts_without_building_the_manager()
    {
        // The shell resolves the switcher from the container as soon as it exists, which is
        // before the host starts its hosted services. Resolving the manager there would read
        // the managed-server file and build the server's remote-access service on the UI
        // thread; the switcher must answer without either.
        var remoteAccessBuilt = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRemoteAccessService>(_ =>
        {
            Interlocked.Increment(ref remoteAccessBuilt);
            throw new InvalidOperationException("the tray must not build this");
        });
        services.AddRemoteConsole(o => o.ManagedDirectory = _console.ManagedDirectory);

        await using var provider = services.BuildServiceProvider();
        var switcher = provider.GetRequiredService<IMainViewSwitcher>();

        var targets = switcher.ListTargets();

        Assert.AreEqual(MainViewTarget.LocalId, targets.Single().Id);
        Assert.IsTrue(targets.Single().IsLocal);
        Assert.AreEqual(Environment.MachineName, targets.Single().Name);
        Assert.IsNull(await switcher.ResolveUrlAsync(MainViewTarget.LocalId));
        Assert.AreEqual(0, remoteAccessBuilt);
        Assert.IsFalse(((RemoteConsoleSwitcher) switcher).IsAttached);
    }

    [TestMethod]
    public async Task Once_the_manager_exists_the_tray_lists_every_managed_server_from_memory()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        await _console.AddManagedAsync(desk);
        await _console.AddManagedAsync(nas);

        var switcher = _console.Get<IMainViewSwitcher>();
        Assert.IsInstanceOfType<RemoteConsoleSwitcher>(switcher, "the shell must get the memory-only view");

        var sent = desk.Requests.Count + nas.Requests.Count;

        // The file is gone and the directory unreadable as far as anybody knows: the list
        // still comes back, because nothing here reads the disk.
        File.Delete(_console.ManagedFile);

        var targets = switcher.ListTargets();

        CollectionAssert.AreEqual(new[] {MainViewTarget.LocalId, "server-desk", "server-nas"},
            targets.Select(t => t.Id).ToArray());
        CollectionAssert.AreEqual(new[] {true, false, false}, targets.Select(t => t.IsLocal).ToArray());
        Assert.AreEqual("Desk", targets[1].Name);

        // And not the network either.
        Assert.AreEqual(sent, desk.Requests.Count + nas.Requests.Count);
    }

    [TestMethod]
    public async Task Listing_is_safe_from_any_thread_while_servers_come_and_go()
    {
        var switcher = _console.Get<IMainViewSwitcher>();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        var failures = new List<Exception>();

        // The shell reads from the UI thread and from a timer while pairing, forgetting and
        // importing write from request threads.
        var readers = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var targets = switcher.ListTargets();

                    if (targets.Count == 0 || targets[0].Id != MainViewTarget.LocalId ||
                        targets.Skip(1).Any(t => t.IsLocal || string.IsNullOrEmpty(t.Name)))
                    {
                        throw new InvalidOperationException("an inconsistent list: " +
                                                            string.Join(",", targets.Select(t => t.Id)));
                    }
                }
                catch (Exception e)
                {
                    lock (failures)
                    {
                        failures.Add(e);
                    }
                }
            }
        })).ToArray();

        var writes = 0;
        while (!stop.IsCancellationRequested)
        {
            var id = $"server-{writes++ % 7}";

            await _console.Store.MutateAsync(data =>
            {
                if (data.Servers.RemoveAll(s => s.ServerId == id) == 0)
                {
                    data.Servers.Add(new ClientServerConnection
                    {
                        ServerId = id, ServerName = id, BaseAddress = "http://192.0.2.1:34567", DeviceId = "d",
                        DeviceKey = "k", PairedAt = DateTime.UtcNow
                    });
                }
            });
        }

        await Task.WhenAll(readers);

        Assert.IsTrue(writes > 10, $"only {writes} writes happened");
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    // ---- the window: ResolveUrlAsync ----

    [TestMethod]
    public async Task Every_target_resolves_to_an_absolute_http_url()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);
        var switcher = _console.Get<IMainViewSwitcher>();

        foreach (var target in switcher.ListTargets())
        {
            var url = await switcher.ResolveUrlAsync(target.Id);

            Assert.IsNotNull(url, target.Id);
            Assert.IsTrue(Uri.TryCreate(url, UriKind.Absolute, out var parsed), url);
            Assert.AreEqual(Uri.UriSchemeHttp, parsed.Scheme, url);
        }

        Assert.IsNull(await switcher.ResolveUrlAsync("nobody"));
    }

    [TestMethod]
    public async Task This_device_is_exactly_the_origin_its_main_window_opened_at()
    {
        var switcher = _console.Get<IMainViewSwitcher>();

        // Before the window opens: the server's own first address, as AppHost reports it —
        // 0.0.0.0 already turned into localhost.
        Assert.AreEqual($"http://localhost:{_console.ServicePort}/", await switcher.ResolveUrlAsync("local"));

        // AppHost opens the window at the first listening address with 0.0.0.0 as localhost,
        // or the dev server in a debug build, then lets the host add the startup page's hash
        // route. The origin of that — not the path, not the hash — is the one to go back to:
        // localhost and 127.0.0.1 are different origins with different storage.
        _console.LocalOrigin.Set($"http://localhost:{_console.ServicePort}/#/resource");
        Assert.AreEqual($"http://localhost:{_console.ServicePort}/", await switcher.ResolveUrlAsync("local"));

        _console.LocalOrigin.Set("http://localhost:3000");
        Assert.AreEqual("http://localhost:3000/", await switcher.ResolveUrlAsync("local"));
    }

    [TestMethod]
    public async Task Resolving_honours_cancellation_at_once_and_while_a_server_is_slow()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);
        var switcher = _console.Get<IMainViewSwitcher>();

        using (var cancelled = new CancellationTokenSource())
        {
            await cancelled.CancelAsync();

            foreach (var id in new[] {"local", "server-desk"})
            {
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => switcher.ResolveUrlAsync(id, cancelled.Token),
                    id);
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => _console.Manager.ResolveUrlAsync(id, null, cancelled.Token), id);
            }
        }

        Assert.AreEqual(0, _console.Manager.RunningRelays.Count, "a cancelled request started a relay");

        // The first open of a launch asks the server for its clock. A server that has stopped
        // answering must not hold the tray's request past its caller's deadline.
        desk.ServerInfoDelay = TimeSpan.FromSeconds(20);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        {
            try
            {
                await switcher.ResolveUrlAsync("server-desk", deadline.Token);
            }
            catch (OperationCanceledException e)
            {
                // TaskCanceledException derives from it; either is an honoured cancellation.
                throw new OperationCanceledException(e.Message, e, e.CancellationToken);
            }
        });

        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(1.5),
            $"took {DateTime.UtcNow - started} to give up");
    }

    // ---- the relay's guard: tickets ----

    [TestMethod]
    public async Task The_ticket_the_app_mints_is_the_one_the_relays_guard_consumes()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        await _console.AddManagedAsync(desk);
        await _console.AddManagedAsync(nas);

        var deskUrl = (await _console.Manager.OpenAsync(desk.ServerId, null))!.Url;
        var nasUrl = (await _console.Manager.OpenAsync(nas.ServerId, null))!.Url;

        // One instance, shared: the app's container and every relay's hold the same object.
        var shared = _console.Get<RelayNavigationTokens>();
        Assert.AreSame(shared, _console.Manager.RelayServices(desk.ServerId)!.GetRequiredService<RelayNavigationTokens>());
        Assert.AreSame(shared, _console.Manager.RelayServices(nas.ServerId)!.GetRequiredService<RelayNavigationTokens>());

        // And so the guard admits the window switching in from this device's own origin.
        var landing = await ConsoleHarness.NavigateAsync(deskUrl, "cross-site");
        Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode, await landing.Content.ReadAsStringAsync());

        // A ticket from any other set is just another site's navigation.
        var deskPort = ConsoleHarness.PortOf(deskUrl);
        var foreign = new RelayNavigationTokens().Mint(deskPort);
        var refused = await ConsoleHarness.NavigateAsync(
            $"http://127.0.0.1:{deskPort}/?{RelayNavigationTokens.QueryName}={foreign}", "cross-site");
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);

        // A ticket is for its own relay: the NAS's does not open the desk.
        var nasTicket = new Uri(nasUrl).Query[(RelayNavigationTokens.QueryName.Length + 2)..];
        var crossed = await ConsoleHarness.NavigateAsync(
            $"http://127.0.0.1:{deskPort}/?{RelayNavigationTokens.QueryName}={nasTicket}", "cross-site");
        Assert.AreEqual(HttpStatusCode.BadRequest, crossed.StatusCode);
    }

    [TestMethod]
    public async Task Opening_lands_on_the_page_asked_for_and_reaches_the_server_signed_without_cookie_or_ticket()
    {
        var desk = await Server("server-desk", "Desk");
        var (deviceId, _) = await _console.AddManagedAsync(desk);

        // A path with a query and the UI's hash route, as the switcher sends it.
        var url = (await _console.Manager.OpenAsync(desk.ServerId, "/resource/42?tab=files#/resource/42"))!.Url;
        Assert.AreEqual("#/resource/42", new Uri(url).Fragment);

        // The window arrives from this device's own origin — another site as far as the
        // browser is concerned — carrying whatever cookies it holds for 127.0.0.1.
        var landing = await ConsoleHarness.NavigateAsync(url, "cross-site", cookie: "session=for-another-port");
        Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode);
        StringAssert.StartsWith(landing.Content.Headers.ContentType!.MediaType, "text/html");
        Assert.IsTrue(desk.Requests.All(r => r.Path != "/resource/42"), "the ticketed request itself was forwarded");

        // The landing page moves on by itself, keeping the fragment, from its own origin.
        var next = await ConsoleHarness.ContinuationOfAsync(landing, new Uri(url).Fragment);
        Assert.AreEqual($"http://127.0.0.1:{ConsoleHarness.PortOf(url)}/resource/42?tab=files#/resource/42", next);

        var followed = await ConsoleHarness.NavigateAsync(next, "same-origin", cookie: "session=for-another-port");
        Assert.AreEqual(HttpStatusCode.OK, followed.StatusCode, await followed.Content.ReadAsStringAsync());

        var arrived = desk.Requests.Single(r => r.Path == "/resource/42");
        Assert.AreEqual("tab=files", arrived.Query, "the ticket reached the server");
        Assert.AreEqual(deviceId, arrived.DeviceId);
        Assert.IsTrue(arrived.SignatureValid);
        Assert.IsNull(arrived.Cookie, "a cookie reached the server");

        // Spent on the way in: the same address again is refused.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await ConsoleHarness.NavigateAsync(url, "cross-site")).StatusCode);
    }
}
