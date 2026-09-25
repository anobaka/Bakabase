using System.Collections.Generic;
using Bakabase.Service.Components;
using Sentry;

namespace Bakabase.Tests;

/// <summary>Signed URLs (Bilibili CDN links, auth_key tokens) must not reach Sentry through breadcrumbs.</summary>
[TestClass]
public sealed class SentryBreadcrumbScrubberTests
{
    private const string SignedCdnUrl =
        "https://upos-sz-example.bilivideo.com:8082/upgcxcode/00/5001-1-30280.m4s?deadline=1900000000&upsig=abc&oi=1&mid=2#frag";

    [TestMethod]
    [DataRow(SignedCdnUrl, "https://upos-sz-example.bilivideo.com:8082/upgcxcode/00/5001-1-30280.m4s")]
    [DataRow("https://api.bilibili.com/x/player/playurl?avid=1&cid=2", "https://api.bilibili.com/x/player/playurl")]
    [DataRow("https://example.com/page#section", "https://example.com/page")]
    [DataRow("https://example.com/plain", "https://example.com/plain")]
    [DataRow("/relative/path?token=1", "/relative/path")]
    [DataRow("", "")]
    public void StripQueryAndFragmentKeepsSchemeHostPortAndPath(string url, string expected) =>
        Assert.AreEqual(expected, SentryBreadcrumbScrubber.StripQueryAndFragment(url));

    [TestMethod]
    public void UrlsInsideTextLoseTheirQuery()
    {
        var text = $"Sending HTTP request GET {SignedCdnUrl} and \"https://i0.hdslb.com/a.json?auth_key=1-0-0\" done";
        Assert.AreEqual(
            "Sending HTTP request GET https://upos-sz-example.bilivideo.com:8082/upgcxcode/00/5001-1-30280.m4s and \"https://i0.hdslb.com/a.json\" done",
            SentryBreadcrumbScrubber.StripQueriesInText(text));
    }

    [TestMethod]
    public void ProtocolRelativeUrlsLoseTheirQueryInMessagesAndData()
    {
        const string subtitle = "//aisubtitle.hdslb.com/bfs/ai_subtitle/prod/1.json?auth_key=1790245698-abc-0-def";
        var breadcrumb = new Breadcrumb($"GET {subtitle} failed", "default",
            new Dictionary<string, string> {["exception_message"] = $"Could not read \"{subtitle}\""}, "log",
            BreadcrumbLevel.Warning);

        var scrubbed = SentryBreadcrumbScrubber.Scrub(breadcrumb);

        Assert.AreEqual("GET //aisubtitle.hdslb.com/bfs/ai_subtitle/prod/1.json failed", scrubbed.Message);
        Assert.AreEqual("Could not read \"//aisubtitle.hdslb.com/bfs/ai_subtitle/prod/1.json\"",
            scrubbed.Data!["exception_message"]);
        Assert.AreEqual("a // b?c", SentryBreadcrumbScrubber.StripQueriesInText("a // b?c"), "not a URL");
    }

    [TestMethod]
    public void HttpBreadcrumbsKeepEverythingButTheQuery()
    {
        var breadcrumb = new Breadcrumb("GET " + SignedCdnUrl, "http",
            new Dictionary<string, string> {["url"] = SignedCdnUrl, ["method"] = "GET", ["status_code"] = "200"},
            "http", BreadcrumbLevel.Info);

        var scrubbed = SentryBreadcrumbScrubber.Scrub(breadcrumb);

        Assert.AreEqual("https://upos-sz-example.bilivideo.com:8082/upgcxcode/00/5001-1-30280.m4s", scrubbed.Data!["url"]);
        Assert.AreEqual("GET", scrubbed.Data["method"]);
        Assert.AreEqual("200", scrubbed.Data["status_code"]);
        Assert.AreEqual("GET https://upos-sz-example.bilivideo.com:8082/upgcxcode/00/5001-1-30280.m4s", scrubbed.Message);
        Assert.AreEqual("http", scrubbed.Type);
        Assert.AreEqual("http", scrubbed.Category);
        Assert.AreEqual(BreadcrumbLevel.Info, scrubbed.Level);
        foreach (var secret in new[] {"upsig", "deadline", "oi=", "mid="})
        {
            Assert.IsFalse(scrubbed.Message!.Contains(secret));
            Assert.IsFalse(scrubbed.Data["url"].Contains(secret));
        }
    }

    [TestMethod]
    public void BreadcrumbsWithoutUrlsAreLeftAsTheyAre()
    {
        var breadcrumb = new Breadcrumb("Skipped Bilibili item 1001", "default",
            new Dictionary<string, string> {["reason"] = "Deleted"}, "log", BreadcrumbLevel.Info);
        Assert.AreSame(breadcrumb, SentryBreadcrumbScrubber.Scrub(breadcrumb));

        var noData = new Breadcrumb("nothing to see", "default");
        Assert.AreSame(noData, SentryBreadcrumbScrubber.Scrub(noData));
    }
}
