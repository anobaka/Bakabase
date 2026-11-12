using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

[TestClass]
public class SoulPlusPostReadinessProfileTests
{
    private static PostContent Content(string? context, decimal price = 999999m) => new()
    {
        SourceHint = "SoulPlus", SourceUrl = "https://www.north-plus.net/read.php?tid=42", Title = "Fixture resource",
        MainHtml = "<p>下载地址和操作说明可见。</p>",
        Locks = [new("https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=tpc", price, false)
            {BlockId = "0:block:0", Floor = "0", PurchaseTargetId = "fixture-target", ContextText = context}]
    };

    private static PostContentBlockDecision Optional(string purpose, string evidence, string blockId = "0:block:0") => new()
    {
        BlockId = blockId, Relevance = "optional", Purpose = purpose, Evidence = [evidence]
    };

    private static async Task<PostContent> Analyze(PostContent content, params PostContentBlockDecision[] decisions)
    {
        var text = JsonSerializer.Serialize(new
        {
            status = "noExpiryReported", evidence = Array.Empty<string>(), contentReadiness = new {decisions}
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var profile = new SoulPlusPostAnalysisPromptContributor();
        var result = await new PostAvailabilityAnalyzer(new FixtureLlm(text), promptContributors: [profile],
            decisionValidators: [profile]).AnalyzeAsync(content);
        return PostContentReadinessPolicy.Apply(content, result.ContentReadiness);
    }

    [TestMethod]
    [DataRow("自购证明：", "purchaseProof")]
    [DataRow("購買憑證", "purchaseProof")]
    [DataRow("自购：", "purchaseProof")]
    [DataRow("如果你想要支持我：", "donation")]
    [DataRow("额外打赏", "donation")]
    [DataRow("可选补充", "supplement")]
    public async Task ExplicitLocalPurposeCanAllowAnalysisWithoutPretendingTheBlockWasPurchased(string context, string purpose)
    {
        var resolved = await Analyze(Content(context), Optional(purpose, context));

        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(resolved));
        Assert.AreEqual("analysis", resolved.Readiness!.Basis);
        Assert.IsFalse(resolved.Locks.Single().IsBought);
    }

    [TestMethod]
    [DataRow("此帖售价 999999 SP币")]
    [DataRow("这是我自购的资源")]
    [DataRow("我购买过这个资源")]
    [DataRow("密码：")]
    public async Task HighPriceOrGeneralOwnershipCannotAuthorizeIgnoringAHiddenBlock(string context)
    {
        var resolved = await Analyze(Content(context), Optional("purchaseProof", context));

        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
        Assert.AreEqual("unknown", resolved.Readiness!.Decisions.Single().Relevance);
    }

    [TestMethod]
    public async Task TitleAndOtherFloorEvidenceCannotSupplyMissingLocalPurpose()
    {
        var content = Content(null) with
        {
            Title = "自购证明", MainHtml = "正文中提及自购证明。",
            Comments = [new() {Floor = "2", Html = "请补充自购证明。"}]
        };
        var resolved = await Analyze(content, Optional("purchaseProof", "自购证明"));

        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
    }

    [TestMethod]
    public async Task CorrectPurposeWithEvidenceFromAnotherBlockStillRemainsBlocking()
    {
        var resolved = await Analyze(Content("自购证明："), Optional("purchaseProof", "如果你想要支持我"));

        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
    }

    [TestMethod]
    [DataRow("自购证明和下载密码都在这里", "purchaseProof", "自购证明")]
    [DataRow("如果你想要支持我，购买后获得必需解压密码", "donation", "如果你想要支持我")]
    public async Task AProofOrDonationLabelCannotHideExplicitlyMixedRequiredCredentials(string context, string purpose, string evidence)
    {
        var resolved = await Analyze(Content(context), Optional(purpose, evidence));

        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
    }

    [TestMethod]
    public async Task IgnoringOneProofBlockKeepsRequiredSiblingAtTheSamePaymentEndpoint()
    {
        var content = Content("自购证明：");
        content = content with {Locks = [content.Locks[0], content.Locks[0] with
            {BlockId = "0:block:1", ContextText = "提取码：", Price = 1m}]};
        var resolved = await Analyze(content, Optional("purchaseProof", "自购证明："),
            new() {BlockId = "0:block:1", Relevance = "required", Purpose = "downloadCredentials", Evidence = ["提取码："]});

        CollectionAssert.AreEqual(new[] {"0:block:1"},
            PostContentReadinessPolicy.BlockingLocks(resolved).Select(b => b.BlockId).ToArray());
        Assert.AreEqual(2, resolved.Locks.Count);
        Assert.IsTrue(resolved.Locks.All(b => !b.IsBought));
        Assert.AreEqual(resolved.Locks[0].PurchaseTargetId, resolved.Locks[1].PurchaseTargetId);
    }

    [TestMethod]
    public async Task SourceSelectionIsCaseInsensitiveForTheEvidenceValidator()
    {
        var resolved = await Analyze(Content("仅说明价格") with {SourceHint = "soulplus"},
            Optional("purchaseProof", "仅说明价格"));

        Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(resolved));
    }

    [TestMethod]
    public void SourceGuidanceAppendsToBasePromptAndDoesNotChangeOtherReaders()
    {
        const string basePrompt = "Base extraction and purchase safety rules.";
        var profile = new SoulPlusPostAnalysisPromptContributor();
        var composed = PostAnalysisPromptComposer.Compose(basePrompt, "soulplus", [profile]);

        StringAssert.StartsWith(composed, basePrompt);
        StringAssert.Contains(composed, profile.Instructions);
        Assert.AreEqual(basePrompt, PostAnalysisPromptComposer.Compose(basePrompt, "AnotherReader", [profile]));
    }

    private sealed class FixtureLlm(string response) : ILlmService
    {
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature,
            IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
