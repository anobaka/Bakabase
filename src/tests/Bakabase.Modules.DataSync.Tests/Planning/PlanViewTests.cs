using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Planning;

/// <summary>v3.1 §7.8: truncation (per item, per candidate and plan-wide), paging, and the refused-apply answer.</summary>
[TestClass]
public class PlanViewTests
{
    [TestMethod]
    public void TruncateKeepsEveryScalarAndTheFirstChildChangesPerItemAndCandidate()
    {
        var item = ItemOf("i", [Scalar("name"), Scalar("ignoreCase"), Scalar("defaultValue"), .. Children(300)], [],
            CandidateOf("7", Children(250)));
        var full = PlanOf(item);

        var view = DataSyncPlanView.Truncate(full);
        var cut = view.Kinds[0].Items[0];
        Assert.AreEqual(DataSyncPlanView.MaxInline, cut.Changes.Count);
        CollectionAssert.AreEqual(new[] { "name", "ignoreCase", "defaultValue", "child:add:c0" },
            cut.Changes.Take(4).Select(c => c.ChangeId).ToArray());
        Assert.AreEqual("child:add:c196", cut.Changes[^1].ChangeId);
        Assert.IsTrue(cut.ChangesTruncated);
        Assert.AreEqual(item.ChangeCounts, cut.ChangeCounts, "counts describe the full list");
        Assert.AreEqual(303, cut.ChangeCounts.Total);

        var candidate = cut.Candidates.Single();
        Assert.AreEqual(DataSyncPlanView.MaxInline, candidate.Changes.Count);
        Assert.IsTrue(candidate.ChangesTruncated);
        Assert.AreEqual(250, candidate.ChangeCounts.Total);
        Assert.AreEqual(full.PlanId, view.PlanId);
    }

    [TestMethod]
    public void ASmallPlanIsLeftAsItIs()
    {
        var full = DataSyncPlanner.Plan(PlannerTests.RichFixture().Input());
        var view = DataSyncPlanView.Truncate(full);
        CollectionAssert.AreEqual(DataSyncPlanFormat.CanonicalBytes(full), DataSyncPlanFormat.CanonicalBytes(view));
    }

    [TestMethod]
    public void PagesIndexTheFullOrderedListWithTheirWarnings()
    {
        var changes = Children(303);
        var warnings = changes.Select(c => Conflict(c.ChangeId)).ToList();
        var full = PlanOf(ItemOf("i", changes, warnings, CandidateOf("7", Children(20))));

        var rest = DataSyncPlanView.Page(full, "i", null, 200, 500);
        Assert.IsNull(rest.Problem);
        Assert.AreEqual(full.PlanId, rest.PlanId);
        Assert.AreEqual(303, rest.Total);
        CollectionAssert.AreEqual(changes.Skip(200).Select(c => c.ChangeId).ToArray(),
            rest.Changes.Select(c => c.ChangeId).ToArray());
        CollectionAssert.AreEqual(rest.Changes.Select(c => c.ChangeId).ToArray(),
            rest.Warnings.Select(w => w.ChangeId).ToArray(), "the warnings of exactly those changes");

        var first = DataSyncPlanView.Page(full, "i", null, 0, 10);
        CollectionAssert.AreEqual(changes.Take(10).ToArray(), first.Changes.ToArray(), "skip 0 repeats the inline ones");

        Assert.AreEqual(DataSyncPlanView.MaxPageSize, DataSyncPlanView.Page(Big(), "i", null, 0, 10_000).Changes.Count,
            "take is clamped to 500");
        Assert.AreEqual(5, DataSyncPlanView.Page(full, "i", null, -3, 5).Changes.Count);
        Assert.AreEqual(0, DataSyncPlanView.Page(full, "i", null, 1_000, 5).Changes.Count);

        var candidate = DataSyncPlanView.Page(full, "i", "7", 15, 100);
        Assert.AreEqual(20, candidate.Total);
        Assert.AreEqual(5, candidate.Changes.Count);

        Assert.AreEqual(DataSyncProblemCode.UnknownItem, DataSyncPlanView.Page(full, "nope", null, 0, 5).Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.UnknownItem, DataSyncPlanView.Page(full, "i", "8", 0, 5).Problem?.Code);

        static DataSyncPlan Big() => PlanOf(ItemOf("i", Children(900), []));
    }

    [TestMethod]
    public void InlineChangesKeepTheirWarningsAndItemLevelWarningsAreCappedApart()
    {
        // 300 folded adds, each with its OptionLabelConflict, and 250 item-level warnings (M-e).
        var changes = Children(300);
        var warnings = changes.Select(c => Conflict(c.ChangeId))
            .Concat(Enumerable.Range(0, 250).Select(_ => new DataSyncPlanWarning(DataSyncWarningCode.OptionDropped, null,
                new Dictionary<string, string> { ["reason"] = "label" })))
            .ToList();
        var item = ItemOf("i", changes, DataSyncPlanFormat.SortWarnings(warnings));

        var cut = DataSyncPlanView.Truncate(PlanOf(item)).Kinds[0].Items[0];
        var inline = cut.Changes.Select(c => c.ChangeId).ToHashSet();
        Assert.AreEqual(200, inline.Count);
        foreach (var id in inline)
            Assert.IsTrue(cut.Warnings.Any(w => w.Code == DataSyncWarningCode.OptionLabelConflict && w.ChangeId == id), id);
        Assert.IsFalse(cut.Warnings.Any(w => w.ChangeId is not null && !inline.Contains(w.ChangeId)),
            "the warnings of a paged change come with its page");
        Assert.AreEqual(DataSyncPlanView.MaxInline, cut.Warnings.Count(w => w.ChangeId is null));
        Assert.IsTrue(cut.WarningsTruncated);
        CollectionAssert.AreEqual(item.WarningCounts.ToArray(), cut.WarningCounts.ToArray());
    }

    [TestMethod]
    public void ThePlanWideBudgetIsSpentInPlanOrder()
    {
        // 150 items × 150 child changes = 22,500 > 20,000.
        var items = Enumerable.Range(0, 150)
            .Select(i => ItemOf(Name(i), [Scalar("name"), .. Children(150)], [])).ToArray();
        var cut = DataSyncPlanView.Truncate(PlanOf(items)).Kinds[0].Items;

        Assert.AreEqual(DataSyncPlanView.MaxInlineChildChanges,
            cut.Sum(i => i.Changes.Count(DataSyncPlanFormat.IsChild)));
        for (var i = 0; i < 150; i++)
        {
            var children = cut[i].Changes.Count(DataSyncPlanFormat.IsChild);
            Assert.AreEqual(i < 133 ? 150 : i == 133 ? 50 : 0, children, Name(i));
            Assert.AreEqual(i >= 133, cut[i].ChangesTruncated, Name(i));
            Assert.AreEqual("name", cut[i].Changes[0].ChangeId, "scalars are always inline");
        }
    }

    [TestMethod]
    public void EachItemsCandidatesSpendTheBudgetAfterIt()
    {
        // 70 items × (150 own + 150 on their candidate): 66 fit (19,800); the 67th keeps all 150 of its own and the
        // last 50 go to its candidate.
        var items = Enumerable.Range(0, 70)
            .Select(i => ItemOf(Name(i), Children(150), [], CandidateOf("1", Children(150)))).ToArray();
        var cut = DataSyncPlanView.Truncate(PlanOf(items)).Kinds[0].Items;

        Assert.AreEqual(150, cut[65].Candidates[0].Changes.Count);
        Assert.AreEqual(150, cut[66].Changes.Count);
        Assert.IsFalse(cut[66].ChangesTruncated);
        Assert.AreEqual(50, cut[66].Candidates[0].Changes.Count);
        Assert.IsTrue(cut[66].Candidates[0].ChangesTruncated);
        Assert.AreEqual(0, cut[67].Changes.Count);
        Assert.AreEqual(0, cut[67].Candidates[0].Changes.Count);
    }

    [TestMethod]
    public void ARefusedApplyCarriesTheErrorsAndTheFreshPlanTruncated()
    {
        var full = PlanOf(ItemOf("i", Children(300), []));
        var errors = new[] { new DataSyncDecisionError("i", DataSyncDecisionErrorCode.ChangedSinceReview) };

        var start = DataSyncPlanView.RejectDecisions(full, errors);
        Assert.IsNull(start.TaskId);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, start.Problem?.Code);
        CollectionAssert.AreEqual(errors, start.DecisionErrors.ToArray());
        Assert.AreEqual(DataSyncPlanView.MaxInline, start.Plan!.Kinds[0].Items[0].Changes.Count);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static string Name(int i) => "i" + i.ToString(CultureInfo.InvariantCulture);

    private static DataSyncFieldChange Scalar(string path) =>
        new(path, DataSyncFieldChangeKind.Set, path, null, new DataSyncDisplayValue(path), null, null);

    private static List<DataSyncFieldChange> Children(int count) => Enumerable.Range(0, count)
        .Select(i => i.ToString(CultureInfo.InvariantCulture))
        .Select(i => new DataSyncFieldChange("child:add:c" + i, DataSyncFieldChangeKind.AddChild, "choices", null,
            new DataSyncDisplayValue("L" + i), null, null))
        .ToList();

    private static DataSyncPlanWarning Conflict(string changeId) =>
        new(DataSyncWarningCode.OptionLabelConflict, changeId, new Dictionary<string, string> { ["when"] = "always" });

    private static DataSyncPlanCandidate CandidateOf(string localKey, IReadOnlyList<DataSyncFieldChange> changes) =>
        new(localKey, "local", null, DataSyncNaturalMatch.Exact, changes,
            DataSyncPlanFormat.CountChanges(changes.ToList()), false, [], [], false, 0, 0, true, "c-token");

    private static DataSyncPlanItem ItemOf(string id, IReadOnlyList<DataSyncFieldChange> changes,
        IReadOnlyList<DataSyncPlanWarning> warnings, params DataSyncPlanCandidate[] candidates) =>
        new(id, "testItem", DataSyncPlanItemType.Update, null, null, new DataSyncPlanEntity(null, "incoming", null, 0, 0),
            new DataSyncPlanEntity("1", "local", null, 0, 0), candidates, changes,
            DataSyncPlanFormat.CountChanges(changes.ToList()), false, 0, 0,
            [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip], DataSyncPlanResolution.Update, "1", false, false,
            false, false, "token", warnings, DataSyncPlanFormat.CountWarnings(warnings), false);

    private static DataSyncPlan PlanOf(params DataSyncPlanItem[] items) =>
        new("0123456789abcdef", "sha256:0", [new DataSyncPlanKindSection("testItem", 1, true, items, 0)],
            new DataSyncPlanSummary([], 0, 0, 0), []);
}
