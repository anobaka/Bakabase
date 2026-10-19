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
        Assert.IsFalse(properties.TryGetProperty("extraction", out _), "File processing belongs to each resource, not the response root.");
        var resourceSchema = properties.GetProperty("resources").GetProperty("items");
        Assert.IsTrue(resourceSchema.GetProperty("required").EnumerateArray().Any(x => x.GetString() == "extraction"));
        Assert.IsTrue(resourceSchema.GetProperty("properties").TryGetProperty("password", out _));
        Assert.IsFalse(resourceSchema.GetProperty("additionalProperties").GetBoolean());
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
        Assert.AreEqual(2048, llm.Parameters!.MaxTokens);
        Assert.IsTrue(llm.Parameters.UseJsonResponseFormat);
        var properties = llm.Parameters.JsonResponseSchema!.Value.GetProperty("properties");
        Assert.IsTrue(properties.TryGetProperty("status", out _));
        Assert.IsFalse(properties.TryGetProperty("resources", out _), "Availability uses its own response contract.");
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
        var llm = new RecordingLlm("""{"status":"noExpiryReported","evidence":[]}""", ChatFinishReason.Length);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent()));
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
