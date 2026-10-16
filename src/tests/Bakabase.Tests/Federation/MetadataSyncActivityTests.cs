using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Service.Components.DataSync;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class MetadataSyncActivityTests
{
    private string _root = null!;
    private MetadataSyncActivityStore _store = null!;
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-sync-activity-" + Guid.NewGuid().ToString("N"));
        _store = new(new DataDirectory(_root));
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task History_pages_filter_source_and_do_not_shift_when_a_new_run_arrives()
    {
        var first = await _store.BeginAsync("a", "A", "epoch-a", "Snapshot", default);
        await _store.FinishAsync(first, "Completed", "Completed", 100000, 100000, null, null, default);
        await _store.BeginAsync("b", "B", "epoch-b", "Snapshot", default);
        var second = await _store.BeginAsync("a", "A", "epoch-a", "Changes", default);
        var page = await _store.ReadAsync("a", null, 1, default);
        Assert.AreEqual(second, page.Entries.Single().Id);
        Assert.AreEqual(second, page.NextBeforeId);
        await _store.BeginAsync("a", "A", "epoch-a", "Changes", default);
        page = await _store.ReadAsync("a", page.NextBeforeId, 1, default);
        Assert.AreEqual(first, page.Entries.Single().Id);
        Assert.IsNull(page.NextBeforeId);
        Assert.AreEqual(100000L, page.Entries.Single().ProcessedCount);
    }

    [TestMethod]
    public async Task Restart_marks_running_attempt_interrupted_without_claiming_a_completed_sync()
    {
        var id = await _store.BeginAsync("source", "Source", "epoch", "Snapshot", default);
        Assert.AreEqual("Running", (await _store.ReadAsync(null, null, 30, default)).Entries.Single().State);
        var restarted = new MetadataSyncActivityStore(new DataDirectory(_root));
        var row = (await restarted.ReadAsync(null, null, 30, default)).Entries.Single();
        Assert.AreEqual(id, row.Id);
        Assert.AreEqual("Interrupted", row.State);
        Assert.AreEqual("Snapshot", row.Phase);
    }

    [TestMethod]
    public async Task History_bounds_payload_rows_and_age_before_returning_a_page()
    {
        await _store.BeginAsync("source", new string('a', 1000), "epoch", "Snapshot", default);
        // These fixture-only connections must release their file handles on disposal,
        // like the store's connections, so Windows can delete the temporary database.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "metadata-replicas", "sync-activity.db"), Pooling = false
        }.ToString();
        await using (var db = new SqliteConnection(connectionString))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE counts(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM counts WHERE n<5100)
                INSERT INTO activity(server_id,name,epoch,started,updated,state,phase,processed,resources)
                SELECT 'source','Source','epoch',0,strftime('%s','now')*1000,'Completed','Completed',n,n FROM counts;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var id = await _store.BeginAsync("last", "Last", "epoch", "Changes", default);
        await _store.FinishAsync(id, "Failed", "Changes", 0, 0, new string('c', 200), new string('e', 10000), default);
        var page = await _store.ReadAsync(null, null, 1000, default);
        Assert.AreEqual(100, page.Entries.Count);
        Assert.AreEqual(128, page.Entries[0].ErrorCode!.Length);
        Assert.AreEqual(2048, page.Entries[0].Error!.Length);
        await using var check = new SqliteConnection(connectionString);
        await check.OpenAsync();
        await using var count = check.CreateCommand(); count.CommandText = "SELECT count(*) FROM activity";
        Assert.AreEqual((long)MetadataSyncActivityStore.MaxEntries, (long)(await count.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public void Completed_data_page_is_not_caught_up_until_catalog_and_resource_sequences_match()
    {
        var head = new ReplicaCheckpoint("g", 500, "start", false, 12);
        var checkpoint = new ReplicaCheckpoint("g", 500, null, true, 11);
        Assert.IsFalse(MetadataReplicaCoordinator.IsCaughtUp(checkpoint, head));
        Assert.IsFalse(MetadataReplicaCoordinator.IsCaughtUp(checkpoint with {CatalogRevision = 12, Cursor = "more"}, head));
        Assert.IsFalse(MetadataReplicaCoordinator.IsCaughtUp(checkpoint with {CatalogRevision = 12, Sequence = 499}, head));
        Assert.IsFalse(MetadataReplicaCoordinator.IsCaughtUp(checkpoint with {CatalogRevision = 12, Generation = "old"}, head));
        Assert.IsTrue(MetadataReplicaCoordinator.IsCaughtUp(checkpoint with {CatalogRevision = 12}, head));
    }

    [TestMethod]
    public void Metadata_does_not_report_success_for_offline_cache_identity_changes_or_pending_writes()
    {
        var id = new ReplicaIdentity("owner", "epoch");
        var source = new ManagedMetadataStatus(id, "Owner", new(new("g", 20, null, true), "g", 10, 20), new());
        Assert.AreEqual("Current", SyncActivityService.MetadataState(source));
        Assert.AreEqual("Offline", SyncActivityService.MetadataState(source with {Sync = new(IsOffline: true)}));
        Assert.AreEqual("Syncing", SyncActivityService.MetadataState(source with {Sync = new(RefreshPending: true)}));
        Assert.AreEqual("Syncing", SyncActivityService.MetadataState(source with {Sync = new(CatchupPending: true)}));
        Assert.AreEqual("Denied", SyncActivityService.MetadataState(source with {Denied = true}));
        Assert.AreEqual("IdentityChanged", SyncActivityService.MetadataState(source with {Sync = new(ErrorCode: "WrongServer")}));
    }
    [TestMethod]
    public void Definition_busy_is_syncing_and_receiver_decisions_are_not_a_success_report()
    {
        var link = new DataSyncLinkView(1, "peer", "Peer", null, DataSyncLinkMode.Follow, DataSyncLinkMode.Follow,
            DataSyncLinkState.Active, null, null, DataSyncLinkInitiator.ThisDevice, ["customProperty"], null,
            DateTime.UtcNow, "Busy", null, 0, 0, null, null, false, false, null, null, null, 0, 0, 0);
        Assert.AreEqual("Syncing", SyncActivityService.DefinitionState(link, false));
        Assert.AreEqual("NeedsReview", SyncActivityService.DefinitionState(link with {OpenItems = 1}, false));
        Assert.AreEqual("Offline", SyncActivityService.DefinitionState(link with {LastErrorCode = "Unreachable"}, false));
        Assert.AreEqual("Paused", SyncActivityService.ReaderState(new("peer", "Peer", DateTime.UtcNow, "twoWay", "paused:ByUser", true)));
        Assert.AreEqual("NeedsReview", SyncActivityService.ReaderState(new("peer", "Peer", DateTime.UtcNow, "twoWay", "needsYou:5", true)));
        Assert.AreEqual("ReportedCurrent", SyncActivityService.ReaderState(new("peer", "Peer", DateTime.UtcNow, "twoWay", "ok", true)));
    }
    private sealed class DataDirectory(string path) : IFederationDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }
}
