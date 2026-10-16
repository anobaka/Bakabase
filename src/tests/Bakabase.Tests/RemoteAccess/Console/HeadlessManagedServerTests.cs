using System.Net.Http;
using System.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Components.Management;
using Bakabase.Remoting.Components.Console;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

[TestClass]
public class HeadlessManagedServerTests
{
    private ServiceProvider _provider = null!;
    private IManagedServerService _manager = null!;
    private FakeServer _owner = null!;
    private string _root = null!;

    [TestInitialize]
    public async Task Start()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-headless-management", Guid.NewGuid().ToString("N"));
        _owner = await FakeServer.StartAsync("resource-owner", "Owner", 46900);
        _owner.PairingCode = "123456";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRemoteAccessService>(new FakeRemoteAccessService());
        services.AddSingleton<IServerDiscovery>(new FakeDiscovery());
        services.AddManagedServerConnections(new ManagedServerOptions
        {
            ManagedDirectory = _root,
            ClaimPollInterval = TimeSpan.FromMilliseconds(20)
        });
        _provider = services.BuildServiceProvider();
        _manager = _provider.GetRequiredService<IManagedServerService>();
        foreach (var hosted in _provider.GetServices<IHostedService>()) await hosted.StartAsync(default);
    }

    [TestCleanup]
    public async Task Stop()
    {
        await _provider.DisposeAsync();
        await _owner.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task Pair()
    {
        var paired = await _manager.PairAsync(_owner.BaseAddress, "123456");
        Assert.AreEqual(ManagedServerOutcome.Ok, paired.Outcome);
    }

    [TestMethod]
    public async Task HeadlessSupportsDirectionalManagementWithoutAWindowOrRelay()
    {
        Assert.IsInstanceOfType<ManagedServerManager>(_manager);
        Assert.IsNull(_provider.GetService<IMainViewSwitcher>());
        Assert.IsNull(_provider.GetService<RemoteConsoleManager>());
        Assert.IsTrue((await _manager.GetAsync(false)).Available);
        Assert.IsFalse(Directory.Exists(_root), "Merely starting management must not write a store.");
        await Pair();
        var view = await _manager.GetAsync(true);
        Assert.AreEqual(ManagedServerState.Online, view.Servers.Single().State);
        Assert.IsFalse(view.CanOpenDesktopView);
        Assert.IsNull(_provider.GetRequiredService<ManagedServerStore>().Find(_owner.ServerId)!.RelayPort);
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _manager.OpenAsync(_owner.ServerId, null));
        Assert.IsTrue(await _manager.ForgetAsync(_owner.ServerId));
        Assert.AreEqual(0, (await _manager.GetAsync(false)).Servers.Count);
    }

    [TestMethod]
    public async Task HeadlessCollectsApprovedPairingRequestsUsingTheSameStore()
    {
        var pending = await _manager.PairAsync(_owner.BaseAddress, null);
        Assert.AreEqual(ManagedServerOutcome.AwaitingApproval, pending.Outcome);
        _owner.Approved = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await _manager.GetAsync(false)).Servers.Count == 0) await Task.Delay(20, timeout.Token);
        var view = await _manager.GetAsync(true);
        Assert.AreEqual(_owner.ServerId, view.Servers.Single().ServerId);
        Assert.AreEqual(ManagedServerState.Online, view.Servers.Single().State);
    }

    [TestMethod]
    public async Task CopiedHeadlessInstallForgetsOnlyItsLocalManagementStore()
    {
        await Pair();
        var store = _provider.GetRequiredService<ManagedServerStore>();
        var original = store.Find(_owner.ServerId)!;
        var requestsBefore = _owner.Requests.Count;
        var devicesBefore = _owner.KnownDevices.Count;

        Assert.AreEqual(1, await _manager.ForgetAllLocallyAsync());

        Assert.AreEqual(0, (await _manager.GetAsync(false)).Servers.Count);
        Assert.IsNull(store.Find(_owner.ServerId));
        Assert.AreEqual(requestsBefore, _owner.Requests.Count,
            "Making a copied installation independent must not revoke the original's key.");
        Assert.AreEqual(devicesBefore, _owner.KnownDevices.Count);
        Assert.IsTrue(_owner.KnownDevices.ContainsKey(original.DeviceId));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _manager.SendAsync(_owner.ServerId, HttpMethod.Get, "/resource", null));
    }

    [TestMethod]
    public async Task ManagedRequestsUseOnlyThePairedOwnerAndCarryItsSignedBodyAndEpoch()
    {
        await Pair();
        _owner.RefusesUnknownDevices = true;
        _owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/resource/42") return false;
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"code\":\"LibraryChanged\"}");
            return true;
        };
        var result = await _manager.SendAsync(_owner.ServerId, HttpMethod.Put,
            "/resource/42?managedLibraryEpoch=epoch-1", "{\"name\":\"changed\"}");
        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("application/json", result.ContentType);
        StringAssert.Contains(result.Body, "LibraryChanged");
        var received = _owner.Requests.Single(r => r.Path == "/resource/42");
        Assert.AreEqual(true, received.SignatureValid);
        Assert.AreEqual("managedLibraryEpoch=epoch-1", received.Query);
        Assert.IsNull(received.Cookie);
        Assert.IsNull(received.Origin);
    }

    [TestMethod]
    public async Task UnknownOwnerCannotBorrowAReadOnlyGrantOrAnotherPairing()
    {
        await Pair();
        var requestsBefore = _owner.Requests.Count;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _manager.SendAsync("read-only-peer", HttpMethod.Get, "/resource", null));
        Assert.AreEqual(requestsBefore, _owner.Requests.Count);
    }

    [TestMethod]
    public async Task AnAddressTakenOverByAnotherInstallReceivesNoSignedApiRequest()
    {
        await Pair();
        var port = _owner.Port;
        await _owner.DisposeAsync();
        _owner = await FakeServer.TakeOverAsync("replacement-owner", "Other install", port);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _manager.SendAsync("resource-owner", HttpMethod.Delete, "/resource/42", null));
        Assert.IsFalse(_owner.Requests.Any(r => r.Path == "/resource/42"));
        Assert.IsFalse(_owner.Requests.Any(r => r.DeviceId != null));
    }

    [TestMethod]
    public async Task RevocationAndFailedOwnerResponsesAreNotReportedAsSuccess()
    {
        await Pair();
        _owner.KnownDevices.Clear();
        _owner.RefusesUnknownDevices = true;
        var response = await _manager.SendAsync(_owner.ServerId, HttpMethod.Get, "/resource", null);
        Assert.AreEqual(401, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("http://other/resource")]
    [DataRow("//other/resource")]
    [DataRow("/\\other/resource")]
    [DataRow("/resource#fragment")]
    [DataRow("/resource\r\nHeader: value")]
    public async Task InvalidTransportPathsNeverReachTheNetwork(string path)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _manager.SendAsync("owner", HttpMethod.Get, path, null));
        Assert.AreEqual(0, _owner.Requests.Count);
    }

    [TestMethod]
    public async Task OversizedResponsesAreBoundedEvenWithoutContentLength()
    {
        await Pair();
        _owner.HandleRequest = async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new string('x', 8 * 1024 * 1024 + 1));
            return true;
        };
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _manager.SendAsync(_owner.ServerId, HttpMethod.Get, "/large", null));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            _manager.SendAsync(_owner.ServerId, HttpMethod.Post, "/large", new string('x', 1024 * 1024 + 1)));
    }

    [TestMethod]
    public async Task SharedCompositionDoesNotReplaceTheDesktopAdapterOrStartASecondManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRemoteAccessService>(new FakeRemoteAccessService());
        services.AddSingleton<IServerDiscovery>(new FakeDiscovery());
        services.AddManagedServerConnections(new ManagedServerOptions {ManagedDirectory = _root});
        services.AddRemoteConsole(options => options.ManagedDirectory = _root);
        services.AddManagedServerConnections();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IManagedServerService>();
        Assert.IsInstanceOfType<RemoteConsoleManager>(manager);
        Assert.IsTrue((await manager.GetAsync(false)).CanOpenDesktopView);
        Assert.AreSame<object>(manager, provider.GetServices<IHostedService>().Single());
    }

    [TestMethod]
    public async Task HeadlessCannotLaunchAnApplicationOnEitherDevice()
    {
        await Pair();
        var before = _owner.Requests.Count;
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _manager.PlayManagedResourceAsync(
            _owner.ServerId, 42, DataOrigin.FileSystem, "/movie.mp4", "epoch"));
        Assert.AreEqual(before, _owner.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("bytes=0-1,5-9")]
    [DataRow("items=0-1")]
    [DataRow("bytes=9-1")]
    [DataRow("not-a-range")]
    public async Task InvalidMediaRangesAreRefusedBeforeAnyConnection(string range)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _manager.SendMediaAsync("owner", HttpMethod.Get,
            "/file/play", range, (_, _) => Task.CompletedTask));
        Assert.AreEqual(0, _owner.Requests.Count);
    }

    [TestMethod]
    public async Task MediaUsesTheVerifiedOwnerAndOnlyPassesTheRequestedByteRange()
    {
        await Pair();
        _owner.RefusesUnknownDevices = true;
        _owner.HandleRequest = async context =>
        {
            Assert.AreEqual("bytes=2-5", context.Request.Headers.Range.ToString());
            Assert.AreEqual("epoch-1", context.Request.Query["managedLibraryEpoch"].ToString());
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            context.Response.ContentType = "video/mp4";
            context.Response.Headers.ContentRange = "bytes 2-5/10";
            await context.Response.WriteAsync("2345");
            return true;
        };
        await _manager.SendMediaAsync(_owner.ServerId, HttpMethod.Get,
            "/file/play?fullname=%2Fmovie.mp4&managedLibraryEpoch=epoch-1", "bytes=2-5", async (response, ct) =>
            {
                Assert.AreEqual(206, (int) response.StatusCode);
                Assert.AreEqual("bytes 2-5/10", response.Content.Headers.ContentRange!.ToString());
                Assert.AreEqual("2345", await response.Content.ReadAsStringAsync(ct));
            });
        var request = _owner.Requests.Single(r => r.Path == "/file/play");
        Assert.AreEqual(true, request.SignatureValid);
        Assert.IsNull(request.Cookie);
        Assert.IsNull(request.Origin);
    }

    [TestMethod]
    public async Task AcceptedMediaOutlivesTheHeaderBudgetButStopsWhenItsViewerDisconnects()
    {
        await Pair();
        _provider.GetRequiredService<ManagedServerOptions>().RequestTimeout = TimeSpan.FromSeconds(1);
        _owner.HandleRequest = async context =>
        {
            context.Response.ContentType = "video/mp4";
            await context.Response.WriteAsync("first");
            await context.Response.Body.FlushAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(1200), context.RequestAborted);
            await context.Response.WriteAsync("second");
            await context.Response.Body.FlushAsync();
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
            return true;
        };
        using var viewer = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            _manager.SendMediaAsync(_owner.ServerId, HttpMethod.Get, "/file/play", null, async (response, ct) =>
            {
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                var bytes = new byte[11];
                await input.ReadExactlyAsync(bytes, ct);
                Assert.AreEqual("firstsecond", Encoding.UTF8.GetString(bytes));
                viewer.Cancel();
                await input.ReadAsync(bytes, ct);
            }, viewer.Token));
    }
}
