using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Planning.PlanFixture;

namespace Bakabase.Modules.DataSync.Tests.Planning;

/// <summary>v3.1 §7.7: strict and non-strict Resolve, CompleteDecisions, and the operations decisions become.</summary>
[TestClass]
public class ResolveTests
{
    // ---- strict: every error code ------------------------------------------------------------------

    [TestMethod]
    public void AnItemThatNeedsADecisionHasOne()
    {
        var f = LinkFixture();
        var plan = f.Plan();

        var result = f.Resolve(plan, [], strict: true);
        Assert.AreEqual(0, result.Items.Count, "nothing resolves while any decision is invalid");
        AssertErrors(result, (Item(plan, 1).ItemId, DataSyncDecisionErrorCode.DecisionMissing));
        CollectionAssert.AreEqual(result.Errors.ToArray(), DataSyncPlanner.ValidateDecisions(plan, []).ToArray());
    }

    [TestMethod]
    public void DefaultsStandInForMissingDecisionsAtTheBoundary()
    {
        var f = new PlanFixture();
        f.Local("12", 2, T("Genre", ("a", "Action")));
        f.Local("13", 3, T("Same"));
        f.Pull(1, T("New"));
        f.Pull(2, T("Genres", ("a", "Action")));
        f.Pull(3, T("Same"));
        var plan = f.Plan();

        var result = f.Resolve(plan, [], strict: true);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(DataSyncItemAction.Created, Resolved(result, 1).Action);
        Assert.AreEqual(DataSyncItemAction.Updated, Resolved(result, 2).Action);
        Assert.AreEqual(DataSyncItemOutcome.NoChange, Resolved(result, 3).Outcome);
    }

    [TestMethod]
    public void AResolutionTheItemDoesNotAllowIsRefused()
    {
        var f = LinkFixture();
        f.PullHeldAtSource(9);
        var plan = f.Plan();
        var link = Item(plan, 1);
        var held = Item(plan, 9);

        var result = f.Resolve(plan,
        [
            Decide(link, DataSyncPlanResolution.Create),
            new DataSyncPlanDecision(held.ItemId, DataSyncPlanResolution.Skip, null, null, [], held.ReviewToken),
        ], strict: true);
        AssertErrors(result, (link.ItemId, DataSyncDecisionErrorCode.ResolutionNotAllowed),
            (held.ItemId, DataSyncDecisionErrorCode.ResolutionNotAllowed));
    }

    [TestMethod]
    public void TheTargetRule()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Local("13", 5, T("Tags"));
        f.Local("14", 6, T("Other"));
        f.Pull(1, T("Genre", ("a", "Drama")));
        f.Pull(2, T("Tags", ("x", "One")));
        f.Pull(3, T("New"));
        var plan = f.Plan();
        var update = Item(plan, 1);
        var link = Item(plan, 2);
        var create = Item(plan, 3);

        void Refused(DataSyncPlanDecision decision)
        {
            var others = new[] { update, link, create }.Where(i => i.ItemId != decision.ItemId).Select(i => Decide(i));
            var result = f.Resolve(plan, [decision, .. others], strict: true);
            AssertErrors(result, (decision.ItemId, DataSyncDecisionErrorCode.TargetNotAllowed));
        }

        Refused(new DataSyncPlanDecision(update.ItemId, DataSyncPlanResolution.Update, null, null, [], update.ReviewToken));
        Refused(new DataSyncPlanDecision(update.ItemId, DataSyncPlanResolution.Update, "13", null, [], update.ReviewToken));
        Refused(new DataSyncPlanDecision(link.ItemId, DataSyncPlanResolution.Link, "14", null, [], link.ReviewToken));
        Refused(new DataSyncPlanDecision(link.ItemId, DataSyncPlanResolution.Skip, "13", null, [], link.ReviewToken));
        Refused(new DataSyncPlanDecision(create.ItemId, DataSyncPlanResolution.Create, "12", null, [], create.ReviewToken));

        // A default made explicit always passes.
        var defaults = f.Resolve(plan, [Decide(update), Decide(link), Decide(create)], strict: true);
        Assert.AreEqual(0, defaults.Errors.Count);
    }

    [TestMethod]
    public void ALocalTargetIsUsedOnce()
    {
        var f = new PlanFixture();
        f.Local("12", 8, T("Genre"));
        f.Local("13", 9, T("Genre"));
        f.Pull(1, T("Genre", ("a", "A")));
        f.Pull(2, T("Genre", ("b", "B")));
        f.Pull(3, T("Tags"));
        var plan = f.Plan();
        var first = Item(plan, 1);
        var second = Item(plan, 2);
        Assert.AreEqual(DataSyncPlanItemReason.AmbiguousNameMatch, first.Reason);

        var result = f.Resolve(plan,
            [Decide(first, DataSyncPlanResolution.Link, "12"), Decide(second, DataSyncPlanResolution.Link, "12")],
            strict: true);
        AssertErrors(result, (first.ItemId, DataSyncDecisionErrorCode.TargetUsedTwice),
            (second.ItemId, DataSyncDecisionErrorCode.TargetUsedTwice));

        var apart = f.Resolve(plan,
            [Decide(first, DataSyncPlanResolution.Link, "12"), Decide(second, DataSyncPlanResolution.Link, "13")],
            strict: true);
        Assert.AreEqual(0, apart.Errors.Count);
    }

    [TestMethod]
    public void TheTokenIsTheChosenCandidatesOrTheItems()
    {
        var f = LinkFixture();
        var plan = f.Plan();
        var link = Item(plan, 1);

        var withItemToken = f.Resolve(plan, [Decide(link, DataSyncPlanResolution.Link, "12", token: link.ReviewToken)],
            strict: true);
        AssertErrors(withItemToken, (link.ItemId, DataSyncDecisionErrorCode.ChangedSinceReview));

        var separate = f.Resolve(plan,
            [Decide(link, DataSyncPlanResolution.CreateSeparate, token: link.Candidates[0].ReviewToken)], strict: true);
        AssertErrors(separate, (link.ItemId, DataSyncDecisionErrorCode.ChangedSinceReview));

        Assert.AreEqual(0, f.Resolve(plan, [Decide(link)], strict: true).Errors.Count);
        Assert.AreEqual(0, f.Resolve(plan, [Decide(link, DataSyncPlanResolution.CreateSeparate)], strict: true).Errors.Count);
    }

    [TestMethod]
    public void ExcludedChangesMustExist()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Pull(1, T("Genres", ("a", "Action"), ("b", "Drama")));
        var plan = f.Plan();
        var item = Item(plan, 1);

        AssertErrors(f.Resolve(plan, [Decide(item, excluded: ["child:add:zzz"])], strict: true),
            (item.ItemId, DataSyncDecisionErrorCode.UnknownChange));
        AssertErrors(f.Resolve(plan, [Decide(item, excluded: ["bogus:*"])], strict: true),
            (item.ItemId, DataSyncDecisionErrorCode.UnknownChange));
        Assert.AreEqual(0, f.Resolve(plan, [Decide(item, excluded: ["tag:add:*", "name"])], strict: true).Errors.Count,
            "a v3.1 group with no change of this item expands to nothing");
    }

    [TestMethod]
    public void ASeparateNameIsOneToMaxLengthCharactersWithoutNulOrALoneSurrogate()
    {
        var f = LinkFixture();
        var plan = f.Plan();
        var link = Item(plan, 1);

        foreach (var name in new[] { "", new string('n', 257), "a\0b", "\ud800x", "x\udc00" })
        {
            var result = f.Resolve(plan, [Decide(link, DataSyncPlanResolution.CreateSeparate, newName: name)], strict: true);
            AssertErrors(result, (link.ItemId, DataSyncDecisionErrorCode.InvalidName));
        }

        foreach (var name in new[] { "Genre (NAS)", new string('n', 256), "tab\tand\nnewline", "😀" })
            Assert.AreEqual(0,
                f.Resolve(plan, [Decide(link, DataSyncPlanResolution.CreateSeparate, newName: name)], strict: true).Errors.Count);
    }

    [TestMethod]
    public void UnknownAndDuplicateDecisionsAreRefused()
    {
        var f = LinkFixture();
        var plan = f.Plan();
        var link = Item(plan, 1);

        var result = f.Resolve(plan,
        [
            Decide(link), Decide(link, DataSyncPlanResolution.Skip),
            new DataSyncPlanDecision("testItem/k/nope", DataSyncPlanResolution.Skip, null, null, [], "t"),
        ], strict: true);
        AssertErrors(result, (link.ItemId, DataSyncDecisionErrorCode.DuplicateDecision),
            ("testItem/k/nope", DataSyncDecisionErrorCode.UnknownItem));
    }

    [TestMethod]
    public void HeldItemsNeedNoDecisionAndUnchangedAcceptsUpdateOrSkip()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.Pull(1, T("Genre"), aliases: [7]);
        f.PullHeldAtSource(2);
        var plan = f.Plan();
        var unchanged = Item(plan, 1);

        var byDefault = f.Resolve(plan, [], strict: true);
        Assert.AreEqual(0, byDefault.Errors.Count);
        Assert.AreEqual(DataSyncItemOutcome.Held, Resolved(byDefault, 2).Outcome);
        Assert.AreEqual(DataSyncItemAction.KeysRecorded, Resolved(byDefault, 1).Action);

        var update = f.Resolve(plan, [Decide(unchanged, DataSyncPlanResolution.Update)], strict: true);
        Assert.AreEqual(DataSyncItemAction.KeysRecorded, Resolved(update, 1).Action);
        var skip = f.Resolve(plan, [Decide(unchanged, DataSyncPlanResolution.Skip)], strict: true);
        Assert.AreEqual(DataSyncItemOutcome.SkippedByUser, Resolved(skip, 1).Outcome);
    }

    // ---- non-strict ------------------------------------------------------------------------------------

    [TestMethod]
    public void InsideTheTaskEveryProblemIsThatItemsOwnAndTheRestResolve()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Local("20", 20, T("Twin"));
        f.Local("21", 21, T("Twin"));
        f.Pull(1, T("Genres", ("a", "Action")));      // Update
        f.Pull(2, T("Twin", ("x", "X")));             // ambiguous
        f.Pull(3, T("Twin", ("y", "Y")));             // ambiguous
        f.Pull(4, T("Brand new"));                     // Create
        f.Pull(5, T("Also new"));                      // Create
        f.Pull(6, T("Third new"));                     // Create
        f.Pull(7, T("Fourth new"));                    // Create
        f.PullHeldAtSource(8);
        var plan = f.Plan();
        var i = (int key) => Item(plan, key);

        var result = f.Resolve(plan,
        [
            Decide(i(1), token: "stale"),                                               // token mismatch
            Decide(i(2), DataSyncPlanResolution.Link, "20"),                            // used twice
            Decide(i(3), DataSyncPlanResolution.Link, "20"),                            // used twice
            Decide(i(4), DataSyncPlanResolution.Update, "12", token: i(4).ReviewToken), // not allowed
            Decide(i(5), excluded: ["nope"]),                                           // unknown change
            Decide(i(6), DataSyncPlanResolution.Create, "12"),                          // target not allowed
            new DataSyncPlanDecision(i(8).ItemId, DataSyncPlanResolution.Skip, null, null, [], "x"),
            new DataSyncPlanDecision("testItem/k/unknown", DataSyncPlanResolution.Skip, null, null, [], "x"),
            Decide(i(1), DataSyncPlanResolution.Skip),                                  // duplicate: ignored
        ], strict: false);

        Assert.AreEqual(0, result.Errors.Count, "non-strict Resolve is total");
        AssertUnapplied(result, 1, "tokenMismatch");
        AssertUnapplied(result, 2, "targetUsedTwice");
        AssertUnapplied(result, 3, "targetUsedTwice");
        AssertUnapplied(result, 4, "resolutionNotAllowed");
        AssertUnapplied(result, 5, "unknownChange");
        AssertUnapplied(result, 6, "targetNotAllowed");
        AssertUnapplied(result, 7, "decisionMissing");
        Assert.AreEqual(DataSyncItemOutcome.Held, Resolved(result, 8).Outcome, "held whatever was sent");
        Assert.AreEqual(8, result.Items.Count);

        var invalidName = f.Resolve(plan, [Decide(i(2), DataSyncPlanResolution.CreateSeparate, newName: "")], strict: false);
        AssertUnapplied(invalidName, 2, "invalidName");
    }

    [TestMethod]
    public void NonStrictResolveIsTotalForAnyDecisions()
    {
        var f = PlannerTests.RichFixture();
        var input = f.Input();
        var plan = DataSyncPlanner.Plan(input);
        var items = plan.Kinds.SelectMany(s => s.Items).ToList();
        var localKeys = items.SelectMany(i => i.Candidates.Select(c => c.LocalKey).Append(i.Local?.LocalKey))
            .Append("999").Append(null).Distinct().ToList();
        var tokens = items.SelectMany(i => i.Candidates.Select(c => c.ReviewToken).Append(i.ReviewToken)).Append("x").ToList();
        var random = new Random(20260925);

        for (var round = 0; round < 300; round++)
        {
            var decisions = new List<DataSyncPlanDecision>();
            for (var n = random.Next(items.Count + 3); n > 0; n--)
            {
                var itemId = random.Next(10) == 0 ? "testItem/k/unknown" : items[random.Next(items.Count)].ItemId;
                var excluded = random.Next(3) switch
                {
                    0 => (IReadOnlyList<string>)[],
                    1 => ["name", "child:add:*"],
                    _ => ["nope"],
                };
                decisions.Add(new DataSyncPlanDecision(itemId,
                    (DataSyncPlanResolution)random.Next(1, 6), localKeys[random.Next(localKeys.Count)],
                    random.Next(4) == 0 ? "" : random.Next(2) == 0 ? "Separate" : null, excluded,
                    tokens[random.Next(tokens.Count)]));
            }

            var loose = DataSyncPlanner.Resolve(plan, input, decisions, strict: false);
            Assert.AreEqual(0, loose.Errors.Count);
            CollectionAssert.AreEqual(items.Select(i => i.ItemId).ToList(), loose.Items.Select(i => i.ItemId).ToList());
            foreach (var resolved in loose.Items)
            {
                Assert.AreEqual(resolved.Outcome is DataSyncItemOutcome.Applied, resolved.Operation is not null,
                    resolved.ItemId);
                if (resolved.Outcome == DataSyncItemOutcome.ChangedSinceReview) Assert.IsNotNull(resolved.Detail);
            }

            var strict = DataSyncPlanner.Resolve(plan, input, decisions, strict: true);
            Assert.IsTrue(strict.Errors.Count == 0 || strict.Items.Count == 0);
            CollectionAssert.AreEqual(strict.Errors.ToArray(), DataSyncPlanner.ValidateDecisions(plan, decisions).ToArray());
        }
    }

    [TestMethod]
    public void AnItemThatChangedSinceTheReviewIsNotWrittenInsideTheTask()
    {
        // P: 1 is a Create, 2 is Unchanged, 3 an Update.
        var f = new PlanFixture();
        var same = f.Local("12", 2, T("Same", ("a", "Action")));
        f.Local("13", 3, T("Edited"));
        f.Pull(1, T("Genre"));
        f.Pull(2, T("Same", ("a", "Action")));
        f.Pull(3, T("Edited!"));
        var reviewed = f.Plan();
        var decisions = DataSyncPlanner.CompleteDecisions(reviewed, []);
        Assert.AreEqual(3, decisions.Count);

        // Meanwhile: someone creates "Genre" here (1 becomes a Link) and edits 12 (2 becomes an Update).
        f.Local("14", 9, T("Genre"));
        f.Entities[ItemKind][0] = same with { Content = T("Same", ("a", "Adventure")) };
        var current = f.Plan();
        Assert.AreEqual(DataSyncPlanItemType.Link, Item(current, 1).Type);
        Assert.AreEqual(DataSyncPlanItemType.Update, Item(current, 2).Type);

        var result = f.Resolve(current, decisions, strict: false);
        AssertUnapplied(result, 1, "resolutionNotAllowed");
        AssertUnapplied(result, 2, "tokenMismatch");
        Assert.AreEqual(DataSyncItemAction.Updated, Resolved(result, 3).Action, "nothing else changes");
    }

    // ---- CompleteDecisions ------------------------------------------------------------------------

    [TestMethod]
    public void CompleteDecisionsGivesEveryDecidableItemExactlyOneDecision()
    {
        var f = PlannerTests.RichFixture();
        var plan = f.Plan();
        var items = plan.Kinds.SelectMany(s => s.Items).ToList();
        var own = items.Where(i => i.RequiresConfirmation && i.AllowedResolutions.Contains(DataSyncPlanResolution.Skip))
            .Select(i => Decide(i, DataSyncPlanResolution.Skip)).ToList();
        Assert.AreEqual(0, f.Resolve(plan, own, strict: true).Errors.Count);

        var complete = DataSyncPlanner.CompleteDecisions(plan, own);
        var decidable = items.Where(i => i.Type != DataSyncPlanItemType.Held).Select(i => i.ItemId).ToList();
        CollectionAssert.AreEqual(decidable, complete.Select(d => d.ItemId).ToList(), "plan order, none for held items");
        foreach (var decision in own) Assert.IsTrue(complete.Contains(decision), "the caller's own decision is kept");

        var autoLink = Item(plan, 31, GroupKind);
        var link = complete.Single(d => d.ItemId == autoLink.ItemId);
        Assert.AreEqual(DataSyncPlanResolution.Link, link.Resolution);
        Assert.AreEqual(autoLink.Candidates.Single().ReviewToken, link.ReviewToken, "the default target's token");
        var unchanged = complete.Single(d => d.ItemId == Item(plan, 2).ItemId);
        Assert.AreEqual(DataSyncPlanResolution.Update, unchanged.Resolution);
        Assert.AreEqual(Item(plan, 2).ReviewToken, unchanged.ReviewToken);
        Assert.AreEqual(0, unchanged.ExcludedChangeIds.Count);
        Assert.IsNull(unchanged.NewName);

        var result = f.Resolve(plan, complete, strict: false);
        Assert.IsFalse(result.Items.Any(r => r.Outcome == DataSyncItemOutcome.ChangedSinceReview));
        Assert.AreEqual(0, f.Resolve(plan, complete, strict: true).Errors.Count);
    }

    [TestMethod]
    public void AnUnconfirmedItemIsNeverDefaultedByCompleteDecisions()
    {
        var f = LinkFixture();
        var plan = f.Plan();
        Assert.AreEqual(0, DataSyncPlanner.CompleteDecisions(plan, []).Count);
    }

    // ---- operations ----------------------------------------------------------------------------------

    [TestMethod]
    public void ACreateTakesTheIncomingKeysOriginPositionAndContent()
    {
        var f = new PlanFixture();
        f.Pull(1, T("Genre", ("a", "Action")), aliases: [2]);
        var plan = f.Plan();

        var resolved = Resolved(f.Resolve(plan, [], strict: true), 1);
        var op = (CreateEntityOperation)resolved.Operation!;
        CollectionAssert.AreEqual(new[] { K(1), K(2) }, op.Keys.All.ToArray());
        Assert.AreEqual(PeerNode, op.OriginNodeId);
        Assert.AreEqual(Item(plan, 1).Incoming.Position, op.IncomingPosition);
        Assert.AreEqual(CanonicalJson.Serialize(TestItemCodec.Instance.Write(T("Genre", ("a", "Action")))),
            CanonicalJson.Serialize(op.Content));
        Assert.AreEqual("a", resolved.ChildMap!["a"]);
        Assert.IsNull(resolved.TargetLocalKey);
    }

    [TestMethod]
    public void ASeparateCreateKeepsTheIncomingKeysOnlyWhenNoneIsBoundHere()
    {
        var f = new PlanFixture();
        f.Local("12", 5, T("Genre"));
        f.Local("13", 2, Typed("Tags", "Tags"));
        f.Pull(1, T("Genre", ("a", "Action")));
        f.Pull(2, Typed("Tags", "Choice"));
        var plan = f.Plan();

        var result = f.Resolve(plan,
        [
            Decide(Item(plan, 1), DataSyncPlanResolution.CreateSeparate, newName: "Genre (NAS)"),
            Decide(Item(plan, 2), DataSyncPlanResolution.CreateSeparate),
        ], strict: true);
        var linked = (CreateEntityOperation)Resolved(result, 1).Operation!;
        CollectionAssert.AreEqual(new[] { K(1) }, linked.Keys.All.ToArray());
        Assert.AreEqual("Genre (NAS)", (string?)linked.Content["name"]);
        Assert.AreEqual(DataSyncItemAction.Created, Resolved(result, 1).Action);

        var mismatch = (CreateEntityOperation)Resolved(result, 2).Operation!;
        Assert.AreSame(EntityKeys.None, mismatch.Keys, "key 2 is bound here: a fresh key");
        Assert.AreEqual("Tags", (string?)mismatch.Content["name"], "no new name keeps the incoming one");
    }

    [TestMethod]
    public void AnUpdateWritesTheAcceptedChangesAndRecordsNewKeys()
    {
        var f = new PlanFixture();
        var local = f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Pull(1, T("Genres", ("a", "Action"), ("b", "Drama")), aliases: [7]);
        var plan = f.Plan();

        var resolved = Resolved(f.Resolve(plan, [Decide(Item(plan, 1), excluded: ["name"])], strict: true), 1);
        Assert.AreEqual(DataSyncItemAction.Updated, resolved.Action);
        var op = (UpdateEntityOperation)resolved.Operation!;
        Assert.AreEqual("12", op.LocalKey);
        Assert.AreEqual(local.LocalHash, op.ExpectedLocalHash);
        CollectionAssert.AreEqual(new[] { K(7) }, op.AliasKeysToAdd.All.ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, op.AddedChildIds.ToArray());
        Assert.AreEqual(0, op.RemovedChildIds.Count, "a review never removes");
        Assert.AreEqual(T("Genre", ("a", "Action"), ("b", "Drama")), TestItemCodec.Instance.ReadLocal(op.MergedContent));
        Assert.AreEqual("12", resolved.TargetLocalKey);
        Assert.AreEqual("a", resolved.ChildMap!["a"]);
        Assert.AreEqual("b", resolved.ChildMap["b"]);
    }

    [TestMethod]
    public void AGroupExclusionReachesBeyondTheInlineChanges()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        var children = Enumerable.Range(0, 300).Select(i => i.ToString(CultureInfo.InvariantCulture))
            .Select(i => ("c" + i, "L" + i)).ToArray();
        f.Pull(1, T("Genres", children));
        var plan = f.Plan();
        Assert.AreEqual(301, Item(plan, 1).Changes.Count);
        Assert.AreEqual(DataSyncPlanView.MaxInline, Item(DataSyncPlanView.Truncate(plan), 1).Changes.Count);

        var op = (UpdateEntityOperation)Resolved(
            f.Resolve(plan, [Decide(Item(plan, 1), excluded: ["child:add:*"])], strict: true), 1).Operation!;
        Assert.AreEqual(T("Genres"), TestItemCodec.Instance.ReadLocal(op.MergedContent), "no add of the 300 was accepted");
    }

    [TestMethod]
    public void ExcludingAnAddExcludesEverythingThatDependsOnIt()
    {
        var codec = new DecoratedCodec(TestItemCodec.Instance)
        {
            // Children named "p/…" depend on "p": like multilevel nodes under an added parent.
            OnDiff = d => d with
            {
                Changes = d.Changes.Select(c => c.ChangeId.StartsWith("child:add:", StringComparison.Ordinal) &&
                                                c.ChangeId.LastIndexOf('/') is var slash and > 0
                    ? c with { DependsOnChangeId = c.ChangeId[..slash] }
                    : c).ToList(),
            },
        };
        var f = new PlanFixture();
        f.Codecs[ItemKind] = codec;
        f.Local("12", 1, T("Genre"));
        f.Pull(1, T("Genre", ("p", "Parent"), ("p/c", "Child"), ("p/c/g", "Grandchild"), ("q", "Other")));
        var plan = f.Plan();

        f.Resolve(plan, [Decide(Item(plan, 1), excluded: ["child:add:p"])], strict: true);
        CollectionAssert.AreEquivalent(new[] { "child:add:q" }, codec.Accepted.Last().ToArray());
    }

    [TestMethod]
    public void BindOnlyWhenOnlyKeysAreNewAndNoChangeWhenNothingIs()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.Local("13", 2, T("Tags"));
        f.Local("14", 3, T("Mood", ("a", "Calm")));
        f.Local("15", 4, T("Other", ("a", "One")));
        f.Pull(1, T("Genre"));                        // Unchanged, keys known
        f.Pull(2, T("Tags"), aliases: [9]);           // Unchanged, a new key
        f.Pull(3, T("Mood", ("a", "Calmer")));        // Update, all unticked, keys known
        f.Pull(4, T("Other", ("a", "Two")), aliases: [10]); // Update, all unticked, a new key
        f.Pull(5, T("Linked"));
        f.Local("16", 6, T("Linked"));
        var plan = f.Plan();

        var result = f.Resolve(plan,
        [
            Decide(Item(plan, 3), excluded: ["child:rename:a"]),
            Decide(Item(plan, 4), excluded: ["child:rename:*"]),
            Decide(Item(plan, 5)),
        ], strict: true);
        Assert.AreEqual(0, result.Errors.Count);

        Assert.AreEqual(DataSyncItemOutcome.NoChange, Resolved(result, 1).Outcome);
        Assert.AreEqual(DataSyncItemAction.None, Resolved(result, 1).Action);
        Assert.IsNull(Resolved(result, 1).Operation);
        Assert.AreEqual("12", Resolved(result, 1).TargetLocalKey, "the base still needs its target");

        Assert.IsInstanceOfType<BindOnlyOperation>(Resolved(result, 2).Operation);
        Assert.AreEqual(DataSyncItemAction.KeysRecorded, Resolved(result, 2).Action);

        Assert.AreEqual(DataSyncItemOutcome.NoChange, Resolved(result, 3).Outcome);
        Assert.IsInstanceOfType<BindOnlyOperation>(Resolved(result, 4).Operation);
        Assert.AreEqual(DataSyncItemAction.KeysRecorded, Resolved(result, 4).Action);

        var link = Resolved(result, 5);
        Assert.IsInstanceOfType<BindOnlyOperation>(link.Operation, "an identical link writes no content");
        Assert.AreEqual(DataSyncItemAction.Linked, link.Action);
        CollectionAssert.AreEqual(new[] { K(5) }, ((BindOnlyOperation)link.Operation!).AliasKeysToAdd.All.ToArray());
    }

    [TestMethod]
    public void AnUpdateOfOneIdentityConflictCandidateNeverOffersTheOthersKeys()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.Local("13", 2, T("Genre"));
        f.Pull(1, T("Genre"), aliases: [2, 3]);
        var plan = f.Plan();
        var item = Item(plan, 1);
        Assert.AreEqual(DataSyncPlanItemReason.IdentityConflict, item.Reason);
        Assert.IsTrue(item.Candidates.All(c => c.RecordsNewKeys), "key 3 is new for either");

        var result = f.Resolve(plan, [Decide(item, DataSyncPlanResolution.Update, "12")], strict: true);
        var op = (BindOnlyOperation)Resolved(result, 1).Operation!;
        CollectionAssert.AreEqual(new[] { K(3) }, op.AliasKeysToAdd.All.ToArray(), "key 2 stays with 13");
    }

    [TestMethod]
    public void AKeyOfARowKeptOutOfSyncIsNeverOffered()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.Local("13", 2, T("Genre"), state: DataSyncEntitySyncState.LocalOnly);
        f.Pull(1, T("Genre"), aliases: [2]);
        var plan = f.Plan();

        var resolved = Resolved(f.Resolve(plan, [], strict: true), 1);
        Assert.AreEqual(DataSyncItemOutcome.NoChange, resolved.Outcome);
    }

    [TestMethod]
    public void ANewerLocalVersionIsNeverOverwritten()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre (edited)", ("a", "Action")), vv: Vv((Peer, 1), (Self, 1)));
        f.Pull(1, T("Genre", ("a", "Drama"), ("b", "New")), vv: Vv((Peer, 1)), aliases: [4]);
        var plan = f.Plan();

        var resolved = Resolved(f.Resolve(plan, [], strict: true), 1);
        Assert.IsInstanceOfType<BindOnlyOperation>(resolved.Operation, "keys only, never content");
        Assert.AreEqual("a", resolved.ChildMap!["a"], "the base still maps the peer's children");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>One Link item (1 → local 12) that needs confirmation.</summary>
    private static PlanFixture LinkFixture() => new PlanFixture().With(f =>
    {
        f.Local("12", 5, T("Genre"));
        f.Pull(1, T("Genre", ("a", "Action")));
    });

    private static ResolvedItem Resolved(ResolveResult result, int key, string kind = ItemKind) =>
        result.Items.Single(i => i.ItemId == $"{kind}/k/{Hex(key)}");

    private static void AssertUnapplied(ResolveResult result, int key, string detail)
    {
        var item = Resolved(result, key);
        Assert.AreEqual(DataSyncItemOutcome.ChangedSinceReview, item.Outcome, item.ItemId);
        Assert.AreEqual(detail, item.Detail, item.ItemId);
        Assert.IsNull(item.Operation);
        Assert.AreEqual(DataSyncItemAction.None, item.Action);
    }

    private static void AssertErrors(ResolveResult result, params (string ItemId, DataSyncDecisionErrorCode Code)[] expected)
    {
        Assert.AreEqual(0, result.Items.Count);
        CollectionAssert.AreEqual(expected.Select(e => new DataSyncDecisionError(e.ItemId, e.Code)).ToArray(),
            result.Errors.ToArray());
    }
}
