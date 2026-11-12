using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostContentReadinessTests
{
    private static PostContent Content() => new()
    {
        Title = "A resource", SourceHint = "Example", SourceUrl = "https://example.test/post/1", MainHtml = "Download link, code and instructions. Proof:",
        Locks = [new("https://example.test/buy/1?nonce=one", 999999999m, false)
        {BlockId = "0:block:1", ContextText = "Purchase proof for moderators", CurrencyCode = "COIN", PurchaseTargetId = "target-1"}]
    };

    private static PostContentBlockDecision Optional(string id = "0:block:1", string evidence = "Purchase proof for moderators") => new()
    {BlockId = id, Relevance = "optional", Purpose = "purchaseProof", Evidence = [evidence]};

    [TestMethod]
    public void EvidenceBasedOptionalBlockAllowsExtractionWithoutPretendingItWasBought()
    {
        var original = Content();
        var resolved = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional()]));
        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(resolved));
        Assert.IsFalse(resolved.Locks.Single().IsBought);
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(original));
    }

    [TestMethod]
    [DataRow("An unrelated quote")]
    [DataRow("")]
    public void EvidenceMustBelongToThisExactBlock(string evidence)
    {
        var original = Content();
        var resolved = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional(evidence: evidence)]));
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
    }

    [TestMethod]
    public void SharedPaymentEndpointDoesNotMergeProofAndRequiredDownloadCode()
    {
        var original = Content();
        var required = original.Locks[0] with {BlockId = "0:block:2", Price = 1, ContextText = "Download password"};
        original = original with {Locks = [original.Locks[0], required]};
        var resolved = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional()]));
        CollectionAssert.AreEqual(new[] {"0:block:2"}, PostContentReadinessPolicy.BlockingLocks(resolved).Select(l => l.BlockId).ToArray());
        Assert.AreEqual(2, resolved.Locks.Count);
    }

    [TestMethod]
    public void NewBodyNewPriceOrNewBlockInvalidatesSnapshotDecisionButAccountBalanceDoesNot()
    {
        var original = Content();
        var assessment = PostContentReadinessPolicy.FromAnalysis(original, [Optional()]);
        Assert.IsTrue(PostContentReadinessPolicy.IsCurrent(original with {Balance = 1234, CapturedAt = DateTimeOffset.UtcNow}, assessment));
        Assert.IsFalse(PostContentReadinessPolicy.IsCurrent(original with {MainHtml = "Different download scope"}, assessment));
        Assert.IsFalse(PostContentReadinessPolicy.IsCurrent(original with {Locks = [original.Locks[0] with {Price = 1}]}, assessment));
        Assert.IsFalse(PostContentReadinessPolicy.IsCurrent(original with {Locks = [original.Locks[0], original.Locks[0] with {BlockId = "other"}]}, assessment));
    }

    [TestMethod]
    public void SourceStablePaymentTargetCanSurviveNonceRotation()
    {
        var original = Content();
        var current = original with {Locks = [original.Locks[0] with {Url = "https://example.test/buy/1?nonce=two"}]};
        Assert.AreEqual(PostContentReadinessPolicy.Fingerprint(original), PostContentReadinessPolicy.Fingerprint(current));
    }

    [TestMethod]
    public void MissingDuplicateAndUnsupportedBlockDecisionsRemainBlocking()
    {
        var original = Content();
        foreach (var decisions in new[] {new List<PostContentBlockDecision>(), [Optional(), Optional()], [Optional() with {Purpose = "downloadCredentials"}]})
            Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Apply(original,
                PostContentReadinessPolicy.FromAnalysis(original, decisions))));
    }

    [TestMethod]
    public void NewlyUnlockedContentMustBeReassessedEvenWhenTheProofLabelIsUnchanged()
    {
        var original = Content();
        var required = original.Locks[0] with {BlockId = "0:block:0", ContextText = "Password", Price = 1};
        original = original with {Locks = [required, original.Locks[0]]};
        original = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional()]));
        var fresh = original with {MainHtml = "Unlocked download details", Locks = [required with {IsBought = true}, original.Locks[1]], Readiness = null};
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Reconcile(fresh, original)));
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Reconcile(
            fresh with {Locks = [required, original.Locks[1] with {ContextText = "Another password"}]}, original)));
    }

    [TestMethod]
    public void NewBodyInstructionsCannotReuseAnOptionalDecisionFromAnUnchangedLabel()
    {
        var original = Content();
        original = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional()]));
        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Reconcile(original, original)));
        var fresh = original with {MainHtml = "The required password is inside the proof block below."};
        Assert.IsNull(PostContentReadinessPolicy.Reconcile(fresh, original).Readiness);
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Reconcile(fresh, original)));
    }

    [TestMethod]
    public void CredentialWhitespaceIsEditorialEvidence()
    {
        var original = Content() with {MainHtml = "<pre>Password: a  b</pre>"};
        Assert.AreNotEqual(PostContentReadinessPolicy.Fingerprint(original),
            PostContentReadinessPolicy.Fingerprint(original with {MainHtml = "<pre>Password: a b</pre>"}));
    }

    [TestMethod]
    public void ManualScopeDoesNotAutomaticallyExtendToAChangedSnapshot()
    {
        var original = Content();
        original = PostContentReadinessPolicy.Apply(original, PostContentReadinessPolicy.FromAnalysis(original, [Optional()]) with {Basis = "manual"});
        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(original));
        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(PostContentReadinessPolicy.Reconcile(original with {MainHtml = "New instructions"}, original)));
    }

    [TestMethod]
    public void LargeSourceAmountsRetainExactDisplayDigits()
    {
        Assert.AreEqual("9223372036854775807", new PostContentLock(null, 9223372036854775807m, false).PriceText);
    }

    [TestMethod]
    public void PlatformInstructionsAreAppendedOnlyForTheChosenSource()
    {
        var profile = new ExampleProfile();
        Assert.AreEqual("base", PostAnalysisPromptComposer.Compose("base", "Another", [profile]));
        StringAssert.Contains(PostAnalysisPromptComposer.Compose("base", "example", [profile]), "source convention");
        StringAssert.StartsWith(PostAnalysisPromptComposer.Compose("base", "Example", [profile]), "base");
    }

    private sealed class ExampleProfile : IPostAnalysisPromptContributor
    {
        public string Source => "Example";
        public string Version => "v1";
        public string Instructions => "source convention";
    }
}
