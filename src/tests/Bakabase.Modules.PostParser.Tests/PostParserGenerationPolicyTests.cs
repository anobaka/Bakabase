using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostParserGenerationPolicyTests
{
    [TestMethod]
    public async Task DownloadExtractionRequestsBoundedJsonWithoutChangingCredentials()
    {
        var llm = new RecordingLlm("""
            {"resources":[{"link":"https://mega.nz/file/download#key","code":" 访问 码 ","password":" a&b  密码 "}]}
            """);
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent());

        Assert.AreEqual(AiFeature.PostParser, llm.Feature);
        Assert.AreEqual(16384, llm.Parameters!.MaxTokens);
        Assert.IsTrue(llm.Parameters.UseJsonResponseFormat);
        var properties = llm.Parameters.JsonResponseSchema!.Value.GetProperty("properties");
        Assert.IsTrue(properties.TryGetProperty("coverCandidates", out var covers));
        Assert.IsTrue(covers.GetProperty("items").GetProperty("properties").TryGetProperty("url", out _));
        Assert.IsFalse(properties.TryGetProperty("extraction", out _), "File processing belongs to each resource, not the response root.");
        var resourceSchema = properties.GetProperty("resources").GetProperty("items");
        Assert.IsTrue(resourceSchema.GetProperty("required").EnumerateArray().Any(x => x.GetString() == "extraction"));
        Assert.IsTrue(resourceSchema.GetProperty("properties").TryGetProperty("password", out _));
        Assert.IsFalse(resourceSchema.GetProperty("additionalProperties").GetBoolean());
        var stepSchema = resourceSchema.GetProperty("properties").GetProperty("extraction")
            .GetProperty("properties").GetProperty("steps").GetProperty("items");
        var stepProperties = stepSchema.GetProperty("properties");
        Assert.IsTrue(stepProperties.GetProperty("op").GetProperty("enum").EnumerateArray().Any(v => v.GetString() == "renameLiteral"));
        foreach (var field in new[] {"findText", "replaceText"})
        {
            Assert.IsTrue(stepProperties.GetProperty(field).GetProperty("type").EnumerateArray().Any(v => v.GetString() == "null"));
            Assert.IsTrue(stepSchema.GetProperty("required").EnumerateArray().Any(v => v.GetString() == field));
        }
        var resource = result.Resources.Single();
        Assert.AreEqual("https://mega.nz/file/download#key", resource.Link);
        Assert.AreEqual(" 访问 码 ", resource.Code);
        Assert.AreEqual(" a&b  密码 ", resource.Password);
    }

    [TestMethod]
    public async Task AvailabilityUsesItsOwnSmallerJsonBudget()
    {
        var llm = new RecordingLlm("""{"status":"noExpiryReported","evidence":[],"reason":"No expiry reports."}""");
        var result = await new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent());

        Assert.AreEqual("noExpiryReported", result.Status);
        Assert.AreEqual(AiFeature.PostParser, llm.Feature);
        Assert.AreEqual(4096, llm.Parameters!.MaxTokens);
        Assert.IsTrue(llm.Parameters.UseJsonResponseFormat);
        var properties = llm.Parameters.JsonResponseSchema!.Value.GetProperty("properties");
        Assert.IsTrue(properties.TryGetProperty("status", out _));
        Assert.IsFalse(properties.TryGetProperty("resources", out _), "Availability uses its own response contract.");
        Assert.AreEqual(3, properties.GetProperty("evidence").GetProperty("maxItems").GetInt32());
        Assert.AreEqual(120, properties.GetProperty("evidence").GetProperty("items").GetProperty("maxLength").GetInt32());
        Assert.AreEqual(100, properties.GetProperty("reason").GetProperty("maxLength").GetInt32());
        var decisions = properties.GetProperty("contentReadiness").GetProperty("properties").GetProperty("decisions");
        Assert.AreEqual(256, decisions.GetProperty("maxItems").GetInt32());
        var block = decisions.GetProperty("items").GetProperty("properties");
        Assert.AreEqual(1, block.GetProperty("evidence").GetProperty("maxItems").GetInt32());
        Assert.AreEqual(80, block.GetProperty("evidence").GetProperty("items").GetProperty("maxLength").GetInt32());
        Assert.AreEqual(60, block.GetProperty("reason").GetProperty("maxLength").GetInt32());
    }

    [DataTestMethod]
    [DataRow(0, 0, 4096)]
    [DataRow(0, 200, 4096)]
    [DataRow(2, 0, 4608)]
    [DataRow(2, 200, 4608)]
    [DataRow(4, 0, 5120)]
    [DataRow(5, 0, 5376)]
    [DataRow(15, 0, 7936)]
    [DataRow(16, 0, 8192)]
    [DataRow(256, 200, 8192)]
    public async Task AvailabilityBudgetCoversUnreadBlockOutputAndRemainsBounded(int unread, int purchased, int expected)
    {
        var llm = new RecordingLlm("""{"status":"unknown","evidence":[]}""");
        await new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent
        {
            Locks = Enumerable.Range(0, unread + purchased)
                .Select(i => new PostContentLock("https://example.test/pay", 1, i >= unread) {BlockId = "block-" + i}).ToList()
        });
        Assert.AreEqual(expected, llm.Parameters!.MaxTokens);
        Assert.AreEqual(1, llm.Calls);
    }

    [TestMethod]
    public async Task CompactBlockAssessmentStillRequiresEvidenceAndPreservesNecessaryContent()
    {
        var llm = new RecordingLlm("""
            {"status":"noExpiryReported","evidence":[],"reason":null,"contentReadiness":{"decisions":[
              {"blockId":"download","relevance":"required","purpose":"downloadCredentials","evidence":["download password"],"reason":null},
              {"blockId":"proof","relevance":"optional","purpose":"purchaseProof","evidence":["purchase proof only"],"reason":null},
              {"blockId":"unsupported","relevance":"optional","purpose":"donation","evidence":["not in this block"],"reason":null}
            ]}}
            """);
        var content = new PostContent {Locks =
            [new("https://example.test/pay1", 1, false) {BlockId = "download", ContextText = "download password"},
             new("https://example.test/pay2", 99999, false) {BlockId = "proof", ContextText = "purchase proof only"},
             new("https://example.test/pay3", 99999, false) {BlockId = "unsupported", ContextText = "unknown content"}]};
        var result = await new PostAvailabilityAnalyzer(llm).AnalyzeAsync(content);
        Assert.AreEqual(3, result.ContentReadiness!.Decisions.Count);
        var effective = PostContentReadinessPolicy.Apply(content, result.ContentReadiness);
        CollectionAssert.AreEquivalent(new[] {"download", "unsupported"},
            PostContentReadinessPolicy.BlockingLocks(effective).Select(l => l.BlockId).ToArray());
        Assert.IsTrue(effective.Locks.All(l => !l.IsBought));
    }

    [TestMethod]
    public async Task TruncatedExtractionCannotSucceedEvenWhenTheReturnedPrefixIsValidJson()
    {
        var llm = new RecordingLlm("""{"resources":[{"link":"https://example.test/first"}]}""", ChatFinishReason.Length);
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => extractor.ExtractAsync(new PostContent()));
        StringAssert.Contains(error.Message, "Partial download information was not accepted");
        Assert.AreEqual(1, llm.Calls, "Truncated responses must not trigger another expensive generation automatically.");
    }

    [TestMethod]
    public async Task TruncatedAvailabilityCannotAuthorizePurchaseFromAValidJsonPrefix()
    {
        var llm = new RecordingLlm("""
            {"status":"noExpiryReported","evidence":[],"reason":null,"contentReadiness":{"decisions":[
              {"blockId":"proof","relevance":"optional","purpose":"purchaseProof","evidence":["proof only"],"reason":null},
              {"blockId":"support","relevance":"optional","purpose":"donation","evidence":["optional support"],"reason":null}
            ]}}
            """, ChatFinishReason.Length);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent {Locks =
                [new("https://example.test/pay1", 2, false) {BlockId = "proof", ContextText = "proof only"},
                 new("https://example.test/pay2", 5, false) {BlockId = "support", ContextText = "optional support"}]}));
        Assert.AreEqual(4608, llm.Parameters!.MaxTokens, "Two-block assessments need room beyond the previously exhausted 2048-token ceiling.");
        Assert.AreEqual(1, llm.Calls);
    }

    private sealed class RecordingLlm(string text, ChatFinishReason? finishReason = null) : ILlmService
    {
        public AiFeature? Feature;
        public LlmModelParameters? Parameters;
        public int Calls;

        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Feature = feature;
            Parameters = parametersOverride;
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) {FinishReason = finishReason});
        }

        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature,
            IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
