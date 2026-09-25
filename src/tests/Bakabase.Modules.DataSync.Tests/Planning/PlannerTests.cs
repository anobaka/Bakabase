using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Planning.PlanFixture;

namespace Bakabase.Modules.DataSync.Tests.Planning;

/// <summary>The first-contact planner: v3.1 §7.2/§7.3 row by row, §7.5's tokens and order, and §8.3's refinements.</summary>
[TestClass]
public class PlannerTests
{
    // ---- v3.1 §7.3, one case per row -------------------------------------------------------------

    [TestMethod]
    public void AnUnknownKeyWithoutANameMatchIsACreate()
    {
        var f = new PlanFixture();
        f.Local("12", 2, T("Tags"));
        f.Pull(1, T("Genre", ("a", "Action")));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Create, item.Type);
        Assert.IsNull(item.Reason);
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
        Assert.AreEqual(DataSyncPlanResolution.Create, item.DefaultResolution);
        Assert.IsNull(item.DefaultTargetLocalKey);
        Assert.IsFalse(item.RequiresConfirmation);
        Assert.IsFalse(item.OffersSeparateName);
        Assert.IsFalse(item.RecordsNewKeys);
        Assert.IsNull(item.Local);
        Assert.AreEqual(0, item.Candidates.Count);
        Assert.AreEqual(0, item.Changes.Count);
        Assert.AreEqual($"{ItemKind}/k/{Hex(1)}", item.ItemId);
        Assert.AreEqual(new DataSyncPlanEntity(null, "Genre", null, 0, 1), item.Incoming);
    }

    [TestMethod]
    public void AKeyMatchWithChangesIsAnUpdate()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Pull(1, T("Genres", ("a", "Action"), ("b", "Drama")));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Update, item.Type);
        Assert.AreEqual("12", item.Local!.LocalKey);
        Assert.AreEqual("12", item.DefaultTargetLocalKey);
        Assert.AreEqual(DataSyncPlanResolution.Update, item.DefaultResolution);
        Assert.IsFalse(item.RequiresConfirmation);
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
        CollectionAssert.AreEqual(new[] { "name", "child:add:b" }, item.Changes.Select(c => c.ChangeId).ToArray());
        Assert.AreEqual(new DataSyncChangeCounts(2, 1, 1, 0, 0), item.ChangeCounts);
        Assert.AreEqual(1, item.UnchangedChildren);
        Assert.IsFalse(item.RecordsNewKeys);
    }

    [TestMethod]
    public void AKeyMatchWithoutChangesIsUnchangedAndDefaultsToUpdate()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Pull(1, T("Genre", ("a", "Action")));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Unchanged, item.Type);
        Assert.IsNull(item.Reason);
        Assert.AreEqual(DataSyncPlanResolution.Update, item.DefaultResolution);
        Assert.AreEqual("12", item.DefaultTargetLocalKey);
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
        Assert.AreEqual(0, item.Changes.Count);
    }

    [TestMethod]
    public void AnExactNameMatchIsALinkThatAsksAndMayBeConfirmedInBulk()
    {
        var f = new PlanFixture();
        f.Local("12", 2, T("Genre", ("x", "Action")));
        f.Pull(1, T("Genre", ("a", "Action"), ("b", "Drama")));

        var plan = f.Plan();
        var item = Item(plan, 1);
        Assert.AreEqual(DataSyncPlanItemType.Link, item.Type);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsTrue(item.BulkLinkEligible);
        Assert.IsTrue(item.OffersSeparateName);
        Assert.AreEqual(DataSyncPlanResolution.Link, item.DefaultResolution);
        Assert.AreEqual("12", item.DefaultTargetLocalKey);
        CollectionAssert.AreEqual(
            new[] { DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
        Assert.IsFalse(item.RecordsNewKeys, "items without Local carry the flag on their candidates");

        var candidate = item.Candidates.Single();
        Assert.AreEqual("12", candidate.LocalKey);
        Assert.AreEqual(DataSyncNaturalMatch.Exact, candidate.Match);
        Assert.IsTrue(candidate.RecordsNewKeys, "a link always records the incoming keys");
        CollectionAssert.AreEqual(new[] { "child:add:b" }, candidate.Changes.Select(c => c.ChangeId).ToArray());
        Assert.AreEqual(1, candidate.UnchangedChildren);
        Assert.AreNotEqual(item.ReviewToken, candidate.ReviewToken);

        Assert.AreEqual(1, plan.Summary.BulkLinkEligibleCount);
        Assert.AreEqual(1, plan.Summary.PendingCount);
    }

    [TestMethod]
    public void ASimilarNameMatchIsALinkThatIsNotBulkEligible()
    {
        var f = new PlanFixture();
        f.Local("12", 2, T("genre"));
        f.Pull(1, T(" Genre"));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Link, item.Type);
        Assert.AreEqual(DataSyncNaturalMatch.Similar, item.Candidates.Single().Match);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsFalse(item.BulkLinkEligible);
    }

    [TestMethod]
    public void AnIdenticalExtensionGroupLinksWithoutAskingButAnIdenticalItemStillAsks()
    {
        var f = new PlanFixture();
        f.Local("3", 2, G("Images", ".jpg", ".png"), GroupKind);
        f.Pull(1, G("Images", ".jpg", ".png"), GroupKind);
        f.Local("12", 4, T("Genre", ("x", "Action")));
        f.Pull(3, T("Genre", ("a", "Action")));

        var plan = f.Plan();
        var group = Item(plan, 1, GroupKind);
        Assert.AreEqual(DataSyncPlanItemType.Link, group.Type);
        Assert.AreEqual(DataSyncNaturalMatch.Identical, group.Candidates.Single().Match);
        Assert.IsFalse(group.RequiresConfirmation, "the D09 exception");
        Assert.IsFalse(group.BulkLinkEligible, "nothing to confirm");
        Assert.AreEqual("3", group.DefaultTargetLocalKey);

        var item = Item(plan, 3);
        Assert.AreEqual(DataSyncNaturalMatch.Identical, item.Candidates.Single().Match);
        Assert.IsTrue(item.RequiresConfirmation, "only kinds that auto-link identical matches skip confirmation");
        Assert.IsTrue(item.BulkLinkEligible);
    }

    [TestMethod]
    public void SeveralBestCandidatesAreAnAmbiguousNameMatchSortedByLevelThenLocalKey()
    {
        var f = new PlanFixture();
        f.Local("10", 2, T("Genre", ("x", "A")));
        f.Local("9", 3, T("Genre", ("y", "B")));
        f.Local("11", 4, T("genre"));
        f.Pull(1, T("Genre"));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.NeedsDecision, item.Type);
        Assert.AreEqual(DataSyncPlanItemReason.AmbiguousNameMatch, item.Reason);
        CollectionAssert.AreEqual(new[] { "9", "10", "11" }, item.Candidates.Select(c => c.LocalKey).ToArray(),
            "level descending, then local key numerically");
        Assert.IsNull(item.DefaultResolution);
        Assert.IsNull(item.DefaultTargetLocalKey);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsFalse(item.BulkLinkEligible);
        CollectionAssert.AreEqual(
            new[] { DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
    }

    [TestMethod]
    public void AKeyBoundEntityOfAnotherSubtypeIsATypeMismatch()
    {
        var f = new PlanFixture();
        f.Local("12", 1, Typed("Genre", "Tags"));
        f.Pull(1, Typed("Genre", "Choice"));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.NeedsDecision, item.Type);
        Assert.AreEqual(DataSyncPlanItemReason.TypeMismatch, item.Reason);
        Assert.AreEqual("12", item.Local!.LocalKey);
        Assert.AreEqual("Tags", item.Local.Subtype);
        Assert.AreEqual("Choice", item.Incoming.Subtype);
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.Skip, DataSyncPlanResolution.CreateSeparate },
            item.AllowedResolutions.ToArray());
        Assert.IsNull(item.DefaultResolution);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsTrue(item.OffersSeparateName);
        Assert.AreEqual(0, item.Changes.Count, "a type difference is never converted by a review");
    }

    [TestMethod]
    public void TheSameNameWithAnotherSubtypeIsANameClash()
    {
        var f = new PlanFixture();
        f.Local("12", 2, Typed("Genre", "Tags"));
        f.Pull(1, Typed("genre", "Choice"));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemReason.NameClashDifferentType, item.Reason);
        Assert.AreEqual(0, item.Candidates.Count);
        Assert.IsNull(item.Local);
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
    }

    [TestMethod]
    public void KeysOfTwoLocalEntitiesAreAnIdentityConflict()
    {
        var f = new PlanFixture();
        f.Local("13", 1, T("Genre"));
        f.Local("12", 2, T("Genres"));
        f.Local("14", 3, Typed("Genre", "Choice"));
        f.Pull(1, T("Genre"), aliases: [2, 3]);

        var plan = f.Plan();
        var item = Item(plan, 1);
        Assert.AreEqual(DataSyncPlanItemReason.IdentityConflict, item.Reason);
        CollectionAssert.AreEqual(new[] { "13", "12" }, item.Candidates.Select(c => c.LocalKey).ToArray(),
            "same-subtype candidates only, by level then local key");
        CollectionAssert.AreEqual(new[] { DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());
        Assert.IsNull(item.DefaultResolution);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.AreEqual(0, Section(plan).LocalOnlyCount, "every key match is claimed");
    }

    [TestMethod]
    public void TwoRecordsBoundToOneLocalEntityAreBothIdentityConflicts()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"), aliases: [2]);
        f.Pull(1, T("Genre"));
        f.Pull(2, T("Genre (2)"));

        var plan = f.Plan();
        foreach (var key in new[] { 1, 2 })
        {
            var item = Item(plan, key);
            Assert.AreEqual(DataSyncPlanItemReason.IdentityConflict, item.Reason);
            Assert.AreEqual("12", item.Candidates.Single().LocalKey);
        }
    }

    [TestMethod]
    public void TwoIncomingEntitiesWantingOneCandidateAreBothDuplicates()
    {
        var f = new PlanFixture();
        f.Local("12", 9, T("Genre"));
        f.Pull(1, T("Genre"));
        f.Pull(2, T("genre"));

        var plan = f.Plan();
        foreach (var key in new[] { 1, 2 })
        {
            var item = Item(plan, key);
            Assert.AreEqual(DataSyncPlanItemReason.DuplicateInPackage, item.Reason);
            Assert.AreEqual("12", item.Candidates.Single().LocalKey);
            Assert.IsNull(item.DefaultResolution);
            Assert.IsFalse(item.BulkLinkEligible);
        }

        Assert.AreEqual(0, Section(plan).LocalOnlyCount, "a shared unique best is not local-only");
    }

    [TestMethod]
    public void HeldEntitiesAndKindsNeedNoDecision()
    {
        var f = new PlanFixture();
        f.PullHeldAtSource(1);
        f.PullRaw(2, new JsonObject { ["name"] = "" }, ItemKind);
        f.Pull(3, G("Images", ".jpg"), GroupKind);
        f.KindHeld[GroupKind] = DataSyncHeldReason.NewerSchema;
        f.PullRaw(4, new JsonObject { ["name"] = "Later" }, "futureKind");
        f.Unsupported.Add("futureKind");

        var plan = f.Plan();
        AssertHeld(Item(plan, 1), DataSyncHeldReason.AtSource);
        AssertHeld(Item(plan, 2), DataSyncHeldReason.Invalid);
        AssertHeld(Item(plan, 3, GroupKind), DataSyncHeldReason.NewerSchema);
        AssertHeld(Item(plan, 4, "futureKind"), DataSyncHeldReason.UnknownKind);
        Assert.IsFalse(Section(plan, "futureKind").Supported);
        Assert.IsTrue(Section(plan, GroupKind).Supported);
        Assert.AreEqual(4, plan.Summary.HeldCount);
        Assert.AreEqual(0, plan.Summary.PendingCount);
        Assert.AreEqual("Later", Item(plan, 4, "futureKind").Incoming.Name, "a held entity still shows a readable name");

        static void AssertHeld(DataSyncPlanItem item, DataSyncHeldReason reason)
        {
            Assert.AreEqual(DataSyncPlanItemType.Held, item.Type);
            Assert.AreEqual(reason, item.HeldReason);
            Assert.AreEqual(0, item.AllowedResolutions.Count);
            Assert.IsNull(item.DefaultResolution);
            Assert.IsFalse(item.RequiresConfirmation);
        }
    }

    // ---- v3.1 §5.2 and the alias rule ------------------------------------------------------------

    [TestMethod]
    public void AKeyMatchesThroughAnAlias()
    {
        var f = new PlanFixture();
        f.Local("12", 5, T("Genre"), aliases: [1]);
        f.Pull(1, T("Genre"));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Unchanged, item.Type);
        Assert.AreEqual("12", item.Local!.LocalKey);
        Assert.IsFalse(item.RecordsNewKeys);
    }

    [TestMethod]
    public void RecordsNewKeysOnlyForAKeyNoLiveEntityOwns()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.Local("13", 8, T("Tags"));
        f.Pull(1, T("Genre"), aliases: [7]);
        f.Pull(2, T("Other"));

        var plan = f.Plan();
        Assert.IsTrue(Item(plan, 1).RecordsNewKeys, "key 7 is new here: an Update records it as an alias");

        var g = new PlanFixture();
        g.Local("12", 1, T("Genre"));
        g.Local("13", 7, T("Tags"), state: DataSyncEntitySyncState.Detached);
        g.Pull(1, T("Genre"), aliases: [7]);
        Assert.IsFalse(Item(g.Plan(), 1).RecordsNewKeys, "a key a detached row owns is never offered (§5.3)");
    }

    [TestMethod]
    public void LocalOnlyCountCountsLocalsNeitherBoundNorProposed()
    {
        var f = new PlanFixture();
        f.Local("1", 1, T("Bound"));
        f.Local("2", 2, T("Genre"));
        f.Local("3", 3, T("Other"));
        f.Local("4", 4, T("Pair"));
        f.Local("5", 5, T("Pair"));
        f.Pull(1, T("Bound"));
        f.Pull(9, T("Genre"));
        f.Pull(10, T("Pair"));

        Assert.AreEqual(3, Section(f.Plan()).LocalOnlyCount, "Other, and both ambiguous Pair candidates");
    }

    [TestMethod]
    public void PreviouslyDeletedHereViaATombstonedPrimaryOrAnAliasOfOne()
    {
        var f = new PlanFixture();
        f.Tombstone(3, aliases: [4]);
        f.Pull(3, T("A"));
        f.Pull(4, T("B"));
        f.Pull(5, T("C"));

        var plan = f.Plan();
        Assert.IsTrue(HasWarning(Item(plan, 3), DataSyncWarningCode.PreviouslyDeletedHere));
        Assert.IsTrue(HasWarning(Item(plan, 4), DataSyncWarningCode.PreviouslyDeletedHere));
        Assert.IsFalse(HasWarning(Item(plan, 5), DataSyncWarningCode.PreviouslyDeletedHere));
        Assert.AreEqual(DataSyncPlanItemType.Create, Item(plan, 3).Type);
    }

    [TestMethod]
    [DataRow("dominated", DisplayName = "the peer never saw this device's deletion")]
    [DataRow("concurrent", DisplayName = "the peer changed it after this device deleted it")]
    [DataRow("restored", DisplayName = "the peer restored it after the deletion")]
    [DataRow("undone", DisplayName = "this device undid its create")]
    public void ARecordOfSomethingDeletedHereIsNeverCreatedByDefault(string how)
    {
        // §8.3: newer local state is never regressed, and the continuous merge never revives by itself (rows T0, T2,
        // T3): whatever the vectors, a key of a tombstone here asks for an explicit Create or Skip.
        var f = new PlanFixture();
        f.Tombstone(3, vv: Vv((Peer, 1), (Self, 5)),
            tombstoneKind: how == "undone" ? DataSyncTombstoneKind.UndoneCreate : DataSyncTombstoneKind.Deleted);
        f.Pull(3, T("Revived"), vv: how switch
        {
            "concurrent" => Vv((Peer, 2)),
            "restored" => Vv((Peer, 2), (Self, 5)),
            _ => Vv((Peer, 1)),
        });

        var plan = f.Plan();
        var item = Item(plan, 3);
        Assert.AreEqual(DataSyncPlanItemType.Create, item.Type);
        Assert.IsTrue(HasWarning(item, DataSyncWarningCode.PreviouslyDeletedHere));
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsNull(item.DefaultResolution, "no default to apply with one click");
        Assert.AreEqual(1, plan.Summary.PendingCount);
        CollectionAssert.AreEquivalent(new[] { DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip },
            item.AllowedResolutions.ToArray());

        // Apply with no decision for it: nothing is created.
        Assert.AreEqual(0, DataSyncPlanner.CompleteDecisions(plan, []).Count);
        Assert.AreEqual(DataSyncDecisionErrorCode.DecisionMissing,
            f.Resolve(plan, [], strict: true).Errors.Single().Code);
        var inTask = f.Resolve(plan, DataSyncPlanner.CompleteDecisions(plan, []), strict: false).Items.Single();
        Assert.IsNull(inTask.Operation);

        // Created only when the person says so.
        var created = f.Resolve(plan, [Decide(item, DataSyncPlanResolution.Create)], strict: true).Items.Single();
        Assert.IsInstanceOfType<CreateEntityOperation>(created.Operation);
    }

    [TestMethod]
    public void ANameMatchOfSomethingDeletedHereIsNeverLinkedInBulkOrByItself()
    {
        // An identical extension group links by itself (D09), unless the record is of a group deleted here.
        var f = new PlanFixture();
        var video = G("Video", ".mkv");
        f.Local("5", 5, video, GroupKind);
        f.Tombstone(3, GroupKind);
        f.Pull(3, video, GroupKind);

        var item = Item(f.Plan(), 3, GroupKind);
        Assert.AreEqual(DataSyncPlanItemType.Link, item.Type);
        Assert.IsTrue(item.RequiresConfirmation);
        Assert.IsFalse(item.BulkLinkEligible);
    }

    [TestMethod]
    public void AReviewOfThisDevicesOwnDefinitionsSaysSo()
    {
        var f = new PlanFixture();
        f.Pull(1, T("Genre"));
        Assert.AreEqual(0, f.Plan().Warnings.Count);

        f.SourceNodeId = SelfNode;
        CollectionAssert.AreEqual(new[] { DataSyncWarningCode.FromThisDevice }, f.Plan().Warnings.Select(w => w.Code).ToArray());
    }

    [TestMethod]
    public void IncomingReadWarningsStayOnTheItem()
    {
        var f = new PlanFixture();
        f.PullRaw(1, new JsonObject { ["name"] = "Genre", ["future"] = 1 }, ItemKind);

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Create, item.Type);
        Assert.IsTrue(HasWarning(item, DataSyncWarningCode.UnknownFieldsIgnored));
        Assert.AreEqual(1, item.WarningCounts.Single(c => c.Code == DataSyncWarningCode.UnknownFieldsIgnored).Count);
    }

    // ---- v3.1 §7.5: tokens, ids, determinism, order ------------------------------------------------

    [TestMethod]
    public void TheTokenIgnoresALocalChangeTheReviewDoesNotTouch()
    {
        var f = new PlanFixture();
        var entity = f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Local("13", 2, T("Tags", ("x", "One")));
        f.Pull(1, T("Genre", ("a", "Action"), ("b", "Drama")));
        f.Pull(3, T("Tags", ("y", "Two")));
        var before = f.Plan();

        // An enhancer adds unrelated options to both targets.
        f.Entities[ItemKind][0] = entity with { Content = T("Genre", ("a", "Action"), ("z", "Zombie")) };
        f.Entities[ItemKind][1] = f.Entities[ItemKind][1] with { Content = T("Tags", ("x", "One"), ("w", "Other")) };
        var after = f.Plan();

        Assert.AreEqual(Item(before, 1).ReviewToken, Item(after, 1).ReviewToken);
        Assert.AreNotEqual(Item(before, 1).LocalOnlyChildren, Item(after, 1).LocalOnlyChildren);
        Assert.AreEqual(Item(before, 3).Candidates.Single().ReviewToken, Item(after, 3).Candidates.Single().ReviewToken);
        Assert.AreNotEqual(before.PlanId, after.PlanId, "the plan itself did change");

        // A change the review does touch changes the token.
        f.Entities[ItemKind][0] = entity with { Content = T("Genre", ("a", "Action"), ("b", "Drama")) };
        Assert.AreNotEqual(Item(before, 1).ReviewToken, Item(f.Plan(), 1).ReviewToken);
    }

    [TestMethod]
    public void TheSameInputPlansToTheSameBytes()
    {
        var f = RichFixture();
        var first = DataSyncPlanner.Plan(f.Input());
        var second = DataSyncPlanner.Plan(f.Input());

        CollectionAssert.AreEqual(DataSyncPlanFormat.CanonicalBytes(first), DataSyncPlanFormat.CanonicalBytes(second));
        Assert.AreEqual(first.PlanId, second.PlanId);
        Assert.AreEqual(16, first.PlanId.Length);
        Assert.IsTrue(first.PlanId.All(Uri.IsHexDigit));
        Assert.IsTrue(first.Kinds.SelectMany(s => s.Items).All(i => i.ReviewToken.Length == 32));
        Assert.AreEqual(DataSyncPlanFormat.PlanId(first with { PlanId = "" }), first.PlanId);

        f.Pull(40, T("Another"));
        Assert.AreNotEqual(first.PlanId, f.Plan().PlanId);
    }

    [TestMethod]
    public void ThePlanIdIgnoresInUseCounts()
    {
        var plan = new PlanFixture().With(f =>
        {
            f.Local("12", 1, T("Genre", ("a", "Action")));
            f.Pull(1, T("Genre", ("a", "Drama")));
        }).Plan();
        var enriched = plan with
        {
            Kinds = plan.Kinds.Select(s => s with
            {
                Items = s.Items.Select(i => i with { Changes = i.Changes.Select(c => c with { InUseCount = 7 }).ToList() })
                    .ToList(),
            }).ToList(),
        };

        Assert.AreEqual(7, Item(enriched, 1).Changes.Single().InUseCount);
        Assert.AreEqual(plan.PlanId, DataSyncPlanFormat.PlanId(enriched));
    }

    [TestMethod]
    public void ScalarsComeFirstAndWarningsSortByCodeThenChange()
    {
        var f = new PlanFixture();
        f.Codecs[ItemKind] = new DecoratedCodec(TestItemCodec.Instance)
        {
            OnDiff = d => d with
            {
                // Children first, scalars last, and warnings in no particular order.
                Changes = d.Changes.Where(DataSyncPlanFormat.IsChild).Concat(d.Changes.Where(c => !DataSyncPlanFormat.IsChild(c)))
                    .ToList(),
                Warnings =
                [
                    new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, "child:add:c", null),
                    new DataSyncPlanWarning(DataSyncWarningCode.UnknownFieldsIgnored, null, null),
                    new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, null, null),
                    new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, "child:add:b", null),
                ],
            },
        };
        f.Local("12", 1, T("Genre", ("a", "Action")));
        f.Pull(1, new TestItemContent("Genres", "#fff", [new("a", "Action"), new("c", "Crime"), new("b", "Drama")]));

        var item = Item(f.Plan(), 1);
        CollectionAssert.AreEqual(new[] { "name", "color", "child:add:c", "child:add:b" },
            item.Changes.Select(c => c.ChangeId).ToArray(), "scalars first, children in incoming order");
        CollectionAssert.AreEqual(new[] { "1:", "2:", "2:child:add:b", "2:child:add:c" },
            item.Warnings.Select(w => $"{(int)w.Code}:{w.ChangeId}").ToArray());
    }

    [TestMethod]
    public void ItemsFollowTheSharedOrderAndSectionsTheApplyOrder()
    {
        var f = new PlanFixture();
        f.Codecs["aKind"] = new DecoratedCodec(TestItemCodec.Instance)
        {
            DescriptorOverride = TestItemCodec.Instance.Descriptor with { Kind = "aKind", DependsOn = [ItemKind] },
        };
        f.Pull(1, T("Third"), orderKey: "a3");
        f.Pull(2, T("First"), orderKey: "a1");
        f.Pull(3, T("Unordered"));
        f.Pull(4, T("Second"), orderKey: "a2");
        f.Pull(5, T("Dependent"), "aKind");
        f.Pull(6, G("Images", ".jpg"), GroupKind);

        var plan = f.Plan();
        CollectionAssert.AreEqual(new[] { GroupKind, ItemKind, "aKind" }, plan.Kinds.Select(s => s.Kind).ToArray(),
            "ties as DataSyncKindIds.All then ordinal; a dependency first");
        CollectionAssert.AreEqual(new[] { "First", "Second", "Third", "Unordered" },
            Section(plan).Items.Select(i => i.Incoming.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Section(plan).Items.Select(i => i.Incoming.Position).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new DataSyncKindTypeCount(GroupKind, DataSyncPlanItemType.Create, 1),
                new DataSyncKindTypeCount(ItemKind, DataSyncPlanItemType.Create, 4),
                new DataSyncKindTypeCount("aKind", DataSyncPlanItemType.Create, 1),
            },
            plan.Summary.Counts.ToArray());
    }

    [TestMethod]
    public void TheSnapshotContentHashCoversTheManifestsKindHashes()
    {
        var f = new PlanFixture();
        f.Pull(1, T("Genre"));
        f.Pull(2, G("Images", ".jpg"), GroupKind);
        var staged = f.Staged();
        var expected = ContentHash.Of(new JsonObject
        {
            [GroupKind] = staged.Manifest.Kinds.Single(k => k.Kind == GroupKind).ContentHash,
            [ItemKind] = staged.Manifest.Kinds.Single(k => k.Kind == ItemKind).ContentHash,
        });
        var plan = f.Plan();
        Assert.AreEqual(expected, plan.SnapshotContentHash);

        f.Pull(3, T("Tags"));
        Assert.AreNotEqual(plan.SnapshotContentHash, f.Plan().SnapshotContentHash);
    }

    // ---- §8.3 ------------------------------------------------------------------------------------

    [TestMethod]
    public void AnIncomingVersionBelowTheLocalOneIsUnchangedBecauseLocalIsNewer()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre (edited here)", ("a", "Action")), vv: Vv((Peer, 1), (Self, 1)));
        f.Local("13", 2, Typed("Tags", "Choice"), vv: Vv((Peer, 1), (Self, 1)));
        f.Pull(1, T("Genre", ("a", "Action"), ("b", "Drama")), vv: Vv((Peer, 1)));
        f.Pull(2, Typed("Tags", "Tags"), vv: Vv((Peer, 1)));

        var plan = f.Plan();
        foreach (var key in new[] { 1, 2 })
        {
            var item = Item(plan, key);
            Assert.AreEqual(DataSyncPlanItemType.Unchanged, item.Type, "newer local content is never regressed");
            Assert.AreEqual(DataSyncPlanItemReason.LocalIsNewer, item.Reason);
            Assert.AreEqual(0, item.Changes.Count);
            Assert.AreEqual(DataSyncPlanResolution.Update, item.DefaultResolution);
            Assert.IsFalse(item.RequiresConfirmation);
        }
    }

    [TestMethod]
    public void AnEqualVersionIsTheSameRevisionAndNoChange()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre", ("a", "Action")), vv: Vv((Peer, 3)));
        f.Pull(1, T("Genre", ("a", "action")), vv: Vv((Peer, 3)));

        var item = Item(f.Plan(), 1);
        Assert.AreEqual(DataSyncPlanItemType.Unchanged, item.Type);
        Assert.IsNull(item.Reason, "LocalIsNewer only when the local side is strictly newer");
        Assert.AreEqual(0, item.Changes.Count);
    }

    [TestMethod]
    public void AVectorWithoutARevisionSaysNothing()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"), vv: DataSyncVersionVector.Empty);
        f.Local("13", 2, T("Tags"), vv: Vv((Peer, 1)));
        f.Pull(1, T("Genres"), vv: DataSyncVersionVector.Empty);
        f.Pull(2, T("Tag"), vv: DataSyncVersionVector.Empty);

        var plan = f.Plan();
        Assert.AreEqual(DataSyncPlanItemType.Update, Item(plan, 1).Type);
        Assert.AreEqual(DataSyncPlanItemType.Update, Item(plan, 2).Type);
    }

    [TestMethod]
    public void ANewerOrConcurrentIncomingVersionIsDiffed()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"), vv: Vv((Peer, 1)));
        f.Local("13", 2, T("Tags"), vv: Vv((Self, 1)));
        f.Pull(1, T("Genres"), vv: Vv((Peer, 2)));
        f.Pull(2, T("Tag"), vv: Vv((Peer, 1)));

        var plan = f.Plan();
        Assert.AreEqual(DataSyncPlanItemType.Update, Item(plan, 1).Type);
        Assert.AreEqual(DataSyncPlanItemType.Update, Item(plan, 2).Type);
        Assert.IsNull(Item(plan, 1).Reason);
    }

    [TestMethod]
    public void TwoWayMarksEverySeparateNameAsUsedEverywhere()
    {
        var f = new PlanFixture();
        f.Local("12", 5, T("Genre"));
        f.Local("13", 6, Typed("Tags", "Tags"));
        f.Local("14", 3, T("Kept"));
        f.Pull(1, T("Genre"));
        f.Pull(2, Typed("Tags", "Choice"));
        f.Pull(3, T("Kept (renamed)"));
        f.Pull(4, T("New"));

        var follow = f.Plan();
        Assert.IsFalse(follow.Kinds.SelectMany(s => s.Items).Any(i => HasWarning(i, DataSyncWarningCode.NameUsedEverywhere)));

        f.Mode = DataSyncLinkMode.TwoWay;
        var twoWay = f.Plan();
        Assert.IsTrue(HasWarning(Item(twoWay, 1), DataSyncWarningCode.NameUsedEverywhere), "link");
        Assert.IsTrue(HasWarning(Item(twoWay, 2), DataSyncWarningCode.NameUsedEverywhere), "name clash");
        Assert.IsFalse(HasWarning(Item(twoWay, 3), DataSyncWarningCode.NameUsedEverywhere), "update");
        Assert.IsFalse(HasWarning(Item(twoWay, 4), DataSyncWarningCode.NameUsedEverywhere), "create");
        Assert.AreEqual(Item(follow, 1).ReviewToken, Item(twoWay, 1).ReviewToken, "warnings are not in tokens");
    }

    [TestMethod]
    public void ARecordBoundToARowKeptOutOfSyncIsIgnored()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"), state: DataSyncEntitySyncState.LocalOnly);
        f.Local("13", 5, T("Tags"), state: DataSyncEntitySyncState.Detached);
        f.Pull(1, T("Genre"));
        f.Pull(2, T("Tags"));

        var section = Section(f.Plan());
        CollectionAssert.AreEqual(new[] { $"{ItemKind}/k/{Hex(2)}" }, section.Items.Select(i => i.ItemId).ToArray());
        Assert.AreEqual(DataSyncPlanItemType.Create, section.Items.Single().Type,
            "a row kept out of sync is never a natural candidate");
        Assert.AreEqual(0, section.LocalOnlyCount);
    }

    [TestMethod]
    public void AnUnreadableLocalEntityHoldsEveryItemTouchingIt()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"), unreadable: true);
        f.Local("13", 2, T("Tags"), unreadable: true);
        f.Local("14", 5, T("Mood"), unreadable: true);
        f.Local("15", 6, T("Mood"));
        f.Pull(1, T("Genre"));
        f.Pull(3, T("Tags"));
        f.Pull(4, T("Mood"), aliases: [5, 6]);

        var plan = f.Plan();
        var bound = Item(plan, 1);
        Assert.AreEqual(DataSyncHeldReason.LocalUnreadable, bound.HeldReason);
        Assert.AreEqual("12", bound.Local!.LocalKey);
        Assert.AreEqual(DataSyncHeldReason.LocalUnreadable, Item(plan, 3).HeldReason, "by name");
        Assert.AreEqual(DataSyncHeldReason.LocalUnreadable, Item(plan, 4).HeldReason, "an identity conflict");
    }

    [TestMethod]
    public void TombstonesTakeNoPartInAReview()
    {
        var f = new PlanFixture();
        f.Local("12", 1, T("Genre"));
        f.PullTombstone(1);
        f.Pull(2, T("Tags"));

        var section = Section(f.Plan());
        Assert.AreEqual(1, section.Items.Count, "a review never removes");
        Assert.AreEqual(1, section.LocalOnlyCount);
        Assert.AreEqual(0, section.Items.Single().Incoming.Position);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    internal static bool HasWarning(DataSyncPlanItem item, DataSyncWarningCode code) =>
        item.Warnings.Any(w => w.Code == code);

    /// <summary>Every item type at once, in two kinds.</summary>
    internal static PlanFixture RichFixture() => new PlanFixture().With(f =>
    {
        f.Mode = DataSyncLinkMode.TwoWay;
        f.Local("1", 1, T("Updated", ("a", "Action")));
        f.Local("2", 2, T("Same"));
        f.Local("3", 3, Typed("Mismatch", "Tags"));
        f.Local("4", 20, T("Linked"));
        f.Local("5", 21, T("Twin"));
        f.Local("6", 22, T("Twin"));
        f.Local("7", 23, Typed("Clash", "Tags"));
        f.Local("8", 4, T("Conflict A"));
        f.Local("9", 5, T("Conflict B"));
        f.Local("10", 24, T("Dup"));
        f.Local("20", 30, G("Images", ".jpg"), GroupKind);
        f.Tombstone(40);
        f.Pull(1, T("Updated!", ("a", "Action"), ("b", "Drama")));
        f.Pull(2, T("Same"));
        f.Pull(3, Typed("Mismatch", "Choice"));
        f.Pull(10, T("Linked", ("n", "New")));
        f.Pull(11, T("Twin"));
        f.Pull(12, Typed("Clash", "Choice"));
        f.Pull(4, T("Conflict"), aliases: [5]);
        f.Pull(13, T("Dup"));
        f.Pull(14, T("dup"));
        f.Pull(15, T("Brand new"));
        f.PullHeldAtSource(16);
        f.Pull(31, G("Images", ".jpg"), GroupKind);
        f.Pull(32, G("Videos", ".mp4"), GroupKind);
    });
}

internal static class PlanFixtureExtensions
{
    public static PlanFixture With(this PlanFixture fixture, Action<PlanFixture> setup)
    {
        setup(fixture);
        return fixture;
    }
}
