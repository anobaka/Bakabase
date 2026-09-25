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
/// the host kind, the addresses and the headless sharing announcer (§7.8). The facade and the grant events are the
/// runtime's alone.
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
    public void The_host_registers_no_facade_and_no_grant_events_of_its_own()
    {
        // The runtime registers them earlier (through AddInsideWorldBusinesses); the host adds no second
        // implementation and replaces none.
        var services = HostServices();
        services.AddDataSyncServiceComponents();
        Assert.AreEqual(0, services.Count(d => d.ServiceType == typeof(IDataSyncService)));
        Assert.AreEqual(0, services.Count(d => d.ServiceType == typeof(IDataSyncGrantEvents)));

        services.AddScoped<IDataSyncService, FakeDataSyncService>();
        services.AddSingleton<IDataSyncGrantEvents, RecordingGrantEvents>();
        services.AddDataSyncServiceComponents();
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IDataSyncService)));
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IDataSyncGrantEvents)));
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
    public void Peer_sessions_come_from_federation_and_a_host_without_it_has_none_online()
    {
        // §8.2 "a federation session to it came online": the scheduler asks this on every tick.
        var services = HostServices();
        services.AddDataSyncServiceComponents();
        using var provider = services.BuildServiceProvider(Strict);
        var sessions = provider.GetRequiredService<IDataSyncPeerSessions>();
        Assert.IsInstanceOfType<FederationDataSyncPeerSessions>(sessions);
        Assert.IsFalse(sessions.IsOnline("node-nas"));
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
