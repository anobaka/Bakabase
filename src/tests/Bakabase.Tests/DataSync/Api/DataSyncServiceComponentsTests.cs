using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.DataSync;
using Bakabase.Service.Controllers;
using Bakabase.Tests.DataSync.Runtime;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// What the Service host registers for data sync (spec §2.11, H-startup): the device identity over the federation node,
/// the host kind, the addresses, the headless sharing announcer (§7.8), and — until the runtime registers its own —
/// the placeholders that keep every <c>/data-sync</c> request answered.
/// </summary>
[TestClass]
public class DataSyncServiceComponentsTests
{
    private static readonly ServiceProviderOptions Strict = new() {ValidateScopes = true, ValidateOnBuild = true};

    private static ServiceCollection HostServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRemoteAccessService, FakeRemoteAccessService>();
        services.AddSingleton<INodeIdentityProvider>(new FixedNode());
        return services;
    }

    [TestMethod]
    public void Placeholders_fill_the_gaps_until_the_runtime_registers_its_own()
    {
        var services = HostServices();
        services.AddDataSyncServiceComponents();

        using var provider = services.BuildServiceProvider(Strict);
        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<UnavailableDataSyncService>(scope.ServiceProvider.GetRequiredService<IDataSyncService>());
        Assert.IsInstanceOfType<NoOpDataSyncGrantEvents>(provider.GetRequiredService<IDataSyncGrantEvents>());
    }

    [TestMethod]
    public void The_runtime_registration_wins()
    {
        // The runtime registers its facade and grant events earlier (through AddInsideWorldBusinesses); the
        // placeholders must then stay out, not replace them or register a second implementation.
        var services = HostServices();
        services.AddScoped<IDataSyncService, FakeDataSyncService>();
        services.AddSingleton<IDataSyncGrantEvents, RecordingGrantEvents>();
        services.AddDataSyncServiceComponents();

        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IDataSyncService)));
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IDataSyncGrantEvents)));
        using var provider = services.BuildServiceProvider(Strict);
        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<FakeDataSyncService>(scope.ServiceProvider.GetRequiredService<IDataSyncService>());
        Assert.IsInstanceOfType<RecordingGrantEvents>(provider.GetRequiredService<IDataSyncGrantEvents>());
    }

    [TestMethod]
    public void The_runtime_registers_the_real_facade_first()
    {
        var services = new ServiceCollection();
        services.AddDataSyncRuntime();
        services.AddDataSyncServiceComponents();
        Assert.AreEqual(typeof(InsideWorld.Business.Components.DataSync.DataSyncService),
            services.Single(d => d.ServiceType == typeof(IDataSyncService)).ImplementationType);
        Assert.AreEqual(typeof(DataSyncRuntimeEvents),
            services.Single(d => d.ServiceType == typeof(IDataSyncRuntimeObserver)).ImplementationType);
    }

    [TestMethod]
    public async Task The_host_says_who_this_device_is_what_kind_it_is_and_where_to_reach_it()
    {
        var services = HostServices();
        services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => ServerKind.Headless));
        services.AddDataSyncServiceComponents();
        await using var provider = services.BuildServiceProvider(Strict);

        var device = await provider.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default);
        Assert.AreEqual(new DataSyncDevice("node-1", "epoch-1", "NAS"), device);
        Assert.IsTrue(provider.GetRequiredService<IDataSyncHostKind>().IsHeadless);
        Assert.IsInstanceOfType<ServiceDataSyncHostAddresses>(provider.GetRequiredService<IDataSyncHostAddresses>());

        // One address per host, as the library shows next to its code.
        var remote = AddressesOnly.Create([new RemoteAccessAddress("http://192.168.1.2:5000", "en0"),
            new RemoteAccessAddress("http://192.168.1.2:5001", "en0"),
            new RemoteAccessAddress("http://10.0.0.2:5000", "en1")]);
        CollectionAssert.AreEqual(new[] {"http://192.168.1.2:5000", "http://10.0.0.2:5000"},
            new ServiceDataSyncHostAddresses(remote).GetReachableAddresses().ToArray());
    }

    /// <summary>Remote access that only knows its addresses.</summary>
    public class AddressesOnly : System.Reflection.DispatchProxy
    {
        private IReadOnlyList<RemoteAccessAddress> _addresses = [];

        public static IRemoteAccessService Create(IReadOnlyList<RemoteAccessAddress> addresses)
        {
            var proxy = Create<IRemoteAccessService, AddressesOnly>();
            ((AddressesOnly) (object) proxy)._addresses = addresses;
            return proxy;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IRemoteAccessService.GetReachableAddresses)
                ? _addresses
                : throw new NotSupportedException(targetMethod?.Name);
    }

    [TestMethod]
    public void A_desktop_host_is_not_headless()
    {
        var services = HostServices();
        services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => ServerKind.Desktop));
        services.AddDataSyncServiceComponents();
        using var provider = services.BuildServiceProvider(Strict);
        Assert.IsFalse(provider.GetRequiredService<IDataSyncHostKind>().IsHeadless);
    }

    [TestMethod]
    public async Task The_variable_turns_sharing_on_at_every_start_with_paired_remote_access_only_from_disabled()
    {
        var grants = new FakeDataSyncGrantService { SharingEnabled = false, RemoteAccessMode = RemoteAccessMode.Disabled };
        var services = new ServiceCollection();
        services.AddSingleton<IDataSyncGrantService>(grants);
        await using var provider = services.BuildServiceProvider(Strict);
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        await new DataSyncSharingAnnouncer(scopes, NullLogger<DataSyncSharingAnnouncer>.Instance)
            { ReadVariable = _ => "false" }.StartAsync(default);
        Assert.AreEqual(0, grants.Changes.Count);

        await new DataSyncSharingAnnouncer(scopes, NullLogger<DataSyncSharingAnnouncer>.Instance)
            { ReadVariable = name => name == DataSyncSharingAnnouncer.SharingVariable ? "true" : null }.StartAsync(default);
        CollectionAssert.AreEqual(new[] {"sharing:True:True"}, grants.Changes.ToArray());
        Assert.IsTrue(grants.SharingEnabled);
        Assert.AreEqual(RemoteAccessMode.Enabled, grants.RemoteAccessMode);

        // A Docker install's Unrestricted mode is never touched.
        grants.RemoteAccessMode = RemoteAccessMode.Unrestricted;
        await new DataSyncSharingAnnouncer(scopes, NullLogger<DataSyncSharingAnnouncer>.Instance)
            { ReadVariable = _ => "true" }.StartAsync(default);
        Assert.AreEqual(RemoteAccessMode.Unrestricted, grants.RemoteAccessMode);
    }

    [TestMethod]
    public async Task A_build_that_cannot_share_definitions_still_starts()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider(Strict);
        await new DataSyncSharingAnnouncer(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DataSyncSharingAnnouncer>.Instance) { ReadVariable = _ => "true" }.StartAsync(default);
    }

    [TestMethod]
    public async Task The_controller_answers_through_the_placeholder()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(DataSyncController)],
            services => services.AddDataSyncServiceComponents());

        // A read answers a switched-off data sync, for this device's own window.
        using (var overview = await host.SendAsync(HttpMethod.Get, "/data-sync/overview", "same-origin", host.Origin))
        {
            Assert.AreEqual(HttpStatusCode.OK, overview.StatusCode);
            using var body = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
            var data = body.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("canManageSharing").GetBoolean());
            Assert.AreEqual(UnavailableDataSyncService.NotAvailableCode,
                data.GetProperty("status").GetProperty("lastErrorCode").GetString());
        }

        // Everything else answers that this build cannot do it yet, as an expected failure.
        using (var link = await host.SendAsync(HttpMethod.Post, "/data-sync/links", "same-origin", host.Origin,
                   """{"peerNodeId":"node-nas","mode":1,"kinds":["customProperty"]}"""))
        {
            Assert.AreEqual(HttpStatusCode.OK, link.StatusCode);
            using var body = JsonDocument.Parse(await link.Content.ReadAsStringAsync());
            Assert.AreEqual((int) DataSyncProblemCode.ThisTooOld,
                body.RootElement.GetProperty("data").GetProperty("problem").GetProperty("code").GetInt32());
        }
    }

    private sealed class FixedNode : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("node-1", "epoch-1", "NAS"));
    }

    private sealed class RecordingGrantEvents : IDataSyncGrantEvents
    {
        public void OutboundGranted(string peerNodeId)
        {
        }

        public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted)
        {
        }
    }
}
