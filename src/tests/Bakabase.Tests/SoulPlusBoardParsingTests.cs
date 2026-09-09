using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Service.Components.Subscription.Providers.SoulPlus;

namespace Bakabase.Tests;

/// <summary>
/// Reading a board's thread list.
/// <para>
/// A third-party page's markup changes without warning, so the parser reads structure — a link
/// to a thread — rather than the class names of whatever skin the forum is wearing. These
/// fixtures are reduced to exactly that structure, which is also the claim being tested: nothing
/// cosmetic is required for it to work.
/// </para>
/// </summary>
[TestClass]
public sealed class SoulPlusBoardParsingTests
{
    private const string PageUrl = "https://soulplus.example/thread.php?fid=42&page=1";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Subscription", name));

    [TestMethod]
    public void EveryThreadOnThePageIsFound()
    {
        var threads = SoulPlusListParser.Parse(Fixture("soulplus-board-page1.html"), PageUrl);

        CollectionAssert.AreEqual(new[] {"100001", "100002", "100003"},
            threads.Select(t => t.Tid).ToArray(),
            "the fourth link is a thumbnail with no text, which is not a title");

        Assert.AreEqual("[汉化] Some Doujin Game v1.2", threads[0].Title);
        Assert.AreEqual("https://soulplus.example/read.php?tid=100001&fpage=1", threads[0].Url,
            "the thread's link is absolute, because it is what becomes the acquisition lead");
        Assert.AreEqual("https://soulplus.example/read.php?tid=100002", threads[1].Url,
            "a root-relative href resolves against the page, not against the board path");
    }

    /// <summary>
    /// A thread's own "last reply" link points at the same thread. Counting it twice would make
    /// every check report the same posts as new.
    /// </summary>
    [TestMethod]
    public void AThreadLinkedTwiceIsOneThread()
    {
        var threads = SoulPlusListParser.Parse(Fixture("soulplus-board-page1.html"), PageUrl);

        Assert.AreEqual(1, threads.Count(t => t.Tid == "100001"));
    }

    [TestMethod]
    public void ThePagerSaysWhereTheNextPageIs()
    {
        Assert.AreEqual("https://soulplus.example/thread.php?fid=42&page=2",
            SoulPlusListParser.FindNextPageUrl(Fixture("soulplus-board-page1.html"), PageUrl));
    }

    /// <summary>
    /// The last page still renders a "next". It points at itself, which is the only way these
    /// forums say there is nothing after this — following it would page forever.
    /// </summary>
    [TestMethod]
    public void TheLastPageHasNoNextPage()
    {
        const string lastPageUrl = "https://soulplus.example/thread.php?fid=42&page=2";

        Assert.IsNull(SoulPlusListParser.FindNextPageUrl(Fixture("soulplus-board-lastpage.html"),
            lastPageUrl));
    }

    /// <summary>
    /// A sharing channel names posts, not works. Saying otherwise would make the next post about
    /// the same game a second copy of it.
    /// </summary>
    [TestMethod]
    public void ABoardIsASharingChannelAndClaimsNoIdentity()
    {
        var provider = new SoulPlusSearchProvider(null!);

        Assert.AreEqual(SubscriptionSourceKind.SharingChannel, provider.SourceKind);
        Assert.IsNull(provider.ResourceSource);
    }

    [TestMethod]
    public async Task ABoardUrlIsRequiredAndMustBeAUrl()
    {
        var provider = new SoulPlusSearchProvider(null!);

        Assert.IsFalse((await provider.ValidateTargetAsync("{}", CancellationToken.None)).IsValid);
        Assert.IsFalse((await provider.ValidateTargetAsync("""{"url":"not a url"}""",
            CancellationToken.None)).IsValid);
        Assert.IsTrue((await provider.ValidateTargetAsync(
            $$"""{"url":"{{PageUrl}}"}""", CancellationToken.None)).IsValid);
    }

    [TestMethod]
    public void TheSummarySaysWhatIsBeingWatched()
    {
        var provider = new SoulPlusSearchProvider(null!);
        var target = JsonSerializer.Serialize(new SoulPlusSearchTarget
        {
            Url = PageUrl,
            Keywords = ["汉化", "生肉"],
        }, new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase});

        StringAssert.Contains(provider.DescribeTarget(target), "汉化, 生肉");
    }
}
