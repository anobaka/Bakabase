using System;
using System.IO;
using System.Linq;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

namespace Bakabase.Tests;

[TestClass]
public class SoulPlusPostParsingTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PostParser", "soulplus-mixed-locks.html"));
    private static string NoticeFixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PostParser", "soulplus-invalid-post.html"));

    [TestMethod]
    public void MixedBoughtAndUnboughtBlocksAreAllPreservedWithCommentMetadata()
    {
        var post = SoulPlusPostParser.Parse(Fixture, "https://www.north-plus.net/read.php?tid=42");
        Assert.AreEqual(120m, post.Balance);
        Assert.AreEqual(1, post.LockedContents!.Count(l => l.IsBought));
        var locked = post.LockedContents!.Where(l => !l.IsBought).ToList();
        Assert.AreEqual(2, locked.Count);
        CollectionAssert.AreEqual(new decimal?[] {5m, 20m}, locked.Select(l => l.Price).ToArray());
        Assert.AreNotEqual(locked[0].Url, locked[1].Url);
        Assert.AreEqual("2", locked[0].Floor);
        Assert.AreEqual(2, post.Comments.Count);
        Assert.AreEqual("1", post.Comments[0].Floor);
        Assert.AreEqual("Reporter", post.Comments[0].Author);
        Assert.AreEqual(TimeSpan.FromHours(8), post.Comments[0].PostedAt!.Value.Offset);
    }

    [TestMethod]
    public void MissingAccountPanelDoesNotReadAnAuthorsCoinsAsOurBalance()
    {
        var html = Fixture.Replace("id=\"user-login\"", "id=\"unrecognized-header\"");
        Assert.IsNull(SoulPlusPostParser.Parse(html, "https://www.north-plus.net/read.php?tid=42").Balance);
    }

    [TestMethod]
    public void UnknownPriceAndForeignPurchaseUrlRemainLockedAndUnpurchasable()
    {
        var html = Fixture.Replace("售价 5 SP币", "价格未知").Replace("job.php?action=buytopic&amp;tid=42&amp;pid=102", "https://evil.test/job.php?action=buytopic");
        var first = SoulPlusPostParser.Parse(html, "https://www.north-plus.net/read.php?tid=42").LockedContents!.First(l => !l.IsBought);
        Assert.IsNull(first.Price);
        Assert.IsNull(first.Url);
    }

    [TestMethod]
    public void FullDocumentWithTypedPurchaseButtonsKeepsEveryLockAndComment()
    {
        var html = Fixture.Replace("<input value=", "<input type=\"button\" value=");
        var post = SoulPlusPostParser.Parse(html, "https://www.north-plus.net/read.php?tid=42");

        Assert.AreEqual(2, post.Comments.Count);
        Assert.AreEqual(2, post.LockedContents!.Count(l => !l.IsBought));
        Assert.AreEqual(1, post.LockedContents.Count(l => l.IsBought));
        var comment = new CsQuery.CQ(post.Comments[1].Html);
        Assert.AreEqual(2, comment["input[type=button]"].Length);
        Assert.IsTrue(comment["input"].All(input => input.GetAttribute("value") == "愿意购买,我买,我付钱"));
    }

    [TestMethod]
    [DataRow("此帖被管理员关闭，暂时不能浏览")]
    [DataRow("用户被禁言,该主题自动屏蔽")]
    public void UnavailableReplyDoesNotHideTheReadableOriginalPost(string message)
    {
        var html = Fixture.Replace("链接失效了。", $"<span class=\"s3\">{message}</span>");
        var post = SoulPlusPostParser.Parse(html, "https://www.north-plus.net/read.php?tid=42");
        Assert.AreEqual("分享及补档", post.Title);
        Assert.AreEqual(2, post.Comments.Count);
        StringAssert.Contains(new CsQuery.CQ(post.Comments[0].Html).Text(), message);
        Assert.AreEqual(2, post.LockedContents!.Count(l => !l.IsBought));
    }

    [TestMethod]
    [DataRow("script")]
    [DataRow("style")]
    [DataRow("template")]
    [DataRow("blockquote")]
    public void NonContentMessagesDoNotTurnReadableOriginalPostIntoDeleted(string tag)
    {
        var html = Fixture.Replace("<div id=\"read_tpc\" class=\"f14\">",
            $"<div id=\"read_tpc\" class=\"f14\"><{tag}>用户被禁言,该主题自动屏蔽</{tag}>");
        Assert.AreEqual("分享及补档", SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42").Title);
    }

    [TestMethod]
    [DataRow("此帖被管理员关闭，暂时不能浏览")]
    [DataRow("用户被禁言,该主题自动屏蔽")]
    public void VisibleBlockingNoticeInOriginalPostStillReportsUnavailable(string message)
    {
        var html = $"<html><body><h1 id=\"subject_tpc\">Blocked thread</h1>" +
                   $"<div id=\"read_tpc\"><span class=\"s3\"> {message}。 </span></div>" +
                   "<div class=\"tpc_content\"><div class=\"f14\">A readable reply is not the original post.</div></div></body></html>";
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("Post is deleted or unavailable.", error.Message);
    }

    [TestMethod]
    public void NoticeOnlyErrorPageIsUnavailableButLoginOrScriptOnlyPageIsUnclassified()
    {
        var deleted = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(
            "<html><body><p>此帖被管理员关闭，暂时不能浏览</p></body></html>", "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("Post is deleted or unavailable.", deleted.Message);
        var unreadable = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(
            "<html><body><script>var notice='用户被禁言,该主题自动屏蔽';</script><p>Please log in.</p></body></html>",
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreNotEqual(deleted.Message, unreadable.Message);
    }

    [TestMethod]
    public void OriginalPostCanDiscussAQuotedOrHiddenBlockingNotice()
    {
        const string html = "<html><body><h1 id=\"subject_tpc\">Discussion</h1><div id=\"read_tpc\">" +
            "<blockquote>用户被禁言,该主题自动屏蔽</blockquote><span style=\"display:none\">此帖被管理员关闭，暂时不能浏览</span>" +
            "<p>这是错误提示的引用，下面是可读的正文。</p></div></body></html>";
        Assert.AreEqual("Discussion", SoulPlusPostParser.Parse(html, "https://www.north-plus.net/read.php?tid=42").Title);
    }

    [TestMethod]
    [DataRow("帖子ID非法")]
    [DataRow("您没有权限浏览此主题")]
    public void DedicatedSiteNoticeIsPreservedInsteadOfGuessingAnAccountProblem(string notice)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(
            NoticeFixture.Replace("帖子ID非法", notice), "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual($"SoulPlus：{notice}", error.Message);
    }

    [TestMethod]
    public void UnbrandedSiteNoticeHeadingIsAlsoRecognized()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(
            NoticeFixture.Replace("南+ South Plus 提示信息", "提示信息"),
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("SoulPlus：帖子ID非法", error.Message);
    }

    [TestMethod]
    public void SiteNoticeUsesVisiblePlainTextWithoutScriptsQuotesOrNavigation()
    {
        var html = NoticeFixture.Replace("<center>帖子ID非法</center>",
            "<center>  帖子<span hidden>secret</span><span>ID非法</span>  " +
            "<script>secret</script><blockquote>secret</blockquote><span style=\"display: none\">secret</span></center>");
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("SoulPlus：帖子ID非法", error.Message);
    }

    [TestMethod]
    [DataRow("title-only")]
    [DataRow("wrong-heading")]
    [DataRow("hidden-heading")]
    [DataRow("hidden-center")]
    [DataRow("hidden-parent")]
    [DataRow("quoted-only")]
    public void UnrecognizedOrInvisibleNoticeDoesNotBecomeAnAuthoritativeSiteError(string variant)
    {
        var html = variant switch
        {
            "title-only" => "<html><head><title>帖子ID非法</title></head><body><center>帖子ID非法</center></body></html>",
            "wrong-heading" => NoticeFixture.Replace("提示信息", "普通表格"),
            "hidden-heading" => NoticeFixture.Replace("class=\"h\"", "class=\"h\" hidden"),
            "hidden-center" => NoticeFixture.Replace("<center>", "<center style=\"visibility: hidden\">"),
            "hidden-parent" => NoticeFixture.Replace("class=\"f_one\"", "class=\"f_one\" aria-hidden=\"true\""),
            _ => NoticeFixture.Replace("<center>帖子ID非法</center>", "<center><blockquote>帖子ID非法</blockquote></center>")
        };
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("The post page could not be read. Check the account login and post URL.", error.Message);
    }

    [TestMethod]
    public void ReadablePostTakesPriorityOverCopiedNoticeMarkup()
    {
        var html = Fixture.Replace("<body>", "<body>" + NoticeFixture);
        Assert.AreEqual("分享及补档", SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42").Title);
    }

    [TestMethod]
    public void MissingTitleWithAReadableMainDoesNotPromoteCopiedNoticeMarkup()
    {
        var html = Fixture.Replace("id=\"subject_tpc\"", "id=\"unrecognized-title\"")
            .Replace("<body>", "<body>" + NoticeFixture);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(html,
            "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("The post page could not be read. Check the account login and post URL.", error.Message);
    }

    [TestMethod]
    public void LongSiteNoticeIsBoundedWithoutSplittingUnicodeCharacters()
    {
        var notice = new string('字', 511) + "😀" + new string('字', 100);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => SoulPlusPostParser.Parse(
            NoticeFixture.Replace("帖子ID非法", notice), "https://www.north-plus.net/read.php?tid=42"));
        Assert.AreEqual("SoulPlus：" + new string('字', 511) + "…", error.Message);
    }

    [TestMethod]
    [DataRow("https://www.north-plus.net/read.php?tid=42&page=5#read_100", "https://www.north-plus.net/read.php?tid=42")]
    [DataRow("https://www.north-plus.net/read.php?tid-42-page-3.html", "https://www.north-plus.net/read.php?tid-42-page-1.html")]
    public void ReaderAlwaysStartsAtFirstPage(string input, string expected) => Assert.AreEqual(expected, SoulPlusPostParser.FirstPageUrl(input));
}
