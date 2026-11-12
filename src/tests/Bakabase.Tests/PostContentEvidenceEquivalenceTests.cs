using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostContentEvidenceEquivalenceTests
{
    internal sealed class FixtureCanonicalizer : IPostContentEvidenceCanonicalizer
    {
        public string PolicyId => "fixture-evidence-v2";
        public bool TryCanonicalSourceFingerprint(Content content, out string fingerprint)
        {
            fingerprint = "";
            if (!content.MainHtml.StartsWith("fixture-body;buyers=", StringComparison.Ordinal) ||
                content.SourceEvidenceFingerprint != Hash(content.MainHtml)) return false;
            fingerprint = Hash("fixture-body;buyers=<counter>");
            return true;
        }
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static Content Count(Content content, int count)
    {
        var body = "fixture-body;buyers=" + count;
        return content with {MainHtml = body, SourceEvidenceFingerprint = Hash(body), ReaderEvidence = null};
    }
    private static Content Sample(int count) => Count(new() {SourceHint = "fixture", SourceUrl = "https://example.test/post",
        Locks = [new("https://example.test/pay", 2, false) {BlockId = "proof", ContextText = "proof", Floor = "0"}]}, count);
    private static readonly IPostContentEvidenceCanonicalizer[] Policies = [new FixtureCanonicalizer()];

    [TestMethod]
    public void VerifiedVolatileChangesPreserveOriginalManualEventAndChainWithoutResurrectingARevocation()
    {
        var original = Sample(1); var second = Sample(2); var third = Sample(3);
        var entry = new PostParserContentReadinessEvent {Version = 1, BlockIds = ["proof"],
            ContentFingerprint = PostContentReadinessPolicy.Fingerprint(original)};
        var state = new PostParserContentReadinessState {History = [entry]};
        state = state.RecordEquivalent(original, second, Policies, out var equivalent);
        Assert.IsTrue(equivalent);
        state = state.RecordEquivalent(second, third, Policies, out equivalent);
        Assert.IsTrue(equivalent); Assert.AreEqual(1, state.Version);
        Assert.AreEqual(entry, state.History.Single()); Assert.AreEqual(entry.Id, state.Active(third)!.Id);
        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(state.Apply(third)));
        Assert.AreEqual(PostContentReadinessPolicy.Fingerprint(third), state.Apply(third).Readiness!.Fingerprint);
        state = state with {History = state.History.Append(new PostParserContentReadinessEvent
            {Version = 2, Action = "revoke", DecisionId = entry.Id}).ToList()};
        Assert.IsNull(state.Active(third)); Assert.IsNull(state.Apply(third).Readiness);
    }

    [TestMethod]
    [DataRow("price")][DataRow("context")][DataRow("target")][DataRow("owner")]
    [DataRow("block")][DataRow("unlocked")][DataRow("source")][DataRow("unverified")]
    public void SourceCanonicalHashCannotWaiveOtherWholePostFields(string change)
    {
        var original = Sample(1); var fresh = Sample(2); var locked = fresh.Locks.Single();
        fresh = change switch
        {
            "price" => fresh with {Locks = [locked with {Price = 3}]},
            "context" => fresh with {Locks = [locked with {ContextText = "download password"}]},
            "target" => fresh with {Locks = [locked with {Url = "https://example.test/pay/other"}]},
            "owner" => fresh with {Locks = [locked with {Floor = "2"}]},
            "block" => fresh with {Locks = [locked with {BlockId = "another"}]},
            "unlocked" => fresh with {Locks = [locked with {IsBought = true}]},
            "source" => fresh with {SourceUrl = "https://other.test/post"},
            _ => fresh with {SourceEvidenceFingerprint = new string('F', 64)}
        };
        Assert.IsFalse(PostContentEvidenceEquivalence.TryProve(original, fresh, Policies, out _));
    }

    [TestMethod]
    public void PureReconcileRequiresValidatedPreviousAnalysisAndDoesNotInventEvidence()
    {
        var previous = Sample(1);
        previous = previous with {Readiness = PostContentReadinessPolicy.FromAnalysis(previous,
            [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["proof"]}])};
        Assert.IsNotNull(PostContentReadinessPolicy.Reconcile(Sample(2), previous, Policies).Readiness);
        Assert.IsNull(PostContentReadinessPolicy.Reconcile(Sample(2), previous).Readiness);
        Assert.IsNull(PostContentReadinessPolicy.Reconcile(Sample(2), previous with {SourceEvidenceFingerprint = "untrusted"}, Policies).Readiness);
    }

    [TestMethod]
    [DataRow("null")][DataRow("[{\"policyId\":\"x\",\"beforeFingerprint\":\"bad\"}]")]
    public void MalformedAliasesNeverDiscardTheOriginalPaymentJournal(string aliases)
    {
        var receipt = new PostParserContentReviewReceipt {Attempts = [new() {TargetId = "fixture", Status = "pending"}]};
        var json = JsonSerializer.Serialize(new PostParserContentReadinessState {Reviews = [receipt]}, WorkflowJson.Options);
        using var document = JsonDocument.Parse(json);
        var objectValue = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        objectValue["evidenceEquivalences"] = System.Text.Json.Nodes.JsonNode.Parse(aliases);
        var state = PostParserContentReadinessState.Read(objectValue.ToJsonString());
        Assert.AreEqual(0, state.EvidenceEquivalences.Count);
        Assert.AreEqual("pending", state.Reviews.Single().Attempts.Single().Status);
    }
}
