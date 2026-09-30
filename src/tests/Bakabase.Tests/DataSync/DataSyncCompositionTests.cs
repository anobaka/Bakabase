using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Service.Components.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The composed data sync (§14.3 steps 4–5), with scopes validated: every seam the runtime and D's feed controller
/// resolve is bound to the persistence layer's one implementation, no singleton captured a scoped service, and the
/// runtime registers once however often it is added.
/// </summary>
[TestClass]
public class DataSyncCompositionTests
{
    [TestMethod]
    public async Task Every_runtime_seam_is_bound_to_the_persistence_layer_and_no_singleton_holds_a_scope()
    {
        IServiceCollection? registered = null;
        var sp = await TestServiceBuilder.BuildServiceProvider(TestServiceBuilder.NewTestDirectory(), services =>
        {
            services.AddDataSyncRuntime();
            services.AddDataSyncServiceComponents();
            registered = services;
        }, new ServiceProviderOptions { ValidateScopes = true });
        using var scope = sp.CreateScope();
        var s = scope.ServiceProvider;

        // Every singleton of the runtime, the runner and the feed, built as the host builds them.
        foreach (var singleton in new[]
                 {
                     typeof(DataSyncScheduler), typeof(DataSyncFetcher), typeof(DataSyncLinkService),
                     typeof(DataSyncTaskLauncher), typeof(DataSyncApplyTask), typeof(DataSyncGrantEventsHandler),
                     typeof(DataSyncNotifier), typeof(DataSyncHubPublisher), typeof(DataSyncRetention),
                     typeof(DataSyncUndoPlanner), typeof(DataSyncRefreshCoordinator),
                 })
        {
            Assert.IsNotNull(s.GetRequiredService(singleton), singleton.Name);
        }

        // D's DataSyncNodeController needs the feed source.
        Assert.AreSame(s.GetRequiredService<DataSyncFeedSource>(), s.GetRequiredService<IDataSyncFeedSource>());

        // One gate for the facade, the runner and the feed.
        Assert.AreSame(s.GetRequiredService<DataSyncGate>(), s.GetRequiredService<IDataSyncGateEntry>());

        // One attempt registry for the launcher and the runner.
        Assert.AreSame(s.GetRequiredService<DataSyncTaskRegistry>(), s.GetRequiredService<IDataSyncTaskRegistry>());

        Assert.AreSame(s.GetRequiredService<DataSyncApplyRunner>(), s.GetRequiredService<IDataSyncApplyRunner>());
        Assert.IsInstanceOfType<DataSyncService>(s.GetRequiredService<IDataSyncService>());
        Assert.IsInstanceOfType<DataSyncGrantEventsHandler>(s.GetRequiredService<IDataSyncGrantEvents>());
        Assert.AreEqual(1, registered!.Count(d => d.ServiceType == typeof(IDataSyncGrantEvents)));
        Assert.AreEqual(1, registered!.Count(d => d.ServiceType == typeof(IHostedService) &&
                                                  d.ImplementationFactory?.GetType().GenericTypeArguments.Last() ==
                                                  typeof(DataSyncScheduler)), "the scheduler is hosted once");
        var fetchTask = s.GetServices<IPredefinedBTaskBuilder>().OfType<DataSyncFetchTask>().Single();
        Assert.AreEqual(DataSyncRuntimeState.FetchInterval, fetchTask.GetInterval());
        CollectionAssert.AreEquivalent(new[] { DataSyncTaskIds.Fetch }, fetchTask.ConflictKeys!.ToArray());

        // The runner's apply events feed the notifier and the hub through the runtime's own observer (§8.10.2).
        var events = s.GetRequiredService<DataSyncRuntimeEvents>();
        Assert.AreSame(events, s.GetRequiredService<IDataSyncRuntimeObserver>());
        Assert.AreSame(events, s.GetServices<IDataSyncApplyListener>().Single());
    }
}
