using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.PostParser.Tests;

public partial class PostDownloadInfoExtractorTests
{
    [TestMethod]
    public async Task LostSubtitleQualifierUsesOriginalTitleWithoutChangingAcquisitionInputs()
    {
        const string original = "[个人汉化][lrc][RJ123456]作品名[8KB][百度]";
        var llm = new FakeLlm {ResponseText = """
            {"title":"作品名 汉化版","groups":[{"id":"main","title":"作品名 汉化版","kind":"main","summary":"完整汉化资源","evidence":["百度分享"]}],
             "resources":[{"link":"https://example.test/share","groupId":"main","code":"abcd","password":"first","extraction":{"requirement":"required","steps":[{"id":"one","op":"extractArchive","input":"download","password":"first"},{"id":"two","op":"extractArchive","input":"one","password":"second"}]}}]}
            """};
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {Title = original, MainHtml = "百度分享；两层解压"});

        Assert.AreEqual(original, result.Title);
        Assert.AreEqual(original, result.Groups.Single().Title);
        Assert.AreEqual(original, result.Groups.Single().Summary);
        Assert.AreEqual(original, result.Groups.Single().Evidence[0]);
        Assert.AreEqual("https://example.test/share", result.Resources.Single().Link);
        Assert.AreEqual("abcd", result.Resources.Single().Code);
        Assert.AreEqual("second", result.Resources.Single().Extraction!.Steps[1].Password);
        Assert.AreEqual("main", result.Groups.Single().Kind, "The main content of a post may itself be subtitles; do not invent a different scope.");
        StringAssert.Contains(llm.Prompt!, "有补丁不等于只有补丁");
        StringAssert.Contains(llm.Prompt!, "每条范围限定只应用到有原文依据的内容分组");
        Assert.AreEqual(1, llm.Calls);
    }

    [TestMethod]
    [DataRow("作品名（仅字幕）")]
    [DataRow("作品名 [不含本体][汉化补丁]")]
    [DataRow("作品名 [差分]")]
    [DataRow("作品名 [试玩版]")]
    [DataRow("作品名 [部分章节]")]
    [DataRow("Example [subtitles only]")]
    [DataRow("Example [patch only]")]
    [DataRow("Example [Demo]")]
    public async Task ExplicitTitleScopesSurviveModelTitleSimplification(string title)
    {
        var result = await new PostDownloadInfoExtractor(new FakeLlm {ResponseText = "{\"title\":\"作品名\",\"resources\":[]}"},
                NullLogger<PostDownloadInfoExtractor>.Instance).ExtractAsync(new PostContent {Title = title});
        Assert.AreEqual(title, result.Title);
    }

    [TestMethod]
    public async Task MainAndSubtitleGroupsAreNotRewrittenFromTheGlobalTitle()
    {
        var llm = new FakeLlm {ResponseText = """
            {"title":"作品名","groups":[{"id":"audio","title":"音频本体","kind":"main","summary":"完整音频"},{"id":"subs","title":"LRC字幕","kind":"supplement","summary":"单独字幕"}],
            "resources":[{"link":"https://example.test/audio","groupId":"audio"},{"link":"https://example.test/subtitles","groupId":"subs"}]}
            """};
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {Title = "作品名 [本体+字幕]"});
        Assert.AreEqual("作品名 [本体+字幕]", result.Title);
        Assert.AreEqual("音频本体", result.Groups[0].Title);
        Assert.AreEqual("完整音频", result.Groups[0].Summary);
        Assert.AreEqual("单独字幕", result.Groups[1].Summary);
        Assert.HasCount(2, result.Resources);
    }

    [TestMethod]
    public async Task SmallSizeAndFileExtensionDoNotInferSubtitleOnlyScope()
    {
        var result = await new PostDownloadInfoExtractor(new FakeLlm {ResponseText = """
            {"title":"作品名 汉化版","resources":[{"link":"https://example.test/subtitles.lrc"}]}
            """}, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {Title = "作品名 [8KB]", MainHtml = "https://example.test/subtitles.lrc"});
        Assert.AreEqual("作品名 汉化版", result.Title);
        Assert.HasCount(0, result.Groups);
        Assert.AreEqual("https://example.test/subtitles.lrc", result.Resources.Single().Link);
    }

    [TestMethod]
    public async Task SourceScopeWordingAndBoundedFallbackRetainCompleteClauses()
    {
        var response = new {title = "作品名 仅字幕", groups = new[] {new {id = "subs", title = "作品名 仅字幕", kind = "main", summary = "仅字幕，双层解压"}},
            resources = new[] {new {link = "https://example.test/share", groupId = "subs"}}};
        var llm = new FakeLlm {ResponseText = JsonSerializer.Serialize(response)};
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent {Title = "[仅字幕] 作品名"});
        Assert.AreEqual("[仅字幕] 作品名", result.Title);
        Assert.AreEqual("[仅字幕] 作品名", result.Groups.Single().Summary);

        result = await extractor.ExtractAsync(new PostContent {Title = new string('长', 550) + " [LRC]"});
        Assert.IsTrue(result.Groups.Single().Title.Length <= 160);
        Assert.IsTrue(result.Groups.Single().Summary!.Length <= 500);
        Assert.IsTrue(result.Groups.Single().Evidence.All(e => e.Length <= 300));
        Assert.IsTrue(result.Title!.Length <= 512);
        StringAssert.Contains(result.Groups.Single().Title, "LRC");
        StringAssert.Contains(result.Groups.Single().Summary!, "LRC");
    }

    [TestMethod]
    [DataRow("作品名 [本体+字幕]")]
    [DataRow("作品名（含字幕）")]
    [DataRow("作品名（不含字幕）")]
    [DataRow("作品名（不含本体，只有字幕）")]
    [DataRow("Example [with subtitles]")]
    [DataRow("Example [without subtitles]")]
    [DataRow("Example [not subtitles only]")]
    public async Task NegationAndCompositionCannotBeReplacedWithSubtitleOnly(string original)
    {
        var llm = new FakeLlm {ResponseText = """
            {"title":"作品名 仅字幕","groups":[{"id":"main","title":"仅字幕","kind":"main","summary":"仅字幕 subtitles only"}],
             "resources":[{"link":"https://example.test/share","groupId":"main"}]}
            """};
        var extractor = new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance);
        var result = await extractor.ExtractAsync(new PostContent {Title = original});
        Assert.AreEqual(original, result.Title);
        Assert.AreEqual(original, result.Groups.Single().Title);
        Assert.AreEqual(original, result.Groups.Single().Summary);

        result = await extractor.ExtractAsync(new PostContent {Title = new string('长', 550) + " [" + original + "]"});
        Assert.IsTrue(result.Title!.Length <= 512);
        Assert.IsFalse(result.Title.Contains("仅字幕", StringComparison.Ordinal));
        var expectedClause = original.Contains("[", StringComparison.Ordinal)
            ? original[(original.LastIndexOf('[') + 1)..original.LastIndexOf(']')]
            : original[(original.IndexOf('（') + 1)..original.LastIndexOf('）')];
        // The Chinese compound example has two complete clauses separated by a comma.
        foreach (var clause in expectedClause.Split('，')) StringAssert.Contains(result.Title, clause);
    }

    [TestMethod]
    public async Task OverlongUnbrokenQualifierClauseRequestsReviewRatherThanCuttingItsMeaning()
    {
        var result = await new PostDownloadInfoExtractor(new FakeLlm {ResponseText = "{\"title\":\"仅字幕\"}"},
                NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {Title = "本体含字幕" + new string('长', 550) + "但不是仅字幕"});
        Assert.AreEqual("资源范围需核对（请查看原帖完整标题）", result.Title);
        Assert.IsTrue(result.Title!.Length <= 512);
    }
}
