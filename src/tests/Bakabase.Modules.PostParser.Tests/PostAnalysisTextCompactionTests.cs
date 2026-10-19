using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostAnalysisTextCompactionTests
{
    [TestMethod]
    public async Task ActualUserPromptKeepsDownloadTargetsCredentialsAndInstructionsWhileRemovingPresentation()
    {
        const string signedUrl = "https://example.test/%E4%B8%AD%20file.7z?sig=a%2Bb%2F%3D&pwd=x%20y#Key-A_b";
        const string magnet = "magnet:?xt=urn:btih:ABC123&dn=Book%20One&tr=https%3A%2F%2Ftracker.test%2Fannounce";
        const string imageUrl = "https://example.test/material.png?token=A%2BB&part=1#original";
        var layout = new string('x', 4000);
        var html = $$"""
            <div class="{{layout}}" style="padding: 10px; color: red" id="read_tpc">
            <style>.layout { background: red; }</style>
            <script>if (1 < 2) { var template = '<div>runtime template</div>'; }</script>
            <p>本体下载：<a class="button" target="_blank" href="{{signedUrl.Replace("&", "&amp;")}}" title="完整原档">点击下载</a></p>
            <p>磁力分流：<a href="{{magnet.Replace("&", "&amp;")}}">磁力链接</a></p>
            <p>提取码： Ab  Cd ；解压密码：  a&amp;amp;b  c  </p>
            <div>先将<span>part  01.bin</span>改名<span>outer  one.7z</span></div>
            <div>移动到 packages/第一卷，然后密码 first  pass 解压</div>
            <div>将产物改为.zip，再用 second  pass 解压，最后 third  pass 解压</div>
            <p>图片下载材料：<img src="{{imageUrl.Replace("&", "&amp;")}}" alt="扫描原图" title="图片材料" width="800" height="600"></p>
            </div>
            """;
        var content = new PostContent {Title = "中文帖子标题", MainHtml = html, SourceUrl = "https://forum.test/post?id=1&view=all", HtmlEncodingTrusted = true};

        var prompt = await ExtractPromptAsync(content);
        using var json = JsonDocument.Parse(prompt);
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        Assert.AreEqual(content.Title, json.RootElement.GetProperty("title").GetString());
        Assert.AreEqual(content.SourceUrl, json.RootElement.GetProperty("sourceUrl").GetString());
        StringAssert.Contains(prompt, "中文帖子标题");
        Assert.IsFalse(prompt.Contains("\\u4", StringComparison.OrdinalIgnoreCase), "The model should receive Chinese text, not literal Unicode escape sequences.");
        foreach (var exact in new[] {signedUrl, magnet, imageUrl, "点击下载", "完整原档", "扫描原图", "图片材料", "提取码： Ab  Cd ", "解压密码：  a&amp;b  c  "})
            StringAssert.Contains(text, exact);
        AssertInOrder(text, "part  01.bin", "outer  one.7z", "packages/第一卷", "first  pass", "second  pass", "third  pass");
        Assert.IsFalse(text.Contains(layout, StringComparison.Ordinal));
        StringAssert.Contains(text, "<script>if (1 < 2) { var template = '<div>runtime template</div>'; }</script>");
        StringAssert.Contains(text, "<style>.layout { background: red; }</style>");
        Assert.IsFalse(text.Contains("padding:", StringComparison.Ordinal));
        Assert.IsTrue(prompt.Length < html.Length / 2, $"Expected clear compaction: {prompt.Length} prompt characters from {html.Length} source characters.");
    }

    [TestMethod]
    public async Task AvailabilityKeepsEveryCommentIdentityOrderDateAndQuotedContext()
    {
        var comments = Enumerable.Range(1, 240).Select(i => new PostComment
        {
            Id = $"reply_{i}", Floor = i.ToString(), Author = $"作者 {i}",
            PostedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
            Html = i == 2 ? "<div>链接失效了</div>" : i == 239
                ? "<blockquote>链接失效了</blockquote><p>已重新上传，解压密码： late  password </p>"
                : $"<div class='comment'>普通回复 {i}，谢谢分享</div>"
        }).ToList();
        var llm = new CapturingLlm("""{"status":"unknown","evidence":[],"reason":"test"}""");
        await new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent
        {
            Title = "首楼", MainHtml = "<p>下载信息</p>", Scope = "firstPage", Comments = comments, HtmlEncodingTrusted = true,
            CommentHtmlList = ["The fallback must not duplicate structured comments"]
        });

        using var json = JsonDocument.Parse(llm.UserPrompt!);
        Assert.AreEqual("firstPage", json.RootElement.GetProperty("scope").GetString());
        var captured = json.RootElement.GetProperty("comments").EnumerateArray().ToArray();
        Assert.HasCount(comments.Count, captured);
        for (var i = 0; i < comments.Count; i++)
        {
            Assert.AreEqual(comments[i].Id, captured[i].GetProperty("Id").GetString());
            Assert.AreEqual(comments[i].Floor, captured[i].GetProperty("Floor").GetString());
            Assert.AreEqual(comments[i].Author, captured[i].GetProperty("Author").GetString());
            Assert.AreEqual(comments[i].PostedAt, captured[i].GetProperty("PostedAt").GetDateTimeOffset());
        }
        StringAssert.Contains(captured[1].GetProperty("Html").GetString()!, "链接失效了");
        StringAssert.Contains(captured[238].GetProperty("Html").GetString()!, "<blockquote>链接失效了</blockquote>");
        StringAssert.Contains(captured[238].GetProperty("Html").GetString()!, "late  password ");
        StringAssert.Contains(captured[^1].GetProperty("Html").GetString()!, "普通回复 240");
        Assert.IsFalse(llm.UserPrompt!.Contains("fallback must not", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PlainTextPasswordsAngleBracketsAndEntityLookingValuesRemainExact()
    {
        const string text = "  密码： a&amp;amp;b  c <archive>.7z\r\n把 a<b 改成 a>b；然后 first  pass 解压\nhttps://example.test/a?pwd=x%20y&amp;literal=1#frag  ";
        const string comment = "  无关键词的补充  <秘密>.txt  &nbsp;  ";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = text, CommentHtmlList = [comment], HtmlEncodingTrusted = true}));
        Assert.AreEqual(text, json.RootElement.GetProperty("mainHtml").GetString());
        Assert.AreEqual(comment, json.RootElement.GetProperty("comments")[0].GetString());
    }

    [TestMethod]
    public async Task PastedTextScopePreservesEvenBalancedLiteralHtmlAndEntities()
    {
        const string text = "  解压密码： literal<b>x</b>&amp;amp;  &nbsp; \r\n然后改名 <div>one  two.zip</div>  ";
        const string comment = "  第二轮密码 <code>a&amp;b</code>  ";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent
        {
            MainHtml = text, Scope = "pastedText", Comments = [new() {Html = comment}], HtmlEncodingTrusted = true
        }));
        Assert.AreEqual(text, json.RootElement.GetProperty("mainHtml").GetString());
        Assert.AreEqual(comment, json.RootElement.GetProperty("comments")[0].GetProperty("Html").GetString());
    }

    [TestMethod]
    public async Task HtmlEntitiesAreDecodedOnceWithoutUrlDecodingOrWhitespaceCollapse()
    {
        const string html = """
            <pre>  a&amp;amp;b&#32;&#32;c&#9;d&#10;last  </pre>
            <p>文件名：&lt;archive&gt;  one.zip；密码：&amp;lt;abc&amp;gt;</p>
            <a href="https://example.test/file?literal=&amp;amp;encoded=%26amp%3B&amp;sig=a%2Bb#K%2Fy">获取</a>
            """;
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        StringAssert.Contains(text, "<pre>  a&amp;b  c\td\nlast  </pre>");
        StringAssert.Contains(text, "文件名：<archive>  one.zip；密码：&lt;abc&gt;");
        StringAssert.Contains(text, "https://example.test/file?literal=&amp;encoded=%26amp%3B&sig=a%2Bb#K%2Fy");
    }

    [TestMethod]
    public async Task ImageAlternatesAndAttributeOnlyDownloadsRemainPresentBesideTheirLabels()
    {
        const string html = """
            <div><a href="/download/a.zip?token=%2F%2B#Keep" download="书籍  第一卷.zip">第一卷</a>
            <img src="https://example.test/preview.png" data-src="https://example.test/full.png?x=1&amp;y=2#raw" srcset="https://example.test/one.png 1x, https://example.test/two.png 2x" alt="原始图片" title="材料说明">
            <button data-clipboard-text="magnet:?xt=urn:btih:XYZ&amp;dn=A%20B" data-password="  hidden  pass  ">复制完整链接</button>
            <input value=" code  123 " title="访问密码">
            </div>
            """;
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        foreach (var exact in new[] {"/download/a.zip?token=%2F%2B#Keep", "书籍  第一卷.zip", "https://example.test/preview.png",
                     "https://example.test/full.png?x=1&y=2#raw", "https://example.test/one.png 1x, https://example.test/two.png 2x",
                     "原始图片", "材料说明", "magnet:?xt=urn:btih:XYZ&dn=A%20B", "  hidden  pass  ", " code  123 "})
            StringAssert.Contains(text, exact);
        AssertInOrder(text, "第一卷", "/download/a.zip", "原始图片", "复制完整链接", "magnet:?");
    }

    [TestMethod]
    public async Task NonPresentationAttributesKeepRelativeDownloadsAndCopyOnlyPasswords()
    {
        const string html = """
            <form action="/download/archive?code=a%20b#keep"><input name="archive-password" value="  a  b  ">
            <button formaction="../mirror/file.7z?sig=%2B#Key" onclick="copy(' a  b ')">复制密码</button>
            <object data="/materials/original.png?raw=1#image"></object>
            <span password="  not-a-data-attribute  ">辅助信息</span></form>
            """;
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        foreach (var exact in new[] {"/download/archive?code=a%20b#keep", "../mirror/file.7z?sig=%2B#Key", "/materials/original.png?raw=1#image", "copy(' a  b ')", "  a  b  ", "  not-a-data-attribute  "})
            StringAssert.Contains(text, exact);
    }

    [TestMethod]
    public async Task ScriptUrlsPasswordsAndJsonRemainRawDataWithTheirBoundaries()
    {
        const string script = """
            const download = "https://example.test/file?literal=&amp;sig=a%2Bb#Key%2Fx";
            const password = "  a&amp;amp;b  <b>literal</b>  ";
            const metadata = {"code":" Ab  Cd ","next":"magnet:?xt=urn:btih:ABC&dn=One%20Two"};
            """;
        var html = $"<div class='layout'><p>正文</p><script type='application/json'>{script}</script><p>后续改名 outer  one.zip</p></div>";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        StringAssert.Contains(text, "<script>" + script + "</script>");
        StringAssert.Contains(text, "type=application/json");
        AssertInOrder(text, "正文", "<script>", "https://example.test/file?literal=&amp;sig=a%2Bb#Key%2Fx", "  a&amp;amp;b  <b>literal</b>  ", "</script>", "后续改名 outer  one.zip");
    }

    [TestMethod]
    public async Task StyleUrlsAndCommentCredentialsRemainRawDataWithTheirBoundaries()
    {
        const string style = """
            .material { background-image: url('https://example.test/material.png?literal=&amp;sig=a%2Bb#Original'); }
            /* 访问码： Ab  Cd ；密码：  a&amp;amp;b  <tag>  */
            """;
        var html = $"<div><p>图片材料</p><style>{style}</style><p>之后密码 second  pass 解压</p></div>";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        StringAssert.Contains(text, "<style>" + style + "</style>");
        AssertInOrder(text, "图片材料", "<style>", "https://example.test/material.png?literal=&amp;sig=a%2Bb#Original", "密码：  a&amp;amp;b  <tag>  ", "</style>", "second  pass");
    }

    [TestMethod]
    public async Task MissingClosingElementsStillPreserveMalformedPostContents()
    {
        const string html = "<div><p>第一步 outer  one.bin 改为.7z<a href='https://example.test/a?x=%20#key'>下载<p>第二步  a  b 解压";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        Assert.AreEqual(html, text, "Unbalanced markup is ambiguous and must retain the complete source.");
    }

    [TestMethod]
    public async Task HtmlCommentDownloadsAndPasswordsRemainAvailableWithTheirCommentContext()
    {
        const string html = "<div>请看源码<!-- 下载 https://example.test/hidden?sig=a%2Bb&amp;code=x#Secret 密码：  a&amp;amp;b  --><p>第二步改名 outer  two.7z</p></div>";
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        StringAssert.Contains(text, "<!-- 下载 https://example.test/hidden?sig=a%2Bb&amp;code=x#Secret 密码：  a&amp;amp;b  -->");
        AssertInOrder(text, "请看源码", "https://example.test/hidden", "第二步改名 outer  two.7z");
    }

    [TestMethod]
    public async Task QuoteAndStrikethroughPresentationKeepTheirMeaningAndBoundaries()
    {
        const string html = """
            <div class="quote forum-layout"><span style="text-decoration: line-through; color: red">旧链接 https://example.test/old#key</span>之前报告失效</div>
            <p>作者现已补档 <a href="https://example.test/new?pwd=a%20b#fresh">新链接</a></p>
            """;
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = html, HtmlEncodingTrusted = true}));
        var text = json.RootElement.GetProperty("mainHtml").GetString()!;
        AssertInOrder(text, "<div>", "class=quote forum-layout", "<span>", "text-decoration: line-through", "https://example.test/old#key", "</span>", "之前报告失效", "</div>", "作者现已补档", "https://example.test/new?pwd=a%20b#fresh");
    }

    [TestMethod]
    [DataRow("<div>密码：  a  b <a href=\"https://example.test/file?token=1#key 后续还有密码  secret  ")]
    [DataRow("<div>文件名 <archive>.zip，然后密码  one  two 解压</div>")]
    [DataRow("<div>可见密码  abc  </div><script>download='https://example.test/file'; missing end")]
    [DataRow("literal<b>")]
    [DataRow("  密码： literal<b>  next  ")]
    public async Task AmbiguousOrTruncatedMarkupFallsBackWithoutLosingSource(string source)
    {
        using var json = JsonDocument.Parse(await ExtractPromptAsync(new PostContent {MainHtml = source, HtmlEncodingTrusted = true}));
        Assert.AreEqual(source, json.RootElement.GetProperty("mainHtml").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HistoricalSnapshotsKeepTheirAlreadyDecodedHtmlAndCommentCredentials(bool useNewtonsoft)
    {
        const string body = "<p>旧快照密码：  a&amp;b  </p><a href='https://example.test/?literal=&amp;sig=%252B#key'>下载</a>";
        const string comment = "<p>旧回复密码：  c&amp;d  </p>";
        var snapshot = JsonSerializer.Serialize(new
        {
            Title = "历史帖子", MainHtml = body, Scope = "firstPage", Comments = new[] {new PostComment {Html = comment}}
        });
        var content = useNewtonsoft
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<PostContent>(snapshot)!
            : JsonSerializer.Deserialize<PostContent>(snapshot)!;
        Assert.IsFalse(content.HtmlEncodingTrusted);
        using var prompt = JsonDocument.Parse(await ExtractPromptAsync(content));
        Assert.AreEqual(body, prompt.RootElement.GetProperty("mainHtml").GetString());
        Assert.AreEqual(comment, prompt.RootElement.GetProperty("comments")[0].GetProperty("Html").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NewSnapshotsLoseRuntimeTrustOnSerializationAndKeepTheirSourceOnRestore(bool useNewtonsoft)
    {
        var fresh = new PostContent {MainHtml = "<p>新密码：  a&amp;amp;b  </p>", HtmlEncodingTrusted = true};
        using (var freshPrompt = JsonDocument.Parse(await ExtractPromptAsync(fresh)))
            StringAssert.Contains(freshPrompt.RootElement.GetProperty("mainHtml").GetString()!, "新密码：  a&amp;b  ");
        var snapshot = useNewtonsoft ? Newtonsoft.Json.JsonConvert.SerializeObject(fresh) : JsonSerializer.Serialize(fresh);
        Assert.IsFalse(snapshot.Contains(nameof(PostContent.HtmlEncodingTrusted), StringComparison.OrdinalIgnoreCase));
        var restored = useNewtonsoft
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<PostContent>(snapshot)!
            : JsonSerializer.Deserialize<PostContent>(snapshot)!;
        Assert.IsFalse(restored.HtmlEncodingTrusted);
        using var restoredPrompt = JsonDocument.Parse(await ExtractPromptAsync(restored));
        Assert.AreEqual(fresh.MainHtml, restoredPrompt.RootElement.GetProperty("mainHtml").GetString());
    }

    private static async Task<string> ExtractPromptAsync(PostContent content)
    {
        var llm = new CapturingLlm("""{"resources":[]}""");
        await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance).ExtractAsync(content);
        Assert.AreEqual(1, llm.Calls);
        return llm.UserPrompt!;
    }

    private static void AssertInOrder(string text, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var index = text.IndexOf(value, previous + 1, StringComparison.Ordinal);
            Assert.IsTrue(index > previous, $"Expected '{value}' after position {previous} in: {text}");
            previous = index;
        }
    }

    private sealed class CapturingLlm(string response) : ILlmService
    {
        public string? UserPrompt;
        public int Calls;
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Calls++;
            UserPrompt = messages.Single(m => m.Role == ChatRole.User).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature, IList<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
