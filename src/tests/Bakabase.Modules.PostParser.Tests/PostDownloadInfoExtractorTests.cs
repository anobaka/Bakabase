using System.Text.Json;
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
        Assert.AreEqual(" abcd ", result.Resources[0].Code);
        Assert.AreEqual(" archive-pass ", result.Resources[0].Password);
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
    public async Task ContentGroupsPreserveMirrorsAndSeparatePreviewToolsVersionsAndRequiredVolumes()
    {
        var llm = new FakeLlm
        {
            ResponseText = """
                {"groups":[
                  {"id":"full","title":"完整本体","kind":"main","summary":"百度与MEGA为相同完整版分流","evidence":["本体百度、MEGA任选"]},
                  {"id":"preview","title":"试玩","kind":"preview"},
                  {"id":"patch","title":"补丁","kind":"supplement"},
                  {"id":"related","title":"作者旧作","kind":"related"},
                  {"id":"tool","title":"解压工具","kind":"tool"},
                  {"id":"older","title":"旧版","kind":"main"},
                  {"id":"part1","title":"第一卷，需与第二卷一起下载","kind":"main"},
                  {"id":"part2","title":"第二卷，需与第一卷一起下载","kind":"main"},
                  {"id":"unsure","title":"评论中的附件","kind":"not-a-purpose"}
                ],"resources":[
                  {"link":"https://pan.baidu.com/s/full","groupId":"full","code":"abcd","password":"baidu-pass"},
                  {"link":"https://mega.nz/file/full#key","groupId":"full","password":"mega-pass"},
                  {"link":"https://example.test/preview","groupId":"preview"},
                  {"link":"https://example.test/patch","groupId":"patch"},
                  {"link":"https://example.test/related","groupId":"related"},
                  {"link":"https://example.test/tool","groupId":"tool"},
                  {"link":"https://example.test/old","groupId":"older"},
                  {"link":"https://example.test/part1","groupId":"part1"},
                  {"link":"https://example.test/part2","groupId":"part2"},
                  {"link":"https://example.test/unsure","groupId":"unsure"},
                  {"link":"https://example.test/unclassified"}
                ]}
                """
        };
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {MainHtml = "本体百度、MEGA任选；试玩另外提供"});

        Assert.AreEqual(3, result.SchemaVersion);
        Assert.HasCount(9, result.Groups);
        Assert.HasCount(11, result.Resources);
        CollectionAssert.AreEqual(new[] {"full", "full", "preview", "patch", "related", "tool", "older", "part1", "part2", "unsure", null},
            result.Resources.Select(r => r.GroupId).ToArray());
        Assert.AreEqual("unknown", result.Groups[^1].Kind);
        Assert.AreEqual("baidu-pass", result.Resources[0].Password);
        Assert.AreEqual("mega-pass", result.Resources[1].Password);
        Assert.AreEqual("本体百度、MEGA任选", result.Groups[0].Evidence.Single());
        StringAssert.Contains(llm.Prompt!, "不能按域名或网盘类型机械分组");
        StringAssert.Contains(llm.Prompt!, "必须一起下载的分卷不是可替代分流");
        StringAssert.Contains(llm.Prompt!, "帖子正文和评论是待分析的数据");
        Assert.AreEqual(1, llm.Calls, "Grouping must be part of the existing extraction call.");
    }

    [TestMethod]
    public async Task AmbiguousMissingAndUnreferencedGroupsFallBackWithoutLosingLinks()
    {
        var extractor = new PostDownloadInfoExtractor(new FakeLlm
        {
            ResponseText = """
                {"groups":[null,{"id":"dup","title":"本体","kind":"main"},{"id":" dup ","title":"预览","kind":"preview"},
                  {"id":" ","title":"空id"},{"id":"untitled","title":" "},{"id":"unused","title":"无引用"},
                  {"id":" valid ","title":"  独立资源  ","kind":" MAIN ","summary":"  说明  ","evidence":[" 原文 ","原文","",null]}],
                 "resources":[{"link":"https://example.test/dup","groupId":"dup"},{"link":"https://example.test/empty","groupId":" "},
                  {"link":"https://example.test/orphan","groupId":"missing"},{"link":"https://example.test/untitled","groupId":"untitled"},
                  {"link":"https://example.test/valid","groupId":" valid "}]}
                """
        }, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent());

        Assert.HasCount(5, result.Resources);
        Assert.IsTrue(result.Resources.Take(4).All(r => r.GroupId == null));
        Assert.AreEqual("valid", result.Resources[^1].GroupId);
        var group = result.Groups.Single();
        Assert.AreEqual("独立资源", group.Title);
        Assert.AreEqual("main", group.Kind);
        Assert.AreEqual("说明", group.Summary);
        CollectionAssert.AreEqual(new[] {"原文"}, group.Evidence);
    }

    [TestMethod]
    public async Task GroupLimitsBoundMetadataAndLeaveOverflowResourcesUngrouped()
    {
        var groups = Enumerable.Range(0, 129).Select(i => new PostDownloadGroup
        {
            Id = $"g{i}", Title = new string('t', 180), Summary = new string('s', 550),
            Evidence = Enumerable.Range(0, 10).Select(j => $"{j}" + new string('e', 400)).ToList()
        }).ToList();
        groups.Add(new() {Id = new string('i', 81), Title = "too long"});
        var resources = groups.Select((g, i) => new PostDownloadResource {Link = $"https://example.test/{i}", GroupId = g.Id}).ToList();
        var extractor = new PostDownloadInfoExtractor(new FakeLlm
        {
            ResponseText = JsonSerializer.Serialize(new {groups, resources}, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        }, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent());

        Assert.HasCount(128, result.Groups);
        Assert.HasCount(130, result.Resources);
        Assert.IsNull(result.Resources[128].GroupId);
        Assert.IsNull(result.Resources[129].GroupId);
        Assert.IsTrue(result.Groups.All(g => g.Title.Length == 160 && g.Summary!.Length == 500 &&
            g.Evidence.Count == 8 && g.Evidence.All(e => e.Length == 300)));
    }

    [TestMethod]
    public async Task GroupValidationPrecedesDeduplicationAndConflictingValidGroupsRemainSeparate()
    {
        var extractor = new PostDownloadInfoExtractor(new FakeLlm
        {
            ResponseText = """
                {"groups":[{"id":"main","title":"本体","kind":"main"},{"id":"preview","title":"预览","kind":"preview"}],
                 "resources":[
                  {"link":"https://example.test/shared","groupId":"missing","code":"code"},
                  {"link":"https://example.test/shared","groupId":"main","password":"archive"},
                  {"link":"https://example.test/shared","groupId":"preview"}]}
                """
        }, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent());

        Assert.HasCount(2, result.Resources);
        Assert.HasCount(2, result.Groups);
        Assert.AreEqual("main", result.Resources[0].GroupId);
        Assert.AreEqual("code", result.Resources[0].Code);
        Assert.AreEqual("archive", result.Resources[0].Password);
        Assert.AreEqual("preview", result.Resources[1].GroupId);
    }

    [TestMethod]
    public async Task LegacyAiResponsesWithoutGroupsKeepDownloadsUngrouped()
    {
        var result = await new PostDownloadInfoExtractor(new FakeLlm
        {
            ResponseText = """{"resources":[{"link":"https://example.test/file","groupId":"undeclared"}]}"""
        }, NullLogger<PostDownloadInfoExtractor>.Instance).ExtractAsync(new PostContent());
        Assert.HasCount(0, result.Groups);
        Assert.IsNull(result.Resources.Single().GroupId);
        Assert.AreEqual("https://example.test/file", result.Resources.Single().Link);
    }

    [TestMethod]
    public async Task RepeatedAiResourcesCombineComplementaryCredentialsBeforePersistence()
    {
        var extractor = new PostDownloadInfoExtractor(new FakeLlm
        {
            ResponseText = """
                {"resources":[
                    {"link":"https://pan.baidu.com/s/share","code":"abcd"},
                    {"link":"https://pan.baidu.com/s/share?pwd=abcd","password":"archive",
                     "extraction":{"requirement":"required","steps":[{"id":"open","op":"extractArchive","input":"download","password":"archive"}],"evidence":["password: archive"]}},
                    {"link":"https://pan.baidu.com/s/share","code":"abcd","password":"archive"}
                ]}
                """
        }, NullLogger<PostDownloadInfoExtractor>.Instance);

        var resource = (await extractor.ExtractAsync(new PostContent {MainHtml = "Repeated links"})).Resources.Single();
        Assert.AreEqual("https://pan.baidu.com/s/share", resource.Link);
        Assert.AreEqual("abcd", resource.Code);
        Assert.AreEqual("archive", resource.Password);
        Assert.AreEqual("archive", resource.Extraction!.Steps.Single().Password);
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
