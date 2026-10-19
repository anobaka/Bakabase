using System.Net;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public class PostParserInputPreservationTests
{
    private const string EncodedBody = """
        <p>解压密码：  a&amp;amp;b  c&#32;&#32;d  </p>
        <p>文件名：&lt;outer&gt;  one.bin；先改后缀.7z，密码 first  pass 解压</p>
        <a href="https://download.test/archive?literal=&amp;amp;token=%252B&amp;sig=a%2Bb#Key%2Fx" title="下载本体">获取文件</a>
        <input value="  a&amp;amp;b  &quot;q&quot;  " title="备用密码">
        """;

    [TestMethod]
    public async Task GenericHttpReaderThroughExtractorDecodesTextAndUrlAttributesExactlyOnce()
    {
        const string embedded = """
            <script>const address="https://example.test/script?literal=&amp;sig=a%2Bb#Key"; const password="  a&amp;amp;b  ";</script>
            <style>.original { background: url('https://example.test/style.png?literal=&amp;#raw'); } /* 密码：  c&amp;amp;d  */</style>
            <noscript><a href="https://example.test/fallback?literal=&amp;amp;#keep">备用下载，密码：  e&amp;amp;f  </a></noscript>
            """;
        var factory = new HtmlClientFactory($"<html><head><title>中文标题</title></head><body>{EncodedBody}{embedded}</body></html>");
        var reader = new LegacyPostContentService([new GenericHtmlReader(factory, NullLogger<GenericHtmlReader>.Instance)]);
        var content = await reader.ReadAsync(" https://forum.test/post/1 ");
        Assert.IsTrue(content.HtmlEncodingTrusted);
        using var json = JsonDocument.Parse(await ExtractPromptAsync(content));
        Assert.AreEqual("https://forum.test/post/1", content.SourceUrl);
        Assert.AreEqual("中文标题", json.RootElement.GetProperty("title").GetString());
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        AssertPreservedBody(text);
        StringAssert.Contains(text, "<script>const address=\"https://example.test/script?literal=&amp;sig=a%2Bb#Key\"; const password=\"  a&amp;amp;b  \";</script>");
        StringAssert.Contains(text, "<style>.original { background: url('https://example.test/style.png?literal=&amp;#raw'); } /* 密码：  c&amp;amp;d  */</style>");
        StringAssert.Contains(text, "https://example.test/fallback?literal=&amp;#keep");
        StringAssert.Contains(text, "备用下载，密码：  e&amp;f  ");
        Assert.AreEqual(1, factory.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SoulPlusReaderPreservesMainAndStructuredOrFallbackCommentsThroughExtractor(bool fallbackComments)
    {
        var html = $$"""
            <html><head><title>帖子</title></head><body><h1 id="subject_tpc">中文主题</h1>
            <table><tr><td><div class="tiptop">B0F 2026-10-19 10:00:00</div><a href="showuid.php?uid=1">主楼作者</a>
            <div class="tpc_content"><div class="f14" id="read_tpc">{{EncodedBody}}</div></div></td></tr></table>
            <table><tr><td><div class="tiptop">B12F 2026-10-19 10:10:00</div><a href="showuid.php?uid=2">补档作者</a>
            <div class="tpc_content"><div class="f14" id="read_12"><p>补档密码：  c&amp;amp;d  e  </p>
            <a href="https://download.test/mirror?literal=&amp;amp;signature=%252B&amp;code=x%20y#Keep">补档链接</a></div></div></td></tr></table>
            </body></html>
            """;
        var client = new FixtureSoulPlusClient(html);
        var legacy = await new SoulPlusContentFetcher(client).ReadAsync("https://www.north-plus.net/read.php?tid=1", default);
        var content = LegacyPostContentService.ToContent(legacy, "SoulPlus");
        Assert.IsTrue(legacy.HtmlEncodingTrusted);
        Assert.IsTrue(content.HtmlEncodingTrusted);
        if (fallbackComments) content = content with {Comments = []};
        using var json = JsonDocument.Parse(await ExtractPromptAsync(content));
        AssertPreservedBody(json.RootElement.GetProperty("mainHtml").GetString()!);
        var comment = json.RootElement.GetProperty("comments")[0];
        var text = fallbackComments ? comment.GetString()! : comment.GetProperty("Html").GetString()!;
        StringAssert.Contains(text, "补档密码：  c&amp;d  e  ");
        StringAssert.Contains(text, "https://download.test/mirror?literal=&amp;signature=%252B&code=x%20y#Keep");
        if (!fallbackComments)
        {
            Assert.AreEqual("read_12", comment.GetProperty("Id").GetString());
            Assert.AreEqual("12", comment.GetProperty("Floor").GetString());
            Assert.AreEqual("补档作者", comment.GetProperty("Author").GetString());
            Assert.AreEqual(new DateTimeOffset(2026, 10, 19, 10, 10, 0, TimeSpan.FromHours(8)), comment.GetProperty("PostedAt").GetDateTimeOffset());
        }
        Assert.AreEqual(1, client.Calls);
    }

    [TestMethod]
    public async Task PlainTextReaderAndAdapterPreserveBoundarySpacesBalancedTagsAndEntityLookingPasswords()
    {
        const string source = "  literal<b>x</b>&amp;amp;  \r\n把 <code>outer  one.zip</code> 改名；密码：  final  pass  ";
        var content = await new LegacyPostContentService([new PlainTextReader()]).ReadAsync(source);
        using var json = JsonDocument.Parse(await ExtractPromptAsync(content));
        Assert.AreEqual("pastedText", content.Scope);
        Assert.AreEqual(source, json.RootElement.GetProperty("mainHtml").GetString());
    }

    [TestMethod]
    public async Task SavedPastedInputAndDirectWorkflowReadKeepOriginalTextAndMarkItsScope()
    {
        const string source = "  literal<b>x</b>&amp;amp;  \r\nouter  one.zip\n密码：  final  pass  ";
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var servicesLifetime = (IAsyncDisposable)services;
        services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = false;
        await using var scope = services.CreateAsyncScope();
        var taskService = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        await taskService.AddInputs([], [], [], source, "Text title");
        var saved = (await taskService.GetAll()).Single();
        Assert.AreEqual(source, saved.Text);
        var input = new PostParserInput {Text = saved.Text, Title = saved.Title};
        var outcome = await new ReadPostContentActivity().ProcessItemAsync(new WorkflowExecutionContext
        {
            RunId = 1, WorkflowDefinitionId = 1, TriggerKind = PostParserWorkflow.Trigger, Payload = input,
            ActivityConfigJson = "{}", Services = scope.ServiceProvider, Logger = NullLogger.Instance
        }, input, default);
        var content = ((PostParserContentItem)outcome.Replacement!).Content;
        Assert.AreEqual("pastedText", content.Scope);
        using var json = JsonDocument.Parse(await ExtractPromptAsync(content));
        Assert.AreEqual(source, json.RootElement.GetProperty("mainHtml").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkflowSnapshotRestoreNeverReparsesHistoricalOrFreshHtml(bool wasFresh)
    {
        var body = wasFresh ? "<p>密码：  a&amp;amp;b  </p>" : "<p>密码：  a&amp;b  </p>";
        var comment = wasFresh ? "<p>补档密码：  c&amp;amp;d  </p>" : "<p>补档密码：  c&amp;d  </p>";
        var item = new PostParserContentItem(new PostParserInput {Link = "https://forum.test/post/1"}, new PostContent
        {
            MainHtml = body, Comments = [new() {Floor = "12", Author = "作者", Html = comment}], HtmlEncodingTrusted = wasFresh
        });
        var saved = WorkflowItemSnapshot.Capture(item);
        Assert.IsFalse(saved.Contains(nameof(PostContent.HtmlEncodingTrusted), StringComparison.OrdinalIgnoreCase));
        var restored = (PostParserContentItem)WorkflowItemSnapshot.Restore(saved);
        Assert.IsFalse(restored.Content.HtmlEncodingTrusted);
        using var prompt = JsonDocument.Parse(await ExtractPromptAsync(restored.Content));
        Assert.AreEqual(body, prompt.RootElement.GetProperty("mainHtml").GetString());
        Assert.AreEqual(comment, prompt.RootElement.GetProperty("comments")[0].GetProperty("Html").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacySnapshotSerializationDoesNotPersistOrGrantHtmlEncodingTrust(bool useNewtonsoft)
    {
        var fresh = new Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostContent
        {
            Title = "帖子", MainHtml = "<p>  a&amp;amp;b  </p>", HtmlEncodingTrusted = true
        };
        var saved = useNewtonsoft ? Newtonsoft.Json.JsonConvert.SerializeObject(fresh) : JsonSerializer.Serialize(fresh);
        Assert.IsFalse(saved.Contains(nameof(PostContent.HtmlEncodingTrusted), StringComparison.OrdinalIgnoreCase));
        var restored = useNewtonsoft
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostContent>(saved)!
            : JsonSerializer.Deserialize<Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostContent>(saved)!;
        Assert.IsFalse(restored.HtmlEncodingTrusted);
        Assert.IsFalse(LegacyPostContentService.ToContent(restored).HtmlEncodingTrusted);
        Assert.AreEqual(fresh.MainHtml, restored.MainHtml);
    }

    private static void AssertPreservedBody(string text)
    {
        StringAssert.Contains(text, "解压密码：  a&amp;b  c  d  ");
        StringAssert.Contains(text, "文件名：<outer>  one.bin；先改后缀.7z，密码 first  pass 解压");
        StringAssert.Contains(text, "https://download.test/archive?literal=&amp;token=%252B&sig=a%2Bb#Key%2Fx");
        StringAssert.Contains(text, "  a&amp;b  \"q\"  ");
        Assert.IsFalse(text.Contains("a&b", StringComparison.Ordinal), "The literal entity-looking password must not be decoded twice.");
    }

    private static async Task<string> ExtractPromptAsync(PostContent content)
    {
        var llm = new CapturingLlm();
        await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance).ExtractAsync(content);
        return llm.UserPrompt!;
    }

    private sealed class HtmlClientFactory(string html) : IHttpClientFactory
    {
        public int Calls;
        public HttpClient CreateClient(string name) => new(new Handler(() => Calls++, html));
        private sealed class Handler(Action onSend, string html) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                onSend();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(html)});
            }
        }
    }

    private sealed class SoulPlusOptionsProvider : IBOptions<ISoulPlusOptions>
    {
        public ISoulPlusOptions Value { get; } = new SoulPlusOptions {Cookie = "fixture", RequestInterval = 0};
    }

    private sealed class FixtureSoulPlusClient(string html)
        : SoulPlusClient(new HtmlClientFactory(html), NullLoggerFactory.Instance, new SoulPlusOptionsProvider())
    {
        public int Calls;
        protected override Task<(int StatusCode, string Text)> SendAsync(string url, Dictionary<string, string> headers, string preset)
        {
            Calls++;
            return Task.FromResult((200, html));
        }
    }

    private sealed class CapturingLlm : ILlmService
    {
        public string? UserPrompt;
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            UserPrompt = messages.Single(m => m.Role == ChatRole.User).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"resources":[]}""")));
        }
        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature, IList<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
