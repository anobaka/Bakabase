using System;
using System.IO;
using System.Linq;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

namespace Bakabase.Tests;

[TestClass]
public class SoulPlusPostParsingTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PostParser", "soulplus-mixed-locks.html"));

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
    [DataRow("https://www.north-plus.net/read.php?tid=42&page=5#read_100", "https://www.north-plus.net/read.php?tid=42")]
    [DataRow("https://www.north-plus.net/read.php?tid-42-page-3.html", "https://www.north-plus.net/read.php?tid-42-page-1.html")]
    public void ReaderAlwaysStartsAtFirstPage(string input, string expected) => Assert.AreEqual(expected, SoulPlusPostParser.FirstPageUrl(input));
}
