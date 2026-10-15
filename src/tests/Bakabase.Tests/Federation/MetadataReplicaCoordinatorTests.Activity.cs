using System.Net.Http;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.Federation;

public sealed partial class MetadataReplicaCoordinatorTests
{
    [TestMethod]
    public async Task History_records_committed_runs_skips_unchanged_polls_and_deduplicates_offline_failures()
    {
        var activity = new MetadataSyncActivityStore(new DataDirectory(_root));
        _coordinator = new(_transport, _store, _registry, activity);
        await SeedAsync();
        var runs = await activity.ReadAsync(Owner.ServerId, null, 30, default);
        Assert.AreEqual("Completed", runs.Entries.Single().State);
        Assert.AreEqual(1L, runs.Entries.Single().ResourceCount);
        await SeedAsync();
        Assert.AreEqual(1, (await activity.ReadAsync(null, null, 30, default)).Entries.Count);
        _wire.Handle = _ => throw new HttpRequestException("device offline");
        await SeedAsync(); await SeedAsync();
        runs = await activity.ReadAsync(null, null, 30, default);
        Assert.AreEqual(2, runs.Entries.Count);
        Assert.AreEqual("Failed", runs.Entries[0].State);
        Assert.IsTrue((await _store.GetSyncStateAsync(Owner)).IsOffline);
        Assert.AreEqual(1L, (await _store.GetStatusAsync(Owner)).ResourceCount);
    }

    [TestMethod]
    public async Task Paused_after_a_committed_page_preserves_checkpoint_and_history_is_not_success()
    {
        var activity = new MetadataSyncActivityStore(new DataDirectory(_root));
        _coordinator = new(_transport, _store, _registry, activity);
        _wire.Handle = async call =>
        {
            if (call.Path.EndsWith("/head")) return Json(new MetadataSourceManifest(Owner, Start));
            var request = JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!;
            await _store.UpdateSyncStateAsync(Owner, state => state with {Paused = true});
            return Json(new ReplicaBatch(request.Checkpoint, request.Checkpoint with {Cursor = "next"}, [Resource(1)], [], [], []));
        };
        await SeedAsync();
        var run = (await activity.ReadAsync(null, null, 30, default)).Entries.Single();
        Assert.AreEqual("Paused", run.State);
        Assert.AreEqual(1L, run.ProcessedCount);
        Assert.IsNull((await _store.GetSyncStateAsync(Owner)).LastSyncedAtUtc);
        Assert.AreEqual("next", (await _store.GetStatusAsync(Owner)).Checkpoint!.Cursor);
    }
}
