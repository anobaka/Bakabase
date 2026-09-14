using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Extensions;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostDownloadInfoExtractorTests
{
    [TestMethod]
    public async Task ExtractionPreservesEveryLinkAndItsCredentialsIncludingComments()
    {
        var llm = new FakeLlm
        {
            ResponseText = """
                ```json
                {"title":"  Example  ","resources":[
                    {"link":" https://pan.baidu.com/s/first ","code":" abcd ","password":" archive-pass "},
                    {"link":"magnet:?xt=urn:btih:123","code":null,"password":"same archive"},
                    {"link":"https://mega.nz/folder/other","code":" ","password":""},
                    {"link":" "},null
                ]}
                ```
                """
        };
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent
        {
            Title = "Original title",
            MainHtml = "Main post content",
            CommentHtmlList = ["Author comment with another link", "Archive password in comment"],
            Locks = [new PostContentLock("https://example.com/buy", 20m, false)]
        });

        Assert.AreEqual("Example", result.Title);
        Assert.AreEqual(3, result.Resources.Count);
        Assert.AreEqual("https://pan.baidu.com/s/first", result.Resources[0].Link);
        Assert.AreEqual("abcd", result.Resources[0].Code);
        Assert.AreEqual("archive-pass", result.Resources[0].Password);
        Assert.AreEqual("magnet:?xt=urn:btih:123", result.Resources[1].Link);
        Assert.AreEqual("same archive", result.Resources[1].Password);
        Assert.IsNull(result.Resources[2].Code);
        Assert.IsNull(result.Resources[2].Password);
        Assert.AreEqual(AiFeature.PostParser, llm.LastFeature);
        StringAssert.Contains(llm.Prompt!, "Original title");
        StringAssert.Contains(llm.Prompt!, "Main post content");
        StringAssert.Contains(llm.Prompt!, "Author comment with another link");
        StringAssert.Contains(llm.Prompt!, "Archive password in comment");
        Assert.AreEqual(1, llm.Calls);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"title\":\" \",\"resources\":null}")]
    [DataRow("{\"resources\":[]}")]
    public async Task MissingDownloadInfoProducesAnEmptyResultWithoutInventingLinks(string response)
    {
        var extractor = new PostDownloadInfoExtractor(new FakeLlm {ResponseText = response},
            NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent {MainHtml = "No downloads here"});
        Assert.IsNull(result.Title);
        Assert.AreEqual(0, result.Resources.Count);
    }

    [TestMethod]
    public async Task MalformedAiResponseFailsInsteadOfClaimingACompletedEmptyParse()
    {
        var extractor = new PostDownloadInfoExtractor(new FakeLlm {ResponseText = "not json"},
            NullLogger<PostDownloadInfoExtractor>.Instance);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(new PostContent {MainHtml = "Post"}));
    }

    [TestMethod]
    public async Task CancellationBeforeExtractionDoesNotCallAi()
    {
        var llm = new FakeLlm();
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            extractor.ExtractAsync(new PostContent(), new CancellationToken(true)));
        Assert.AreEqual(0, llm.Calls);
    }

    [TestMethod]
    public async Task CallerCancellationTokenIsForwardedToAi()
    {
        var llm = new FakeLlm();
        using var cts = new CancellationTokenSource();
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);
        await extractor.ExtractAsync(new PostContent(), cts.Token);
        Assert.AreEqual(cts.Token, llm.LastCancellationToken);
    }

    [TestMethod]
    public void CapabilityCanBeResolvedWithoutWorkflowAcquisitionOrTaskServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILlmService>(new FakeLlm());
        services.AddPostParserCapabilities();
        services.AddPostParserCapabilities();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<PostDownloadInfoExtractor>(
            scope.ServiceProvider.GetRequiredService<IPostDownloadInfoExtractor>());
        Assert.AreEqual(1, scope.ServiceProvider.GetServices<IPostDownloadInfoExtractor>().Count());
    }

    private sealed class FakeLlm : ILlmService
    {
        public string ResponseText = "{}";
        public int Calls;
        public string? Prompt;
        public AiFeature? LastFeature;
        public CancellationToken LastCancellationToken;

        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Calls++;
            LastFeature = feature;
            LastCancellationToken = ct;
            Prompt = string.Join("\n", messages.Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText)));
        }

        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId,
            IEnumerable<ChatMessage> messages, LlmModelParameters? parameters = null, AiFeature? feature = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature,
            IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
