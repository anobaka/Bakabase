using System.Diagnostics;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.DataSync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.TwoHost;

/// <summary>
/// Two hosts in one process over real SQLite (spec §13.7): each a TestKit provider with its own database and data
/// directory, reading the other's real feed through <see cref="InProcessPeerClient"/> (the real page bytes, parsed by
/// the wire reader), with a simulated clock and the pairing flow's grants and events in memory
/// (<see cref="TwoHostNetwork"/>). The runtime is driven as its own loops would drive it: the scheduler's tick, the
/// <c>DataSync</c> fetch task and the write tasks it enqueues.
/// </summary>
/// <remarks>
/// Every step of §13.7 runs in one flow, step 6 last: resetting B's link to A takes away the link the undo and the
/// restores pull through. Step 8 runs three times: the database alone rewound (B's own records), and the whole data
/// directory restored with 60 edits, detected once by A's read and once by B's startup verification — the restore
/// choices "this device wins", "take the others'" and a link-scoped "this device wins" after them.
/// </remarks>
[TestClass]
public class DataSyncTwoHostTests
{
    private const string Cp = DataSyncKindIds.CustomProperty;
    private const string Eg = DataSyncKindIds.ExtensionGroup;
    private const int BigTagCount = 10_000;
    private const string BigTagsName = "Big tags";

    private static readonly string[] SharedNames = ["Shared 1", "Shared 2", "Shared 3", "Shared 4", "Shared 5", "Shared 6"];
    private static readonly string[] Colours = ["Red", "Green", "Blue"];

    private TwoHostClock _clock = null!;
    private TwoHostNetwork _network = null!;
    private TwoHostNode _a = null!;
    private TwoHostNode _b = null!;

    [TestMethod]
    [Timeout(180_000)]
    public async Task Two_hosts_link_converge_decide_undo_and_survive_restores()
    {
        var total = Stopwatch.StartNew();
        _clock = new TwoHostClock(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)));
        _network = new TwoHostNetwork(_clock);
        _a = await TwoHostNode.StartAsync(_network, Device("PC-A"));
        _b = await TwoHostNode.StartAsync(_network, Device("PC-B"));

        await StepAsync("1 setup", Step1_SetupAsync);
        await StepAsync("2 review", Step2_B_initiates_two_way_and_reviews_the_first_syncAsync);
        await StepAsync("3 first pull", Step3_A_first_pull_finds_everything_by_keyAsync);
        await StepAsync("4 convergence", Step4_convergenceAsync);

        // Backups of B for step 8, taken while both sides agree: B's database alone, and its whole data directory.
        _scratch = Path.Combine(Path.GetTempPath(), "BakabaseTests_twoHost_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_scratch);
        _bDatabaseBackup = await _b.CopyDatabaseAsync(Path.Combine(_scratch, "b-database.db"));
        _bDirectoryBackup = await _b.CopyDataDirectoryAsync(Path.Combine(_scratch, "b-directory"));
        _bBackupState = await _b.LocalStateAsync();

        await StepAsync("5 concurrent rename", Step5_a_concurrent_rename_is_decided_onceAsync);
        await StepAsync("7 undo", Step7_undo_of_the_first_sync_reaches_nobodyAsync);
        await StepAsync("8a database rewound", Step8a_a_database_rewound_alone_is_detected_by_its_own_recordsAsync);
        await StepAsync("8b whole directory, reader", Step8b_a_whole_directory_restore_is_detected_by_the_readerAsync);
        await StepAsync("8c whole directory, startup", Step8c_a_whole_directory_restore_is_detected_at_startupAsync);
        // Step 6 last: resetting B's link takes away the link every other step pulls through.
        await StepAsync("6 in-use child deletion", Step6_an_in_use_child_deletion_is_held_until_the_link_is_resetAsync);

        Console.WriteLine($"Two-host test: {total.Elapsed.TotalSeconds:F1} s");
        try
        {
            Directory.Delete(_scratch, true);
        }
        catch (IOException)
        {
        }
    }

    private string _scratch = null!;
    private string _bDatabaseBackup = null!;
    private string _bDirectoryBackup = null!;
    private DataSyncLocalStateDbModel _bBackupState = null!;

    private static async Task StepAsync(string name, Func<Task> step)
    {
        var watch = Stopwatch.StartNew();
        await step();
        Console.WriteLine($"Step {name}: {watch.Elapsed.TotalSeconds:F2} s");
    }

    // ---- 1. Setup ------------------------------------------------------------------------------------------------

    private async Task Step1_SetupAsync()
    {
        await _a.InScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<ICustomPropertyService>();
            var dtos = new List<CustomPropertyAddOrPutDto>();
            for (var i = 0; i < SharedNames.Length; i++) dtos.Add(Choices(SharedNames[i], i < 3, Colours));
            dtos.Add(BigTags());
            for (var i = 1; dtos.Count < 100; i++)
            {
                dtos.Add(i % 3 == 0
                    ? Choices($"Prop {i:D2}", true, "One", "Two")
                    : new CustomPropertyAddOrPutDto
                    {
                        Name = $"Prop {i:D2}",
                        Type = i % 3 == 1 ? PropertyType.SingleLineText : PropertyType.Number,
                    });
            }

            await properties.AddRange(dtos.ToArray());
            var groups = sp.GetRequiredService<IExtensionGroupService>();
            await groups.AddRange(Enumerable.Range(1, 8)
                .Select(i => new ExtensionGroupAddInputModel($"Group {i}", [$".g{i}a", $".g{i}b"])).ToArray());
        });

        await _b.InScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<ICustomPropertyService>();
            await properties.AddRange(SharedNames.Select((name, i) =>
                Choices(name, i < 3, [.. Colours, $"B extra {i + 1}"])).ToArray());
        });

        Assert.AreEqual(100, (await PropertiesAsync(_a)).Count);
        Assert.AreEqual(BigTagCount, TagCount((await PropertiesAsync(_a)).Single(p => p.Name == BigTagsName)));
        Assert.AreEqual(6, (await PropertiesAsync(_b)).Count);
    }

    // ---- 2. B initiates two-way ----------------------------------------------------------------------------------

    private async Task Step2_B_initiates_two_way_and_reviews_the_first_syncAsync()
    {
        // B asks A to keep in step both ways: no access yet, so a request goes out with a reciprocal offer (§7.2.4).
        var created = await _b.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(_a.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem, created.Problem?.Code.ToString());
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, created.Link!.State);
        Assert.IsNotNull(created.RequestId);

        // A approves, reading B back (§7.2.4): A's link waits for B's first review.
        var request = (await _a.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming);
        var approved = await _a.CallAsync(s => s.ApproveRequestAsync(request.RequestId,
            new DataSyncApproveInput(true, null), default));
        var approvedAt = _clock.UtcNow;
        Assert.IsNull(approved.Problem, approved.Problem?.Code.ToString());
        Assert.IsTrue(approved.ReadBackGranted);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, approved.CreatedLink!.State);
        await _a.CycleAsync();
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, (await _a.RequireLinkToAsync(_b)).State,
            "the approver never builds a snapshot before its peer's review (§8.3)");

        // B's claim loop (5 s), the grant event, the scheduler's tick and the fetch (§8.2: ≤ 15 s).
        _clock.Advance(TimeSpan.FromSeconds(5));
        _network.Claim(_b.NodeId);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _b.CycleAsync();
        var link = await _b.RequireLinkToAsync(_a);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, link.State);
        Assert.IsNotNull(link.ReviewId, "the review is staged");
        var review = await _b.CallAsync(s => s.GetReviewAsync(link.ReviewId!, default));
        Assert.IsNull(review.Problem, review.Problem?.Code.ToString());
        Assert.IsTrue(review.Source!.FetchedAt - approvedAt <= TimeSpan.FromSeconds(15),
            $"staged {review.Source.FetchedAt - approvedAt} after the approval");

        // 94 Create and 6 Link (all bulk-linkable) custom properties, and the 8 extension groups.
        var plan = review.Plan!;
        int Count(string kind, DataSyncPlanItemType type) =>
            plan.Summary.Counts.Where(c => c.Kind == kind && c.Type == type).Sum(c => c.Count);
        Assert.AreEqual(94, Count(Cp, DataSyncPlanItemType.Create));
        Assert.AreEqual(6, Count(Cp, DataSyncPlanItemType.Link));
        Assert.AreEqual(8, Count(Eg, DataSyncPlanItemType.Create));
        Assert.AreEqual(6, plan.Summary.BulkLinkEligibleCount);

        // "Link all exact matches", then apply.
        var links = plan.Kinds.SelectMany(k => k.Items).Where(i => i.BulkLinkEligible).Select(i =>
            new DataSyncPlanDecision(i.ItemId, DataSyncPlanResolution.Link, i.DefaultTargetLocalKey, null, [],
                i.Candidates.Single(c => c.LocalKey == i.DefaultTargetLocalKey).ReviewToken)).ToList();
        var start = await _b.CallAsync(s => s.ApplyReviewAsync(link.ReviewId!,
            new DataSyncReviewApplyInput(links, false), default));
        Assert.IsNull(start.Problem, $"{start.Problem?.Code} {string.Join(", ", start.DecisionErrors)}");
        await _b.RunWriteTasksAsync();

        link = await _b.RequireLinkToAsync(_a);
        Assert.AreEqual(DataSyncLinkState.Active, link.State);
        Assert.IsNotNull(link.FirstContactCompletedAtUtc);
        var first = (await _b.HistoryAsync()).Single(h => h.Kind == DataSyncHistoryKind.FirstLink);
        var counts = await _b.CallAsync(async s => (await s.GetHistoryAsync(default)).Single(h => h.Id == first.Id).Counts);
        Assert.AreEqual((102, 6), (counts.Created, counts.Linked));
        var transactionMs = System.Text.Json.Nodes.JsonNode.Parse(first.ResultJson)!["transactionMs"]!.GetValue<long>();
        Assert.IsTrue(transactionMs <= 5_000, $"the whole first-sync apply took {transactionMs} ms (§8.10.2 budget)");
        Assert.AreEqual(100, (await PropertiesAsync(_b)).Count, "B holds A's 94 new properties and its own 6");
        Assert.AreEqual(BigTagCount, TagCount((await PropertiesAsync(_b)).Single(p => p.Name == BigTagsName)));
    }

    // ---- 3. A's first pull ---------------------------------------------------------------------------------------

    private async Task Step3_A_first_pull_finds_everything_by_keyAsync()
    {
        await PullAsync(_a);
        var link = await _a.RequireLinkToAsync(_b);
        Assert.AreEqual(DataSyncLinkState.Active, link.State, link.LastErrorCode);
        Assert.IsNotNull(link.FirstContactCompletedAtUtc);

        var items = await _a.ItemsAsync();
        Assert.AreEqual(0, items.Count(i => i.Type == DataSyncInboxItemType.LinkSuggestion),
            "B's review recorded A's keys, so everything is found by key");
        Assert.AreEqual(0, items.Count(i => i.Type == DataSyncInboxItemType.LargeChange), "B5 skips a first contact");
        Assert.AreEqual(0, items.Count, string.Join(", ", items.Select(i => $"{i.Type} {i.SubjectPath}")));

        var shared = (await PropertiesAsync(_a)).Where(p => SharedNames.Contains(p.Name)).ToList();
        Assert.AreEqual(6, shared.Count);
        foreach (var property in shared)
        {
            var labels = ChoiceLabels(property);
            var index = Array.IndexOf(SharedNames, property.Name) + 1;
            CollectionAssert.IsSubsetOf(new[] {"Red", "Green", "Blue", $"B extra {index}"}, labels,
                $"{property.Name}: B's extra option is added on A");
            Assert.AreEqual(4, labels.Count, $"{property.Name}: nothing is duplicated ({string.Join(", ", labels)})");
        }

        Assert.AreEqual(100, (await PropertiesAsync(_a)).Count, "nothing is created on A");

        // §8.3: the approver's first pull is its one "First sync with B" entry.
        var firstPull = (await _a.HistoryAsync()).Single(h => h.Kind == DataSyncHistoryKind.FirstLink);
        Assert.AreEqual(((int?) link.Id, link.PeerName), (firstPull.LinkId, firstPull.PeerName));
        Assert.AreEqual(0, (await _a.HistoryAsync()).Count(h => h.Kind == DataSyncHistoryKind.AutoSync),
            "no other entry for that pull");
        // §8.10.2: the approver's first pull of this fixture stays within the CI budget.
        var transactionMs = System.Text.Json.Nodes.JsonNode.Parse(firstPull.ResultJson)!["transactionMs"]!.GetValue<long>();
        Assert.IsTrue(transactionMs <= 5_000, $"the approver's first pull took {transactionMs} ms");
    }

    // ---- 4. Convergence ------------------------------------------------------------------------------------------

    private async Task Step4_convergenceAsync()
    {
        await PullAsync(_b);
        await PullAsync(_a);
        await AssertConvergedAsync();

        var before = await RevisionsAsync();
        await PullAsync(_b);
        await PullAsync(_a);
        var after = await RevisionsAsync();
        CollectionAssert.AreEqual(before.A, after.A, "one more round makes no revision on A");
        CollectionAssert.AreEqual(before.B, after.B, "one more round makes no revision on B");
    }

    // ---- 5. A concurrent rename ----------------------------------------------------------------------------------

    private async Task Step5_a_concurrent_rename_is_decided_onceAsync()
    {
        await RenameAsync(_a, "Prop 10", "Prop 10 (A)");
        await RenameAsync(_b, "Prop 10", "Prop 10 (B)");
        await PullAsync(_b);
        await PullAsync(_a);

        var aItem = (await _a.ItemsAsync()).Single();
        var bItem = (await _b.ItemsAsync()).Single();
        Assert.AreEqual((DataSyncInboxItemType.FieldConflict, "name"), (aItem.Type, aItem.SubjectPath));
        Assert.AreEqual((DataSyncInboxItemType.FieldConflict, "name"), (bItem.Type, bItem.SubjectPath));

        // B decides: A's name.
        var view = await _b.CallAsync(s => s.GetInboxItemAsync(bItem.Id, default));
        CollectionAssert.Contains(view!.AllowedActions.ToList(), DataSyncInboxAction.UseRemote);
        await _b.RunAsync(await _b.CallAsync(s => s.ResolveAsync(new DataSyncResolveBatchInput(
            [new DataSyncResolveInput(bItem.Id, DataSyncInboxAction.UseRemote, view.Token, null, null, null, null)],
            false), default)));
        var bClosed = (await _b.ItemsAsync(false)).Single(i => i.Id == bItem.Id);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedHere, bClosed.Closure);

        // A's next pull closes its item as decided elsewhere.
        await PullAsync(_a);
        var aClosed = (await _a.ItemsAsync(false)).Single(i => i.Id == aItem.Id);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, aClosed.Closure);
        Assert.AreEqual(0, (await _a.ItemsAsync()).Count);

        await PullAsync(_b);
        Assert.IsTrue((await PropertiesAsync(_a)).Any(p => p.Name == "Prop 10 (A)"));
        Assert.IsTrue((await PropertiesAsync(_b)).Any(p => p.Name == "Prop 10 (A)"));
        await AssertConvergedAsync();
    }

    // ---- 7. Undo of B's first sync -------------------------------------------------------------------------------

    private async Task Step7_undo_of_the_first_sync_reaches_nobodyAsync()
    {
        var first = (await _b.HistoryAsync()).Single(h => h.Kind == DataSyncHistoryKind.FirstLink);
        var preview = await _b.CallAsync(s => s.PreviewUndoAsync(first.Id, default));
        Assert.IsTrue(preview.CanUndo, preview.Problem?.Code.ToString());
        var removable = preview.Items.Count(i => i.Action == DataSyncUndoAction.Remove && i.Blocked is null);
        Assert.IsTrue(removable > 90, $"{removable} created definitions can be removed");

        var aProperties = (await PropertiesAsync(_a)).Select(p => p.Name).OrderBy(n => n).ToList();
        var aKeys = (await _a.KeysAsync()).Keys.OrderBy(k => k.Kind).ThenBy(k => k.Key).ToList();
        var aItems = (await _a.ItemsAsync(false)).Count;
        var bProperties = (await PropertiesAsync(_b)).Count;

        await _b.RunAsync(await _b.CallAsync(s => s.StartUndoAsync(first.Id, default)));
        Assert.IsNotNull((await _b.HistoryAsync()).Single(h => h.Id == first.Id).UndoneAtUtc);
        Assert.IsTrue((await PropertiesAsync(_b)).Count <= bProperties - 90, "the created definitions are gone on B");

        // Nothing is created or re-linked on A at its next pull: undone creates are never served. B no longer offers
        // any extension group (it had only A's), so that pull asks first (B3, §8.7); applying it as usual changes
        // nothing on A either and records them as no longer offered (§8.8).
        async Task AssertNothingChangedOnAAsync(string when)
        {
            CollectionAssert.AreEqual(aProperties,
                (await PropertiesAsync(_a)).Select(p => p.Name).OrderBy(n => n).ToList(), when);
            CollectionAssert.AreEqual(aKeys,
                (await _a.KeysAsync()).Keys.OrderBy(k => k.Kind).ThenBy(k => k.Key).ToList(), when);
            Assert.AreEqual(aItems, (await _a.ItemsAsync(false)).Count, $"{when}: no item on A");
            Assert.AreEqual(8, await _a.InScopeAsync(async sp =>
                (await sp.GetRequiredService<IExtensionGroupService>().GetAll()).Length), when);
        }

        await PullAsync(_a);
        await AssertNothingChangedOnAAsync("paused");
        var aLink = await _a.RequireLinkToAsync(_b);
        Assert.AreEqual((DataSyncLinkState.Paused, DataSyncPauseReason.KindEmptied), (aLink.State, aLink.PausedReason));
        StringAssert.Contains(aLink.PausedDetail, "kind=" + Eg);
        var resumed = await _a.CallAsync(s => s.ResumeLinkAsync(aLink.Id, DataSyncResumeAction.ApplyAsUsual, default));
        Assert.IsNull(resumed.Problem, resumed.Problem?.Code.ToString());
        await PullAsync(_a);
        await AssertNothingChangedOnAAsync("applied as usual");
        var aView = (await _a.CallAsync(s => s.GetLinksAsync(default))).Single();
        Assert.AreEqual((DataSyncLinkState.Active, 8), (aView.State, aView.MissingAtPeerCount));

        // A full reconciliation on B changes nothing: its bases of what it undid are excluded (§8.11).
        var before = await RevisionsAsync();
        var bNames = (await PropertiesAsync(_b)).Select(p => p.Name).OrderBy(n => n).ToList();
        _clock.Advance(DataSyncSchedule.FullReconciliationInterval);
        await PullAsync(_b);
        var link = await _b.RequireLinkToAsync(_a);
        Assert.AreEqual(_clock.UtcNow, link.LastFullReconciliationAtUtc, "the pull was a full reconciliation");
        CollectionAssert.AreEqual(bNames, (await PropertiesAsync(_b)).Select(p => p.Name).OrderBy(n => n).ToList());
        CollectionAssert.AreEqual(before.B, (await RevisionsAsync()).B, "no revision on B");
        Assert.AreEqual(0, (await _b.ItemsAsync()).Count, "no item on B");
    }

    // ---- 8. Restore detection ------------------------------------------------------------------------------------

    /// <summary>
    /// B's database alone goes back to the backup, its <c>actor.json</c> stays: the first actor check after the start
    /// finds the file ahead of the database (§5.6) and pauses every link.
    /// </summary>
    private async Task Step8a_a_database_rewound_alone_is_detected_by_its_own_recordsAsync()
    {
        await RestartBAsync(_bDatabaseBackup, _b.DataSyncFolder);
        await _b.TickAsync();

        var state = await _b.LocalStateAsync();
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, state.RestoreReason);
        StringAssert.Contains(state.RestoreDetail, "watermark");
        Assert.AreNotEqual(_bBackupState.ActorId, state.ActorId, "the actor rotated at detection");
        Assert.AreEqual((DataSyncLinkState.Paused, DataSyncPauseReason.LocalRestoreDetected),
            await StateOfAsync(_b, _a));
        Assert.AreEqual(1, await RestoreNotificationsAsync(_b), "one notification (§9.4)");

        await AfterDetectionAsync(DataSyncRestoreChoice.ThisDeviceWins, null);
    }

    /// <summary>
    /// B's whole data directory goes back to the backup and B edits 60 definitions before its first pull: A's next
    /// read of B carries cursors B never issued (§7.5.1), which B reports before any Refresh can reissue a counter.
    /// </summary>
    private async Task Step8b_a_whole_directory_restore_is_detected_by_the_readerAsync()
    {
        await RestartBAsync(Path.Combine(_bDirectoryBackup, "test.db"), Path.Combine(_bDirectoryBackup, "data-sync"));
        await _b.TickAsync();
        Assert.IsNull((await _b.LocalStateAsync()).RestoreReason, "a consistent backup has no records of its own");
        await EditSixtyAsync(_b);

        // A's next read, within B's first two minutes (§5.6: B is unverified until then).
        await PullAsync(_a);
        await AssertDetectedBeforeAnyCounterAsync(DataSyncPauseReason.LocalRestoreDetected, "reader");
        Assert.AreNotEqual(DataSyncLinkState.Paused, (await _a.RequireLinkToAsync(_b)).State,
            "A waits for B's choice without pausing (§7.5.2)");
        // Announced when the reader's head detected it, not only at B's next tick; and the tick adds none.
        await WaitUntilAsync(async () => await RestoreNotificationsAsync(_b) == 1, "the restore is announced");
        await _b.TickAsync();
        Assert.AreEqual(1, await RestoreNotificationsAsync(_b), "one notification (§9.4)");

        await AfterDetectionAsync(DataSyncRestoreChoice.OthersWin, null);
    }

    /// <summary>
    /// The same restore, but B heads A first: A has seen newer counters of B's restored actor (§7.5.1
    /// <c>SeenCounter</c>). One peer's evidence pauses that link only (§5.6).
    /// </summary>
    private async Task Step8c_a_whole_directory_restore_is_detected_at_startupAsync()
    {
        await RestartBAsync(Path.Combine(_bDirectoryBackup, "test.db"), Path.Combine(_bDirectoryBackup, "data-sync"));
        await _b.TickAsync();
        await EditSixtyAsync(_b);

        _clock.Advance(DataSyncSchedule.StartupDelay);
        await _b.CycleAsync();
        await AssertDetectedBeforeAnyCounterAsync(DataSyncPauseReason.LocalRestoreSuspected, "peer");
        var link = await _b.RequireLinkToAsync(_a);
        Assert.AreEqual(link.Id, (await _b.LocalStateAsync()).RestoreLinkId);
        Assert.AreEqual(1, await RestoreNotificationsAsync(_b), "one notification (§9.4)");

        await AfterDetectionAsync(DataSyncRestoreChoice.ThisDeviceWins, link.Id);
    }

    private async Task RestartBAsync(string database, string dataSyncFolder)
    {
        var directory = Bakabase.TestKit.Utils.TestServiceBuilder.NewTestDirectory();
        Directory.CreateDirectory(directory);
        File.Copy(database, Path.Combine(directory, "test.db"));
        TwoHostNode.CopyFolder(dataSyncFolder, Path.Combine(directory, "data-sync"));
        _b = await TwoHostNode.StartAsync(_network, _b.Device, directory);
    }

    /// <summary>60 local edits, made before the first pull (§13.7 step 8).</summary>
    private static Task EditSixtyAsync(TwoHostNode node) => node.InScopeAsync(async sp =>
    {
        var service = sp.GetRequiredService<ICustomPropertyService>();
        foreach (var property in (await service.GetAll()).Where(p => p.Name.StartsWith("Prop ")).Take(60))
        {
            await service.Put(property.Id, new CustomPropertyAddOrPutDto
            {
                Name = property.Name + " (restored)", Type = property.Type,
                Options = property.Options is null ? null : JsonConvert.SerializeObject(property.Options),
            });
        }
    });

    /// <summary>
    /// Detected before any counter was reissued (§5.6): the restored actor retired with its counter and no vector
    /// carries a higher one of it; whatever was published since — the 60 edits, once every link was paused and the
    /// actor verified — carries the new actor.
    /// </summary>
    private async Task AssertDetectedBeforeAnyCounterAsync(DataSyncPauseReason reason, string evidence)
    {
        var state = await _b.LocalStateAsync();
        Assert.AreEqual(reason, state.RestoreReason);
        StringAssert.Contains(state.RestoreDetail, evidence);
        Assert.AreNotEqual(_bBackupState.ActorId, state.ActorId, "the actor rotated at detection");
        var retired = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(state.RetiredActorsJson!)!;
        Assert.IsTrue(retired.GetValueOrDefault(_bBackupState.ActorId) >= _bBackupState.ActorCounter,
            "the restored actor retired with its counter");
        Assert.AreEqual((DataSyncLinkState.Paused, reason), await StateOfAsync(_b, _a));

        // No counter of the restored actor was issued again: every vector keeps at most the backup's counter of it, and
        // every row that changed since the backup (the 60 edits, once published) carries the new actor.
        var backup = (await ReadRowsAsync(Path.Combine(_bDirectoryBackup, "test.db")))
            .ToDictionary(r => r.Key, r => r.VvJson);
        foreach (var row in await _b.EntitiesAsync(true))
        {
            var counters = Bakabase.Modules.DataSync.Identity.DataSyncVersionVector.ParseStored(row.VvJson).Counters;
            Assert.IsTrue(counters.GetValueOrDefault(_bBackupState.ActorId) <= _bBackupState.ActorCounter,
                $"{row.Kind}/{row.LocalKey} carries a counter the restored actor issued again: {row.VvJson}");
            if (backup.GetValueOrDefault($"{row.Kind}/{row.SyncKey}") != row.VvJson)
                Assert.IsTrue(counters.GetValueOrDefault(state.ActorId) > 0, $"{row.Kind}/{row.LocalKey}: {row.VvJson}");
        }
    }

    /// <summary>
    /// The restore choice, then two pulls in both directions (§13.7 step 8): each applies, and nothing pauses again.
    /// </summary>
    private async Task AfterDetectionAsync(DataSyncRestoreChoice choice, int? linkId)
    {
        var restore = await _b.CallAsync(s => s.GetRestoreAsync(default));
        Assert.IsTrue(restore.Pending);
        await _b.RunAsync(await _b.CallAsync(s => s.ChooseRestoreAsync(choice, linkId, default)));
        Assert.IsNull((await _b.LocalStateAsync()).RestoreReason, "the choice clears the restore");
        Assert.AreEqual(DataSyncLinkState.Active, (await _b.RequireLinkToAsync(_a)).State);

        for (var round = 0; round < 2; round++)
        {
            // A waits up to an hour after a source asked it to wait for a restore choice (§8.2).
            await PullAsync(_b);
            await AssertPulledAsync(_b, _a, $"{choice}, round {round + 1}, B pulled A", round == 0);
            _clock.Advance(DataSyncSchedule.AccessRetry);
            await PullAsync(_a);
            await AssertPulledAsync(_a, _b, $"{choice}, round {round + 1}, A pulled B", round == 0);
        }

        // What the restore left to decide is items, never pauses: 60 edits in one pull wait as a large change (B5,
        // §8.7), and a change this device's restored copy never saw — made by its own later incarnation — meets its
        // restored one as concurrent (§9.5). Decided (Apply all; keep this device's), the two sides agree again.
        for (var settle = 0; settle < 3; settle++)
        {
            var decided = false;
            foreach (var node in new[] {_a, _b})
            {
                foreach (var item in await node.ItemsAsync())
                {
                    var action = item.Type switch
                    {
                        DataSyncInboxItemType.LargeChange => DataSyncInboxAction.ApplyAll,
                        DataSyncInboxItemType.FieldConflict => DataSyncInboxAction.KeepLocal,
                        _ => throw new AssertFailedException($"{choice}: {node.Name} has {item.Type} {item.SubjectPath}"),
                    };
                    var view = await node.CallAsync(s => s.GetInboxItemAsync(item.Id, default));
                    await node.RunAsync(await node.CallAsync(s => s.ResolveAsync(new DataSyncResolveBatchInput(
                        [new DataSyncResolveInput(item.Id, action, view!.Token, null, null, null, null)], false),
                        default)));
                    decided = true;
                }
            }

            if (!decided) break;
            await PullAsync(_b);
            await PullAsync(_a);
        }

        foreach (var node in new[] {_a, _b})
        {
            var open = await node.ItemsAsync();
            Assert.AreEqual(0, open.Count, $"{choice}: {node.Name} still has {string.Join(", ", open.Select(i => i.Type))}");
        }

        await AssertConvergedAsync();
    }

    /// <summary>
    /// <paramref name="puller"/>'s cycle reached its peer without a pause or an error, and — with
    /// <paramref name="applied"/> — applied a pull; neither side has a restore pending or a paused link.
    /// </summary>
    private async Task AssertPulledAsync(TwoHostNode puller, TwoHostNode peer, string when, bool applied)
    {
        var link = await puller.RequireLinkToAsync(peer);
        Assert.AreEqual(DataSyncLinkState.Active, link.State, $"{when}: {link.PausedReason} {link.PausedDetail}");
        Assert.IsNull(link.LastErrorCode, $"{when}: {link.LastErrorCode} {link.LastErrorDetail}");
        Assert.AreEqual(_clock.UtcNow, link.LastAttemptAtUtc, $"{when}: the cycle reached the peer");
        if (applied) Assert.AreEqual(_clock.UtcNow, link.LastSyncedAtUtc, $"{when}: the pull applied");
        Assert.AreNotEqual(DataSyncLinkState.Paused, (await peer.RequireLinkToAsync(puller)).State, when);
        Assert.IsNull((await _b.LocalStateAsync()).RestoreReason, $"{when}: no new restore on B");
        Assert.IsNull((await _a.LocalStateAsync()).RestoreReason, $"{when}: no restore on A");
    }

    /// <summary>"Data sync is paused: this device's data looks restored" notifications (§9.4).</summary>
    private static Task<int> RestoreNotificationsAsync(TwoHostNode node) => node.InScopeAsync(async sp =>
    {
        var found = await sp.GetRequiredService<INotificationService>().SearchAsync(new NotificationSearchInputModel
        {
            Source = DataSyncNotifier.Source, PageSize = 100,
        });
        return (found.Data ?? []).Count(n => n.PayloadJson?.Contains("\"case\":\"restore\"") == true);
    });

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting until {what}.");
            await Task.Delay(20);
        }
    }

    private static async Task<(DataSyncLinkState, DataSyncPauseReason?)> StateOfAsync(TwoHostNode node, TwoHostNode peer)
    {
        var link = await node.RequireLinkToAsync(peer);
        return (link.State, link.PausedReason);
    }

    private static async Task<List<(string Key, string VvJson)>> ReadRowsAsync(string database)
    {
        var rows = new List<(string, string)>();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {DataSource = database, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false}.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind, SyncKey, VvJson FROM DataSyncEntities";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(($"{reader.GetString(0)}/{reader.GetString(1)}", reader.GetString(2)));
        return rows;
    }

    // ---- 6. An in-use child deletion -----------------------------------------------------------------------------

    /// <summary>
    /// A deletes an option resources use on B: B holds it and asks (§8.5.4). Resetting B's link keeps the option on B
    /// only and closes the item (§8.1).
    /// </summary>
    private async Task Step6_an_in_use_child_deletion_is_held_until_the_link_is_resetAsync()
    {
        const string name = "Shared 4";
        var bProperty = (await PropertiesAsync(_b)).Single(p => p.Name == name);
        var green = Choices(bProperty).Single(c => c.Label == "Green").Value;
        await _b.InScopeAsync(async sp =>
        {
            var values = sp.GetRequiredService<ICustomPropertyValueService>();
            await values.AddDbModelRange([
                new Bakabase.Modules.Property.Abstractions.Models.Db.CustomPropertyValueDbModel
                {
                    ResourceId = 1, PropertyId = bProperty.Id, Scope = (int) PropertyValueScope.Manual,
                    Value = new List<string> {green}.SerializeAsStandardValue(StandardValueType.ListString),
                },
            ]);
        });

        await _a.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<ICustomPropertyService>();
            var property = (await service.GetAll()).Single(p => p.Name == name);
            var options = JsonConvert.DeserializeObject<MultipleChoicePropertyOptions>(
                JsonConvert.SerializeObject(property.Options))!;
            options.Choices = options.Choices!.Where(c => c.Label != "Green").ToList();
            await service.Put(property.Id, new CustomPropertyAddOrPutDto
            {
                Name = name, Type = property.Type, Options = JsonConvert.SerializeObject(options),
            });
        });

        await PullAsync(_b);
        var item = (await _b.ItemsAsync()).Single();
        Assert.AreEqual(DataSyncInboxItemType.ChildDeletedInUse, item.Type);
        Assert.IsTrue(Choices((await PropertiesAsync(_b)).Single(p => p.Name == name)).Any(c => c.Value == green),
            "B keeps the option its resources use");
        var entity = (await _b.EntitiesAsync()).Single(e => e.Kind == Cp && e.LocalKey == bProperty.Id.ToString());
        var overlay = DataSyncViews.ReadOverlay(entity.OverlayJson);
        CollectionAssert.AreEqual(new[] {green}, overlay.HeldChildren.Select(h => h.ChildId).ToArray());

        var link = await _b.RequireLinkToAsync(_a);
        Assert.IsNull(await _b.CallAsync(s => s.ResetLinkAsync(link.Id, default)));

        entity = (await _b.EntitiesAsync()).Single(e => e.Kind == Cp && e.LocalKey == bProperty.Id.ToString());
        overlay = DataSyncViews.ReadOverlay(entity.OverlayJson);
        Assert.AreEqual(0, overlay.HeldChildren.Count, "nobody can decide it any more");
        CollectionAssert.Contains(overlay.LocalOnlyChildren.ToList(), green, "the option stays on B only");
        var closed = (await _b.ItemsAsync(false)).Single(i => i.Id == item.Id);
        Assert.AreEqual(DataSyncInboxClosure.LinkRemoved, closed.Closure);
        Assert.IsNull(await _b.LinkToAsync(_a));
        Assert.IsTrue(Choices((await PropertiesAsync(_b)).Single(p => p.Name == name)).Any(c => c.Value == green));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static Task RenameAsync(TwoHostNode node, string from, string to) => node.InScopeAsync(async sp =>
    {
        var service = sp.GetRequiredService<ICustomPropertyService>();
        var property = (await service.GetAll()).Single(p => p.Name == from);
        await service.Put(property.Id, new CustomPropertyAddOrPutDto
        {
            Name = to, Type = property.Type,
            Options = property.Options is null ? null : JsonConvert.SerializeObject(property.Options),
        });
    });

    private static DataSyncDevice Device(string name)
    {
        var id = Guid.NewGuid().ToString("N");
        return new DataSyncDevice(id, Guid.NewGuid().ToString("N"), name);
    }

    /// <summary>A pull by <paramref name="node"/>: its link is due again a minute later (§8.2).</summary>
    private async Task PullAsync(TwoHostNode node)
    {
        _clock.Advance(TimeSpan.FromSeconds(61));
        await node.CycleAsync();
    }

    /// <summary>Every synced entity has the same comparison form on both sides (§3.4, SharedHash by key).</summary>
    private async Task AssertConvergedAsync()
    {

        var aRows = (await _a.EntitiesAsync()).Where(e => e.State == DataSyncEntitySyncState.Synced).ToList();
        var bRows = (await _b.EntitiesAsync()).Where(e => e.State == DataSyncEntitySyncState.Synced).ToList();
        var bKeys = await _b.KeysAsync();
        Assert.AreEqual(aRows.Count, bRows.Count, "both sides hold the same entities");
        foreach (var row in aRows)
        {
            Assert.IsTrue(bKeys.TryGetValue((row.Kind, row.SyncKey), out var bId),
                $"{row.Kind}/{row.LocalKey} is on B under its key");
            var other = bRows.Single(r => r.Id == bId);
            Assert.AreEqual(row.SharedHash, other.SharedHash, $"{row.Kind}/{row.LocalKey}: comparison forms differ");
        }
    }

    private async Task<(List<string> A, List<string> B)> RevisionsAsync()
    {
        static async Task<List<string>> Of(TwoHostNode node) =>
        [
            $"lastSeq={(await node.LocalStateAsync()).LastSeq}",
            .. (await node.EntitiesAsync(true)).Select(e => $"{e.Kind}/{e.SyncKey}:{e.Seq}:{e.VvJson}"),
        ];

        return (await Of(_a), await Of(_b));
    }

    private static Task<List<Bakabase.Abstractions.Models.Domain.CustomProperty>> PropertiesAsync(TwoHostNode node) =>
        node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().GetAll());

    private static List<string> ChoiceLabels(Bakabase.Abstractions.Models.Domain.CustomProperty property)
    {
        var json = JsonConvert.SerializeObject(property.Options);
        var options = JsonConvert.DeserializeObject<MultipleChoicePropertyOptions>(json)!;
        return options.Choices?.Select(c => c.Label).ToList() ?? [];
    }

    private static List<ChoiceOptions> Choices(Bakabase.Abstractions.Models.Domain.CustomProperty property) =>
        JsonConvert.DeserializeObject<MultipleChoicePropertyOptions>(JsonConvert.SerializeObject(property.Options))!
            .Choices ?? [];

    private static int TagCount(Bakabase.Abstractions.Models.Domain.CustomProperty property) =>
        JsonConvert.DeserializeObject<TagsPropertyOptions>(JsonConvert.SerializeObject(property.Options))!.Tags?.Count ?? 0;

    private static CustomPropertyAddOrPutDto Choices(string name, bool single, params string[] labels)
    {
        var choices = labels.Select((label, i) => new ChoiceOptions
        {
            Value = Guid.NewGuid().ToString(), Label = label,
        }).ToList();
        object options = single
            ? new SingleChoicePropertyOptions {Choices = choices}
            : new MultipleChoicePropertyOptions {Choices = choices};
        return new CustomPropertyAddOrPutDto
        {
            Name = name, Type = single ? PropertyType.SingleChoice : PropertyType.MultipleChoice,
            Options = JsonConvert.SerializeObject(options),
        };
    }

    private static CustomPropertyAddOrPutDto BigTags() => new()
    {
        Name = BigTagsName,
        Type = PropertyType.Tags,
        Options = JsonConvert.SerializeObject(new TagsPropertyOptions
        {
            Tags = Enumerable.Range(0, BigTagCount).Select(i =>
                new TagsPropertyOptions.TagOptions($"Group {i % 50}", $"Tag {i:D5}")
                {
                    Value = $"00000000-0000-4000-9000-{i:D12}",
                }).ToList(),
        }),
    };
}
