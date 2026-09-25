using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The actor guard (spec §5.6, gate fixes B1(a)–(c)): rotation on every trigger in its own transaction, the retired
/// actors' recorded counters, evidence that never rotates twice for one actor, the scopes of a restore (one link
/// suspected, every link detected, corroboration), the reader-ahead record, startup verification and actor.json.
/// </summary>
[TestClass]
public class ActorGuardTests
{
    private const string LostActor = "ffffffffffffffff";

    #region CheckAsync: identity and the watermark

    [TestMethod]
    public async Task The_first_check_creates_the_state_row_and_the_watermark_and_verifies_without_active_links()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(verified: false);
        Assert.IsFalse(f.Guard.IsVerified);

        Assert.IsNull(await f.CheckAsync());

        var state = await f.StateAsync();
        Assert.AreEqual(1, state.ActorGeneration);
        Assert.AreEqual(f.Identity.Device.NodeId, state.NodeId);
        Assert.AreEqual(DataSyncActorId.Derive(state.NodeId, state.LibraryEpoch, state.ActorSalt).Value, state.ActorId);
        Assert.AreEqual(0, state.ActorCounter);
        Assert.AreEqual(DataSyncActorWatermark.Of(state), f.Watermark.Read().Watermark);
        Assert.IsTrue(f.Guard.IsVerified, "with no Active link there is no head to wait for (§5.6)");
    }

    [TestMethod]
    public async Task An_identity_change_rotates_without_pausing()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        await f.LinkAsync("peer-1");
        var before = await f.StateAsync();

        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        Assert.IsNull(await f.CheckAsync(), "a deliberate reset pauses nothing");

        var after = await f.StateAsync();
        Assert.AreEqual(f.Identity.Device.NodeId, after.NodeId);
        Assert.AreEqual(f.Identity.Device.LibraryEpoch, after.LibraryEpoch);
        Assert.AreEqual(2, after.ActorGeneration);
        Assert.AreNotEqual(before.ActorSalt, after.ActorSalt);
        Assert.AreEqual(DataSyncActorId.Derive(after.NodeId, after.LibraryEpoch, after.ActorSalt).Value, after.ActorId);
        Assert.AreEqual(0, after.ActorCounter);
        Assert.AreEqual(1, Retired(after)[before.ActorId], "the retired actor keeps its recorded counter");
        Assert.IsNull(after.RestoreReason);
        Assert.AreEqual(DataSyncLinkState.Active, (await f.LinkRowAsync("peer-1")).State);
        Assert.AreEqual(DataSyncActorWatermark.Of(after), f.Watermark.Read().Watermark, "written before anything continues");
    }

    [TestMethod]
    public Task A_watermark_with_a_newer_generation_is_a_restore() =>
        AssertWatermarkRestoreAsync(w => w with {Generation = w.Generation + 1, ActorId = LostActor, Counter = 9});

    [TestMethod]
    public Task A_watermark_with_a_larger_counter_of_the_same_actor_is_a_restore() =>
        AssertWatermarkRestoreAsync(w => w with {Counter = w.Counter + 4});

    [TestMethod]
    public Task A_watermark_of_another_database_instance_is_a_restore() =>
        AssertWatermarkRestoreAsync(w => w with {DbInstanceId = Guid.NewGuid().ToString("N")});

    private static async Task AssertWatermarkRestoreAsync(Func<DataSyncActorWatermark, DataSyncActorWatermark> ahead)
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        await f.LinkAsync("peer-1");
        await f.LinkAsync("peer-2", DataSyncLinkState.Paused, DataSyncPauseReason.ByUser);
        await f.LinkAsync("peer-3", DataSyncLinkState.AwaitingAccess);
        var before = await f.StateAsync();
        var file = ahead(DataSyncActorWatermark.Of(before));
        await f.Watermark.WriteAsync(file, default);
        var detections = new List<DataSyncRestoreDetection>();
        f.Guard.RestoreDetected += (_, d) => detections.Add(d);

        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, await f.CheckAsync());

        var after = await f.StateAsync();
        Assert.AreNotEqual(before.ActorId, after.ActorId);
        Assert.AreEqual(Math.Max(before.ActorGeneration, file.Generation) + 1, after.ActorGeneration,
            "past every generation the file has seen");
        Assert.AreEqual(0, after.ActorCounter);
        var retired = Retired(after);
        Assert.AreEqual(file.ActorId == before.ActorId ? Math.Max(file.Counter, 1) : 1, retired[before.ActorId]);
        if (file.ActorId != before.ActorId)
            Assert.AreEqual(file.Counter, retired[file.ActorId], "the actor lost with the restore is recorded too");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, after.RestoreReason);
        Assert.IsNull(after.RestoreLinkId);
        Assert.IsNotNull(after.RestoreDetectedAtUtc);
        Assert.AreEqual("evidence=watermark", after.RestoreDetail);
        var evidence = DataSyncRestoreEvidence.Read(after.RestoreEvidenceJson).Single();
        Assert.AreEqual((DataSyncRestoreEvidence.Watermark, file.ActorId, (long?) file.Counter),
            (evidence.Source, evidence.ActorId, evidence.Counter));

        var active = await f.LinkRowAsync("peer-1");
        Assert.AreEqual((DataSyncLinkState.Paused, DataSyncPauseReason.LocalRestoreDetected),
            (active.State, active.PausedReason));
        Assert.AreEqual(DataSyncPauseReason.ByUser, (await f.LinkRowAsync("peer-2")).PausedReason,
            "a link the person paused keeps its reason");
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, (await f.LinkRowAsync("peer-3")).State);
        Assert.AreEqual(1, detections.Count);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, detections[0].Reason);
        Assert.AreEqual(DataSyncActorWatermark.Of(after), f.Watermark.Read().Watermark);

        Assert.IsNull(await f.CheckAsync(), "the same restore is detected once");
        Assert.AreEqual(after.ActorId, (await f.StateAsync()).ActorId);
    }

    [TestMethod]
    public async Task A_watermark_behind_the_row_missing_or_unreadable_is_no_signal_and_is_written_again()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        f.Kind.Add("2", "Mood");
        await f.RefreshAsync();
        var state = await f.StateAsync();

        // A crash between a commit and the write leaves the file behind: not a signal (§5.6).
        await f.Watermark.WriteAsync(DataSyncActorWatermark.Of(state) with {Counter = 1}, default);
        Assert.IsNull(await f.CheckAsync());
        Assert.AreEqual(DataSyncActorWatermark.Of(state), f.Watermark.Read().Watermark);

        File.Delete(f.Watermark.FilePath);
        Assert.IsNull(await f.CheckAsync());
        Assert.AreEqual(DataSyncActorWatermark.Of(state), f.Watermark.Read().Watermark, "a missing file is written fresh");

        await File.WriteAllTextAsync(f.Watermark.FilePath, "{not json");
        Assert.IsNull(await f.CheckAsync());
        Assert.AreEqual(DataSyncActorWatermark.Of(state), f.Watermark.Read().Watermark);
        Assert.AreEqual(state.ActorId, (await f.StateAsync()).ActorId);
    }

    [TestMethod]
    public async Task A_check_with_nothing_to_do_never_waits_for_the_database_writer()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();

        // Another writer holds SQLite's write lock (an apply's BEGIN IMMEDIATE, say).
        await using var transaction = await f.Db.Database.BeginTransactionAsync();
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE DataSyncLocalStates SET NewDefinitionsStayLocal = 0");

        var check = f.CheckAsync();
        Assert.AreSame(check, await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(10))),
            "a head's check reads; only a rotation writes");
        Assert.IsNull(await check);
        await transaction.RollbackAsync();
    }

    [TestMethod]
    public async Task A_restored_database_never_derives_an_actor_it_used_before()
    {
        // (a) of RefreshTests' actor row: the whole data folder (row and actor.json) goes back to generation 1, and a
        // later identity reset rotates to generation 2 again — with another salt, so another actor.
        var f = await DataSyncRefreshFixture.CreateAsync();
        var originalDevice = f.Identity.Device;
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        var backupRow = await f.StateAsync();
        var backupFile = f.Watermark.Read().Watermark!;

        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        await f.CheckAsync();
        var generation2 = await f.StateAsync();
        Assert.AreEqual(2, generation2.ActorGeneration);

        await RestoreRowAsync(f, backupRow);
        await f.Watermark.WriteAsync(backupFile, default);
        f.Identity.Device = originalDevice;
        Assert.IsNull(await f.CheckAsync(), "a whole-folder restore leaves no local trace (the residual case)");

        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        await f.CheckAsync();
        var again = await f.StateAsync();
        Assert.AreEqual(2, again.ActorGeneration);
        Assert.AreNotEqual(generation2.ActorSalt, again.ActorSalt);
        Assert.AreNotEqual(generation2.ActorId, again.ActorId, "the salts differ, so the actors differ");
        Assert.AreNotEqual(backupRow.ActorId, again.ActorId);
    }

    [TestMethod]
    public async Task A_database_restored_alone_is_detected_and_rotates_past_the_watermark()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        var backupRow = await f.StateAsync();
        f.Kind.Definitions["1"].Name = "Genres";
        await f.RefreshAsync();
        var newer = await f.StateAsync();

        await RestoreRowAsync(f, backupRow);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, await f.CheckAsync());

        var after = await f.StateAsync();
        Assert.AreNotEqual(backupRow.ActorId, after.ActorId);
        Assert.AreEqual(2, after.ActorGeneration);
        Assert.AreEqual(newer.ActorCounter, Retired(after)[backupRow.ActorId],
            "the counters issued and lost are recorded from actor.json");
    }

    [TestMethod]
    public async Task A_database_that_lost_its_state_behind_the_watermark_is_a_restore()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        await f.LinkAsync("peer-1");
        var lost = await f.StateAsync();
        await f.Db.DataSyncLocalStates.ExecuteDeleteAsync();

        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, await f.CheckAsync());

        var state = await f.StateAsync();
        Assert.AreNotEqual(lost.ActorId, state.ActorId);
        Assert.AreEqual(lost.ActorGeneration + 1, state.ActorGeneration);
        Assert.AreEqual(lost.ActorCounter, Retired(state)[lost.ActorId]);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, (await f.LinkRowAsync("peer-1")).PausedReason);
    }

    #endregion

    #region Peer evidence

    [TestMethod]
    public async Task Peer_evidence_about_the_current_actor_rotates_and_pauses_that_link_only()
    {
        var f = await WithLinksAsync(3, "peer-1", "peer-2");
        var before = await f.StateAsync();
        var detections = new List<DataSyncRestoreDetection>();
        f.Guard.RestoreDetected += (_, d) => detections.Add(d);

        await f.Guard.ReportPeerEvidenceAsync("peer-1", before.ActorId, 7, default);

        var after = await f.StateAsync();
        Assert.AreNotEqual(before.ActorId, after.ActorId);
        Assert.AreEqual(7, Retired(after)[before.ActorId], "max(ActorCounter, evidence)");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreSuspected, after.RestoreReason);
        var link1 = await f.LinkRowAsync("peer-1");
        Assert.AreEqual(link1.Id, after.RestoreLinkId);
        Assert.AreEqual((DataSyncLinkState.Paused, DataSyncPauseReason.LocalRestoreSuspected),
            (link1.State, link1.PausedReason));
        Assert.AreEqual(DataSyncLinkState.Active, (await f.LinkRowAsync("peer-2")).State,
            "one peer alone never pauses every link (product must-fix 19)");
        var evidence = DataSyncRestoreEvidence.Read(after.RestoreEvidenceJson).Single();
        Assert.AreEqual((DataSyncRestoreEvidence.Peer, "peer-1", "PEER-1", before.ActorId, (long?) 7),
            (evidence.Source, evidence.NodeId, evidence.Name, evidence.ActorId, evidence.Counter));
        Assert.AreEqual((DataSyncPauseReason.LocalRestoreSuspected, (int?) link1.Id),
            (detections.Single().Reason, detections.Single().LinkId));
        Assert.IsTrue(f.Guard.IsVerified);
        Assert.AreEqual(DataSyncActorWatermark.Of(after), f.Watermark.Read().Watermark);
    }

    [TestMethod]
    public async Task Evidence_at_or_below_what_is_recorded_does_nothing()
    {
        var f = await WithLinksAsync(3, "peer-1");
        var before = await f.StateAsync();

        await f.Guard.ReportPeerEvidenceAsync("peer-1", before.ActorId, 3, default);
        await f.Guard.ReportPeerEvidenceAsync("peer-1", "0123456789abcdef", 99, default);

        var state = await f.StateAsync();
        Assert.AreEqual((before.ActorId, before.ActorGeneration), (state.ActorId, state.ActorGeneration));
        Assert.IsNull(state.RestoreEvidenceJson, "another device's actor is never evidence");

        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        await f.CheckAsync();
        await f.Guard.ReportPeerEvidenceAsync("peer-1", before.ActorId, 2, default);
        var rotated = await f.StateAsync();
        Assert.AreEqual(3, Retired(rotated)[before.ActorId]);
        Assert.IsNull(rotated.RestoreEvidenceJson);
        Assert.IsTrue(f.Guard.IsVerified);
    }

    [TestMethod]
    public async Task Evidence_about_a_retired_actor_only_raises_its_recorded_counter()
    {
        var f = await WithLinksAsync(3, "peer-1");
        var original = await f.StateAsync();
        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        await f.CheckAsync();
        var current = await f.StateAsync();

        await f.Guard.ReportPeerEvidenceAsync("peer-1", original.ActorId, 10, default);

        var after = await f.StateAsync();
        Assert.AreEqual((current.ActorId, current.ActorGeneration), (after.ActorId, after.ActorGeneration),
            "never rotates (gate fix B1(a))");
        Assert.AreEqual(10, Retired(after)[original.ActorId]);
        Assert.IsNull(after.RestoreReason, "never pauses: the counters it lost are history");
        Assert.AreEqual(DataSyncLinkState.Active, (await f.LinkRowAsync("peer-1")).State);
        Assert.AreEqual(DataSyncRestoreEvidence.Peer, DataSyncRestoreEvidence.Read(after.RestoreEvidenceJson).Single().Source);
    }

    [TestMethod]
    public async Task A_second_peer_corroborates_a_suspected_restore_without_rotating_again()
    {
        var f = await WithLinksAsync(3, "peer-1", "peer-2", "peer-3");
        var original = await f.StateAsync();
        var detections = new List<DataSyncRestoreDetection>();
        f.Guard.RestoreDetected += (_, d) => detections.Add(d);

        await f.Guard.ReportPeerEvidenceAsync("peer-1", original.ActorId, 7, default);
        var suspected = await f.StateAsync();
        await f.Guard.ReportPeerEvidenceAsync("peer-2", original.ActorId, 9, default);

        var detected = await f.StateAsync();
        Assert.AreEqual((suspected.ActorId, suspected.ActorGeneration), (detected.ActorId, detected.ActorGeneration));
        Assert.AreEqual(9, Retired(detected)[original.ActorId]);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, detected.RestoreReason);
        Assert.IsNull(detected.RestoreLinkId);
        foreach (var peer in new[] {"peer-1", "peer-2", "peer-3"})
            Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, (await f.LinkRowAsync(peer)).PausedReason, peer);
        CollectionAssert.AreEqual(new[] {DataSyncPauseReason.LocalRestoreSuspected, DataSyncPauseReason.LocalRestoreDetected},
            detections.Select(d => d.Reason).ToArray());
        Assert.IsTrue(detections[1].Escalated);
    }

    [TestMethod]
    public async Task Evidence_never_rotates_twice_for_one_actor()
    {
        var f = await WithLinksAsync(3, "peer-1", "peer-2");
        var original = await f.StateAsync();

        await Task.WhenAll(
            f.Guard.ReportPeerEvidenceAsync("peer-1", original.ActorId, 5, default),
            f.Guard.ReportPeerEvidenceAsync("peer-2", original.ActorId, 6, default));

        var after = await f.StateAsync();
        Assert.AreEqual(2, after.ActorGeneration, "one rotation");
        Assert.AreEqual(6, Retired(after)[original.ActorId]);
        Assert.IsFalse(f.Guard.HasPendingEvidence);
    }

    [TestMethod]
    public async Task Evidence_that_could_not_be_handled_stays_pending_until_CheckAsync()
    {
        var identity = new SwitchableIdentity(TestDataSyncDeviceIdentity.NewDevice());
        var f = await DataSyncRefreshFixture.CreateAsync(configure: s => s.AddSingleton<IDataSyncDeviceIdentity>(identity));
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        await f.LinkAsync("peer-1");
        var original = await f.StateAsync();

        identity.Fail = true;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            f.Guard.ReportPeerEvidenceAsync("peer-1", original.ActorId, 5, default));

        Assert.IsTrue(f.Guard.HasPendingEvidence);
        Assert.IsFalse(f.Guard.IsVerified, "no counter may be issued under an actor about to retire");
        f.Kind.Definitions["1"].Name = "Genres";
        Assert.IsTrue((await f.RefreshAsync()).Skipped);

        identity.Fail = false;
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreSuspected, await f.CheckAsync());
        Assert.IsFalse(f.Guard.HasPendingEvidence);
        Assert.IsTrue(f.Guard.IsVerified);
        Assert.AreNotEqual(original.ActorId, (await f.StateAsync()).ActorId);
    }

    #endregion

    #region Reader ahead

    [TestMethod]
    public async Task A_reader_ahead_is_a_restore_reported_once_until_it_reads_in_step()
    {
        var f = await WithLinksAsync(1, "peer-1");
        await f.Store.TouchReaderAsync(new DataSyncReader("reader-1", "grant-1", "NAS"),
            new DataSyncFeedQuery("follow", new Dictionary<string, long>(), null, "ok"), 1, f.Now, default);
        var original = await f.StateAsync();

        await f.Guard.ReportReaderAheadAsync("reader-1", default);

        var detected = await f.StateAsync();
        Assert.AreNotEqual(original.ActorId, detected.ActorId);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, detected.RestoreReason);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, (await f.LinkRowAsync("peer-1")).PausedReason);
        var entry = DataSyncRestoreEvidence.Read(detected.RestoreEvidenceJson).Single();
        Assert.AreEqual((DataSyncRestoreEvidence.Reader, "reader-1", "NAS"), (entry.Source, entry.NodeId, entry.Name));
        Assert.IsTrue(await f.Guard.IsReaderAheadRecordedAsync("reader-1", default));

        await f.Guard.ReportReaderAheadAsync("reader-1", default);
        Assert.AreEqual(detected.ActorGeneration, (await f.StateAsync()).ActorGeneration,
            "a recorded reader is not reported again (gate fix B1(b))");

        Assert.IsTrue(await f.Guard.NoteReaderInStepAsync("reader-1", default));
        Assert.IsFalse(await f.Guard.IsReaderAheadRecordedAsync("reader-1", default));
        Assert.IsTrue(DataSyncRestoreEvidence.Read((await f.StateAsync()).RestoreEvidenceJson).Single().Settled);

        // The person chose; later the same reader reads ahead again: a new detection.
        var state = (await f.Store.GetLocalStateAsync(default))!;
        state.RestoreReason = null;
        state.RestoreDetectedAtUtc = null;
        await f.Store.SaveLocalStateAsync(state, default);
        await f.Guard.ReportReaderAheadAsync("reader-1", default);
        Assert.AreEqual(detected.ActorGeneration + 1, (await f.StateAsync()).ActorGeneration);
    }

    [TestMethod]
    public async Task A_reader_ahead_corroborates_a_suspected_restore()
    {
        var f = await WithLinksAsync(3, "peer-1", "peer-2");
        var original = await f.StateAsync();
        await f.Guard.ReportPeerEvidenceAsync("peer-1", original.ActorId, 7, default);
        var suspected = await f.StateAsync();

        await f.Guard.ReportReaderAheadAsync("peer-2", default);

        var detected = await f.StateAsync();
        Assert.AreEqual(suspected.ActorId, detected.ActorId, "the same restore: no second rotation");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, detected.RestoreReason);
        Assert.AreEqual("evidence=peer+reader", detected.RestoreDetail);
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, (await f.LinkRowAsync("peer-2")).PausedReason);
    }

    #endregion

    #region Verification

    [TestMethod]
    public async Task With_an_active_link_the_actor_is_verified_by_MarkVerified_or_after_two_minutes()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(verified: false);
        await f.CheckAsync();
        await f.LinkAsync("peer-1");
        var guard = new DataSyncActorGuard(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Watermark, f.Clock);

        using (var lease = await f.Gate.EnterAsync(null, default)) await guard.CheckAsync(lease, default);
        Assert.IsFalse(guard.IsVerified, "the head of every Active link's peer is awaited (§5.6)");
        f.Clock.Advance(TimeSpan.FromSeconds(119));
        Assert.IsFalse(guard.IsVerified);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(guard.IsVerified, "offline peers: two minutes");

        var marked = new DataSyncActorGuard(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Watermark, f.Clock);
        Assert.IsFalse(marked.IsVerified);
        marked.MarkVerified();
        Assert.IsTrue(marked.IsVerified);
    }

    [TestMethod]
    public async Task CheckAsync_needs_the_gate()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        var lease = await f.Gate.EnterAsync(null, default);
        lease.Dispose();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => f.Guard.CheckAsync(lease, default));
    }

    #endregion

    private static async Task<DataSyncRefreshFixture> WithLinksAsync(int counters, params string[] peers)
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        for (var i = 1; i <= counters; i++) f.Kind.Add(i.ToString(), $"Definition {i}");
        await f.RefreshAsync();
        foreach (var peer in peers) await f.LinkAsync(peer);
        Assert.AreEqual(counters, (await f.StateAsync()).ActorCounter);
        return f;
    }

    private static IReadOnlyDictionary<string, long> Retired(DataSyncLocalStateDbModel state) =>
        DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "RetiredActorsJson");

    /// <summary>Puts the state row back as a backup had it (a restored database).</summary>
    private static async Task RestoreRowAsync(DataSyncRefreshFixture f, DataSyncLocalStateDbModel backup)
    {
        f.Db.ChangeTracker.Clear();
        var row = await f.Db.DataSyncLocalStates.SingleAsync();
        f.Db.Entry(row).CurrentValues.SetValues(backup);
        await f.Db.SaveChangesAsync();
    }

    private sealed class SwitchableIdentity(DataSyncDevice device) : IDataSyncDeviceIdentity
    {
        public bool Fail { get; set; }

        public Task<DataSyncDevice> GetAsync(CancellationToken ct) =>
            Fail ? throw new InvalidOperationException("The federation identity is not available.") : Task.FromResult(device);
    }
}
