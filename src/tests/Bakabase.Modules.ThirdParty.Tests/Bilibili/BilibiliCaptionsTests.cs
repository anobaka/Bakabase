using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliCaptionsTests
{
    private static List<DmView.TSubtitleItem> Subtitles(string fixture) =>
        BilibiliFixtures.Data<DmView>(fixture).Subtitle!.Subtitles!;

    [TestMethod]
    public void ChineseHumanTrackIsPrimaryOtherHumanLanguagesAreExtras()
    {
        var plans = BilibiliCaptions.PlanSubtitles(Subtitles("dmview-subs.json"));
        Assert.AreEqual(2, plans.Count, "AI track, duplicate zh-Hans and the URL-less track are dropped");

        Assert.AreEqual("", plans[0].FileSuffix);
        Assert.AreEqual("zh-Hans", plans[0].Item.Lan);
        Assert.IsFalse(plans[0].IsAi);
        Assert.AreEqual("https://aisubtitle.hdslb.com/bfs/subtitle/0002.json?auth_key=1-0-0-0", plans[0].Url);

        Assert.AreEqual(".en-us", plans[1].FileSuffix);
        Assert.AreEqual("en-US", plans[1].Item.Lan);
    }

    [TestMethod]
    public void WithoutAChineseTrackTheFirstHumanTrackIsPrimary()
    {
        var plans = BilibiliCaptions.PlanSubtitles([
            new DmView.TSubtitleItem {Lan = "en-US", SubtitleUrl = "//h/1.json"},
            new DmView.TSubtitleItem {Lan = "fr", SubtitleUrl = "//h/2.json"},
            new DmView.TSubtitleItem {Lan = "zh-Hant", SubtitleUrl = "//h/3.json"},
        ]);
        CollectionAssert.AreEqual(new[] {"zh-Hant", "en-US", "fr"}, plans.Select(p => p.Item.Lan).ToArray(),
            "any zh* before the first human track");
        CollectionAssert.AreEqual(new[] {"", ".en-us", ".fr"}, plans.Select(p => p.FileSuffix).ToArray());

        var noZh = BilibiliCaptions.PlanSubtitles([
            new DmView.TSubtitleItem {Lan = "en-US", SubtitleUrl = "//h/1.json"},
            new DmView.TSubtitleItem {Lan = "fr", SubtitleUrl = "//h/2.json"},
        ]);
        Assert.AreEqual("en-US", noZh[0].Item.Lan);
        Assert.AreEqual(0, BilibiliCaptions.PlanSubtitles(null).Count);
    }

    [TestMethod]
    public void AnAiTrackIsPrimaryOnlyWhenItsBodyConfirmsTheLanguage()
    {
        var plans = BilibiliCaptions.PlanSubtitles(Subtitles("dmview-ai-only.json"));
        var plan = plans.Single();
        Assert.IsTrue(plan.IsAi);
        Assert.AreEqual("ai-zh", plan.Item.Lan);

        Assert.AreEqual("", BilibiliCaptions.ResolveFileSuffix(plan, BilibiliFixtures.Load<SubtitleBody>("subtitle-ai-zh.json")));
        // The ai-zh label was wrong: the body says Thai.
        Assert.AreEqual(".ai-th", BilibiliCaptions.ResolveFileSuffix(plan, BilibiliFixtures.Load<SubtitleBody>("subtitle-ai.json")));
        Assert.AreEqual(".ai-und", BilibiliCaptions.ResolveFileSuffix(plan, new SubtitleBody()));

        var human = BilibiliCaptions.PlanSubtitles(Subtitles("dmview-subs.json"))[1];
        Assert.AreEqual(".en-us", BilibiliCaptions.ResolveFileSuffix(human, BilibiliFixtures.Load<SubtitleBody>("subtitle-ai.json")));
    }

    [TestMethod]
    public void HumanSrt()
    {
        Assert.AreEqual(
            "1\n00:00:00,130 --> 00:00:00,670\n你好\n\n" +
            "2\n00:00:00,670 --> 00:00:00,670\n结束早于开始\n\n" +
            "3\n00:00:02,000 --> 00:00:03,250\n第一行\n第二行\n\n" +
            "4\n01:02:05,500 --> 01:02:07,000\n一小时以后\n\n",
            BilibiliCaptions.ToSrt(BilibiliFixtures.Load<SubtitleBody>("subtitle-human.json")));
    }

    [TestMethod]
    public void AiSrtWithoutLocation()
    {
        Assert.AreEqual(
            "1\n00:00:00,000 --> 00:00:01,500\nสวัสดี\n\n2\n00:00:01,500 --> 00:00:02,250\nครับ\n\n",
            BilibiliCaptions.ToSrt(BilibiliFixtures.Load<SubtitleBody>("subtitle-ai.json")));
    }

    [TestMethod]
    public void SrtHoursMayExceedADayAndNegativeTimesClamp()
    {
        var body = new SubtitleBody
        {
            Body = [new SubtitleBody.TLine {From = 90000, To = 90001.25, Content = "x"}, new SubtitleBody.TLine {From = -1, To = -0.5, Content = "y"}],
        };
        Assert.AreEqual("1\n00:00:00,000 --> 00:00:00,000\ny\n\n2\n25:00:00,000 --> 25:00:01,250\nx\n\n",
            BilibiliCaptions.ToSrt(body));
        Assert.AreEqual("", BilibiliCaptions.ToSrt(new SubtitleBody()));
    }

    [TestMethod]
    public void NormalizeUrl()
    {
        Assert.AreEqual("https://h/p.json?a=1", BilibiliCaptions.NormalizeUrl("//h/p.json?a=1"));
        Assert.AreEqual("https://h/p.json", BilibiliCaptions.NormalizeUrl("http://h/p.json"));
        Assert.AreEqual("https://h/p.json", BilibiliCaptions.NormalizeUrl(" https://h/p.json "));
    }

    [DataTestMethod]
    [DataRow("en-US", "en-us")]
    [DataRow("zh_Hans!", "zhhans")]
    [DataRow("", "und")]
    [DataRow(null, "und")]
    [DataRow("../..", "und")]
    [DataRow("abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnop")]
    public void SanitizeLanguage(string? lan, string expected)
    {
        Assert.AreEqual(expected, BilibiliCaptions.SanitizeLanguage(lan));
    }
}
