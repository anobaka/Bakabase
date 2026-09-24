using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Relay;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// What a relay's own container is made of: the thin client's relay core, with this app's
/// windows and logging, and one server's credentials — never the list, never the app's
/// container.
/// </summary>
[TestClass]
public class RelayCompositionTests
{
    private readonly TestGuiAdapter _gui = new();
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private FakeServer _nas = null!;
    private IServiceProvider _relay = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync(services: s => s.AddSingleton<IGuiAdapter>(_gui));
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);

        await _console.AddManagedAsync(_desk);
        await _console.AddManagedAsync(_nas);
        await _console.Manager.OpenAsync(_desk.ServerId, null);

        _relay = _console.Manager.RelayServices(_desk.ServerId)!;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
        await _nas.DisposeAsync();
    }

    [TestMethod]
    public void The_app_container_offers_nobody_a_connection_store()
    {
        // The managed store holds every server's key. Anything in the server that asked for
        // "the client's connection" would get all of them.
        Assert.IsNull(_console.Get<IServiceProvider>().GetService<IClientConnectionStore>());
        Assert.IsNull(_console.Get<IServiceProvider>().GetService<ActiveConnection>());
    }

    [TestMethod]
    public void A_relay_signs_for_its_own_server_and_sees_no_other()
    {
        var store = _relay.GetRequiredService<IClientConnectionStore>();

        Assert.IsInstanceOfType<SingleServerConnectionStore>(store);
        Assert.AreEqual(_desk.ServerId, store.Read().Servers.Single().ServerId);
        Assert.AreEqual(_desk.ServerId, _relay.GetRequiredService<ActiveConnection>().Server!.ServerId);
    }

    [TestMethod]
    public void A_relay_borrows_this_apps_window_and_logging_and_builds_the_rest()
    {
        Assert.AreSame(_gui, _relay.GetRequiredService<IGuiAdapter>());
        Assert.AreNotSame(_console.Get<IServiceProvider>(), _relay);

        // The app's own logging, with only the forwarder's per-request lines held back.
        var logging = _relay.GetRequiredService<ILoggerFactory>();
        Assert.IsInstanceOfType<RelayLogging.FlooredLoggerFactory>(logging);
        Assert.AreSame(_console.Get<ILoggerFactory>(), ((RelayLogging.FlooredLoggerFactory) logging).Inner);

        // Every action that runs on this machine resolves in the relay's container — a
        // missing dependency would otherwise surface as a 500 the first time someone clicks.
        var handlers = _relay.GetServices<IUserMachineHandler>().ToList();
        Assert.IsTrue(handlers.Count > 10, $"only {handlers.Count} handlers");
        Assert.IsNotNull(_relay.GetRequiredService<UserMachineDispatcher>());

        using var scope = _relay.CreateScope();
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<CookieCaptureOrchestrator>());
        Assert.IsTrue(scope.ServiceProvider.GetServices<ICookieCaptureFlow>().Any());

        // The sign-in window's own labels are real strings here, not resource keys.
        var localizer = scope.ServiceProvider.GetRequiredService<ICookieCaptureLocalizer>();
        Assert.AreNotEqual("CookieCapture_Confirm", localizer.Confirm);
    }

    [TestMethod]
    public void Nothing_a_relay_resolves_points_back_into_this_app()
    {
        var self = _relay.GetRequiredService<ClientSelfAddress>();

        // The app's own server and every relay, running or reserved, are all "self" to a
        // relay's handshake — so no pairing from a server's page can loop back here.
        Assert.IsTrue(self.Matches(new Uri($"http://127.0.0.1:{_console.ServicePort}")));
        Assert.IsTrue(self.Matches(new Uri($"http://localhost:{_console.Manager.RunningRelays[_desk.ServerId]}")));
        Assert.IsFalse(self.Matches(new Uri(_desk.BaseAddress)));
    }
}
