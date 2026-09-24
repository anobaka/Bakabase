using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Extensions;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bakabase.Tests.RemoteAccess.Service;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// What an install tells other devices it is — the desktop app or a headless server, and what
/// it runs on — so their device maps can show it. Optional everywhere: added as trailing
/// fields, left out by older installs, and never a reason to refuse anything.
/// </summary>
[TestClass]
public class ServerSelfDescriptionTests
{
    private sealed class NoAddresses : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() => [];
    }

    private sealed class Says(ServerKind? kind, RemoteDevicePlatform? platform) : IServerSelfDescription
    {
        public ServerKind? Kind => kind;
        public RemoteDevicePlatform? Platform => platform;
    }

    private static RemoteAccessService Service(IServerSelfDescription? self) =>
        new(new TestBOptionsManager<RemoteAccessOptions>(new RemoteAccessOptions {ServerId = "this-server"}),
            new RemoteAccessDefaults(RemoteAccessMode.Enabled), new RemoteAccessHostInfo("2.4.0"), new NoAddresses(),
            NullLogger<RemoteAccessService>.Instance, self);

    [TestMethod]
    public async Task The_descriptor_carries_what_the_host_says_and_nothing_it_cannot_tell()
    {
        var said = await Service(new Says(ServerKind.Headless, RemoteDevicePlatform.Linux)).GetServerDescriptorAsync();

        Assert.AreEqual(ServerKind.Headless, said.Kind);
        Assert.AreEqual(RemoteDevicePlatform.Linux, said.Platform);

        var silent = await Service(null).GetServerDescriptorAsync();

        Assert.IsNull(silent.Kind);
        Assert.IsNull(silent.Platform);

        // Unknown is not a kind, and a number this build does not define is nothing.
        var odd = await Service(new Says(ServerKind.Unknown, (RemoteDevicePlatform) 42)).GetServerDescriptorAsync();

        Assert.IsNull(odd.Kind);
        Assert.IsNull(odd.Platform);
    }

    [TestMethod]
    public void By_default_the_platform_is_this_process_and_the_kind_is_not_guessed()
    {
        var services = new ServiceCollection().AddLogging()
            .AddSingleton<IListeningAddressProvider, NoAddresses>();

        services.AddRemoteAccess(RemoteAccessMode.Disabled, "2.4.0");
        using var provider = services.BuildServiceProvider();
        var self = provider.GetRequiredService<IServerSelfDescription>();

        Assert.IsNull(self.Kind);
        Assert.AreEqual(ServerSelfDescription.CurrentPlatform, self.Platform);
        if (OperatingSystem.IsMacOS()) Assert.AreEqual(RemoteDevicePlatform.MacOS, self.Platform);
        if (OperatingSystem.IsWindows()) Assert.AreEqual(RemoteDevicePlatform.Windows, self.Platform);
        if (OperatingSystem.IsLinux()) Assert.AreEqual(RemoteDevicePlatform.Linux, self.Platform);
    }

    [TestMethod]
    public void The_desktop_builds_are_the_desktop_app_and_the_container_a_headless_server()
    {
        Assert.AreEqual(ServerKind.Desktop, ServiceSelfDescription.KindOf(RuntimeMode.WinForms, false));
        Assert.AreEqual(ServerKind.Desktop, ServiceSelfDescription.KindOf(RuntimeMode.MacOS, false));
        Assert.AreEqual(ServerKind.Headless, ServiceSelfDescription.KindOf(RuntimeMode.Docker, true));
        // A development run is whatever it was composed as.
        Assert.AreEqual(ServerKind.Desktop, ServiceSelfDescription.KindOf(RuntimeMode.Dev, true));
        Assert.AreEqual(ServerKind.Headless, ServiceSelfDescription.KindOf(RuntimeMode.Dev, false));
    }

    [TestMethod]
    public void A_development_run_is_the_desktop_app_only_when_composed_with_the_server_manager()
    {
        using var headless = new ServiceCollection().BuildServiceProvider();
        // Registered, and never to be resolved: the manager depends on remote access, which asks.
        using var desktop = new ServiceCollection()
            .AddSingleton<IManagedServerService>(_ => throw new InvalidOperationException("resolved"))
            .BuildServiceProvider();

        Assert.AreEqual(ServerKind.Headless, ServiceSelfDescription.Create(headless, RuntimeMode.Dev).Kind);
        Assert.AreEqual(ServerKind.Desktop, ServiceSelfDescription.Create(desktop, RuntimeMode.Dev).Kind);
        Assert.AreEqual(ServerSelfDescription.CurrentPlatform, ServiceSelfDescription.Create(desktop, RuntimeMode.Dev).Platform);
    }

    [TestMethod]
    public async Task Server_info_says_what_kind_of_install_this_is_and_leaves_it_out_when_unknown()
    {
        var access = new FakeRemoteAccessService
        {
            Descriptor = new RemoteAccessServerDescriptor("this-server", "NAS", 5000, "2.4.0", 1, ServerKind.Headless,
                RemoteDevicePlatform.Linux)
        };
        var root = Path.Combine(Path.GetTempPath(), "bakabase-self-description", Guid.NewGuid().ToString("N"));
        var devices = new RemoteDeviceService(new RemoteDeviceStore(new TempDirectory(root)), () => DateTime.UtcNow);
        var controller = new RemoteAccessController(access, devices, new RemoteConnectionRegistry(),
            new RecordingNotificationService(), new PairingRequestRateLimiter(() => DateTime.UtcNow))
        {
            ControllerContext = new ControllerContext {HttpContext = new DefaultHttpContext()}
        };

        var info = (await controller.GetServerInfo()).Data!;

        Assert.AreEqual("this-server", info.Id);
        Assert.AreEqual(ServerKind.Headless, info.Kind);
        Assert.AreEqual(RemoteDevicePlatform.Linux, info.Platform);
        // The contract the protocol version guards is unchanged: saying more is not a new protocol.
        Assert.AreEqual(1, info.ProtocolVersion);

        access.Descriptor = new RemoteAccessServerDescriptor("this-server", "NAS", 5000, "2.4.0", 1);
        info = (await controller.GetServerInfo()).Data!;

        Assert.IsNull(info.Kind);
        Assert.IsNull(info.Platform);
    }

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }
}
