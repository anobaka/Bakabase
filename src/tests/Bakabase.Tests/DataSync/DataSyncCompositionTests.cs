using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The composed data sync (§14.3 steps 4–5): every seam the runtime and D's feed controller resolve is bound to the
/// persistence layer's one implementation, so the Service never falls back to "not available".
/// </summary>
[TestClass]
public class DataSyncCompositionTests
{
    [TestMethod]
    public async Task Every_runtime_seam_is_bound_to_the_persistence_layer()
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var s = scope.ServiceProvider;

        // D's DataSyncNodeController answers 501 only without a feed source.
        Assert.AreSame(s.GetRequiredService<DataSyncFeedSource>(), s.GetRequiredService<IDataSyncFeedSource>());

        // One gate for the facade, the runner and the feed.
        Assert.AreSame(s.GetRequiredService<DataSyncGate>(), s.GetRequiredService<IDataSyncGateEntry>());

        // One attempt registry for the launcher and the runner.
        Assert.AreSame(s.GetRequiredService<DataSyncTaskRegistry>(), s.GetRequiredService<IDataSyncTaskRegistry>());

        Assert.AreSame(s.GetRequiredService<DataSyncLocalStateReader>(), s.GetRequiredService<IDataSyncLocalStateReader>());
        Assert.AreSame(s.GetRequiredService<DataSyncUndoPlanner>(), s.GetRequiredService<IDataSyncUndoPreviewer>());
        Assert.AreSame(s.GetRequiredService<DataSyncRetention>(), s.GetRequiredService<IDataSyncRetention>());
        Assert.AreSame(s.GetRequiredService<DataSyncApplyRunner>(), s.GetRequiredService<IDataSyncApplyRunner>());
        Assert.IsInstanceOfType<DataSyncService>(s.GetRequiredService<IDataSyncService>());
        Assert.IsInstanceOfType<DataSyncGrantEventsHandler>(s.GetRequiredService<IDataSyncGrantEvents>());
    }
}
