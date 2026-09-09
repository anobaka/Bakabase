using Bakabase.Modules.Acquisition.Components;

namespace Bakabase.Modules.Acquisition.Tests;

/// <summary>
/// Reading somebody's list of things to get.
/// <para>
/// These lists are never in the same shape twice — a message with twenty lines, a spreadsheet a
/// group keeps, a paste out of a chat. Reading one wrongly is worse than not reading it, so the
/// rules here are deliberately conservative: a link is what looks like a link, a password is what
/// sits behind the word for it, and the rest is the title.
/// </para>
/// </summary>
[TestClass]
public sealed class SharedListReadingTests
{
    [TestMethod]
    public void ALineThatIsJustATitleAndALinkReadsAsBoth()
    {
        var rows = SharedListReader.ReadText("Some Doujin Game https://pan.example/s/1AbC");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("Some Doujin Game", rows[0].Title);
        Assert.AreEqual("https://pan.example/s/1AbC", rows[0].Url);
    }

    /// <summary>
    /// The commonest shape there is: a link with the code right after it, in either language.
    /// </summary>
    [TestMethod]
    public void ACodeAfterTheLinkIsRead()
    {
        foreach (var line in new[]
                 {
                     "Volume 1 https://pan.example/s/1AbC 提取码: 8k2p",
                     "Volume 1 https://pan.example/s/1AbC password=8k2p",
                     "Volume 1 https://pan.example/s/1AbC 密码 8k2p",
                 })
        {
            var row = SharedListReader.ReadText(line).Single();

            Assert.AreEqual("8k2p", row.Password, line);
            Assert.AreEqual("https://pan.example/s/1AbC", row.Url, line);
            Assert.AreEqual("Volume 1", row.Title, line);
        }
    }

    /// <summary>
    /// A bare word is not a password. A title is a bare word too, and guessing wrong would write
    /// nonsense into every row of the file.
    /// </summary>
    [TestMethod]
    public void ABareWordIsNotTakenForAPassword()
    {
        var row = SharedListReader.ReadDelimited("Some Game,https://pan.example/s/1AbC,hunter2")
            .Single();

        Assert.IsNull(row.Password);
        Assert.AreEqual("Some Game hunter2", row.Title,
            "it stays part of what the row says, rather than being invented into a field");
    }

    [TestMethod]
    public void SeparatedRowsAreReadWhicheverColumnIsWhich()
    {
        var rows = SharedListReader.ReadDelimited(
            """
            title,link,password
            Volume 1,https://pan.example/s/1,提取码 aaaa
            https://pan.example/s/2,Volume 2,提取码 bbbb
            """);

        Assert.AreEqual(2, rows.Count, "the header is not a row");
        Assert.AreEqual("Volume 1", rows[0].Title);
        Assert.AreEqual("https://pan.example/s/2", rows[1].Url,
            "which column holds what is not something these files agree on");
        Assert.AreEqual("bbbb", rows[1].Password);
    }

    /// <summary>A title with a comma in it is the commonest thing in one of these files.</summary>
    [TestMethod]
    public void AQuotedTitleKeepsItsCommas()
    {
        var row = SharedListReader
            .ReadDelimited("\"Volume 1, Special Edition\",https://pan.example/s/1")
            .Single();

        Assert.AreEqual("Volume 1, Special Edition", row.Title);
    }

    [TestMethod]
    public void ARowWithNeitherANameNorALinkIsNotARow()
    {
        var rows = SharedListReader.ReadText(
            """
            ==========

            Volume 1 https://pan.example/s/1
            """);

        Assert.AreEqual(2, rows.Count,
            "the separator line has no link, but it does have text, so it reads as a title");
        Assert.IsTrue(rows.Any(r => r.Url == "https://pan.example/s/1"));
    }

    [TestMethod]
    public void ALineNumberSaysWhereEachRowCameFrom()
    {
        var rows = SharedListReader.ReadText(
            """
            Volume 1 https://pan.example/s/1

            Volume 3 https://pan.example/s/3
            """);

        Assert.AreEqual(1, rows[0].LineNumber);
        Assert.AreEqual(3, rows[1].LineNumber, "a blank line still counts, so a report can point at it");
    }

    /// <summary>A link's own punctuation must not swallow the bracket somebody put around it.</summary>
    [TestMethod]
    public void ALinkInBracketsIsJustTheLink()
    {
        var row = SharedListReader.ReadText("Volume 1 (https://pan.example/s/1AbC)").Single();

        Assert.AreEqual("https://pan.example/s/1AbC", row.Url);
    }

    [TestMethod]
    public void ATitleWithNoLinkIsStillSomethingYouAreMissing()
    {
        var row = SharedListReader.ReadText("Some Game I Want").Single();

        Assert.AreEqual("Some Game I Want", row.Title);
        Assert.IsNull(row.Url);
    }
}
