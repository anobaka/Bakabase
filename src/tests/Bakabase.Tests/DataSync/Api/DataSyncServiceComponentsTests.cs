using System.Net;
using System.Text.Json;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.DataSync;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// What the Service host registers for data sync (spec §2.11, H-startup): <see cref="DataSyncController"/> takes the
/// facade in its constructor, so without a registration every <c>/data-sync</c> request would fail with a 500.
/// </summary>
[TestClass]
public class DataSyncServiceComponentsTests
{
    private static readonly ServiceProviderOptions Strict = new() {ValidateScopes = true, ValidateOnBuild = true};

    [TestMethod]
    public void Placeholders_fill_the_gaps_until_the_runtime_registers_its_own()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRemoteAccessService, FakeRemoteAccessService>();
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
        var services = new ServiceCollection();
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
