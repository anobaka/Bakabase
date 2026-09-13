using Bakabase.Modules.Acquisition.Components;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using System.Text;
using NPOI.XSSF.UserModel;

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
            Volume 1,https://pan.example/s/1,提取码 aaaa
            https://pan.example/s/2,Volume 2,提取码 bbbb
            """);

        Assert.AreEqual(2, rows.Count);
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

    [DataTestMethod]
    [DataRow("cn", "示例：星海旅行记（请替换本行）")]
    [DataRow("en", "Example: Star Voyage (replace this row)")]
    public async Task TheActualDownloadableTemplatePreviewsItsColumnsWithoutImportingTheHeader(
        string language, string expectedTitle)
    {
        // Embed the browser's actual download assets, so changing their headers or encoding cannot
        // silently disconnect the template from the production preview parser.
        await using var template = typeof(SharedListReadingTests).Assembly
            .GetManifestResourceStream($"SharedListTemplates.resource-list.{language}.csv")!;
        Assert.IsNotNull(template);
        var bytes = new byte[3];
        await template.ReadExactlyAsync(bytes);
        CollectionAssert.AreEqual(new byte[] {0xEF, 0xBB, 0xBF}, bytes,
            "Excel should recognise UTF-8 when opening the template directly");
        template.Position = 0;

        var reader = new SharedListImportService(null!, null!, null!, null!, null!, null!);
        var rows = await reader.ReadAsync(template, "resource-list-template.csv");

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(expectedTitle, rows[0].Title);
        Assert.AreEqual("https://example.com/share/replace-me", rows[0].Url);
        Assert.AreEqual("example-password", rows[0].Password);
        Assert.AreEqual(2, rows[0].LineNumber);
        Assert.IsNotNull(rows[1].Title);
        Assert.IsNull(rows[1].Url);
        Assert.IsNull(rows[1].Password);
    }

    [TestMethod]
    public void HeaderColumnsKeepQuotedCommasQuotesNewlinesAndPasswordsInTheirOwnFields()
    {
        var csv = "\uFEFFTitle,Download URL,Archive password\r\n" +
                  "\"Volume 1, \"\"Special\"\" Edition\r\nPart 2\",\"https://example.com/file?a=1,2&b=3\",\"a, \"\"b\"\" c\"\r\n" +
                  "Name only,,\r\n";
        var rows = SharedListReader.ReadDelimited(csv);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("Volume 1, \"Special\" Edition\r\nPart 2", rows[0].Title);
        Assert.AreEqual("https://example.com/file?a=1,2&b=3", rows[0].Url);
        Assert.AreEqual("a, \"b\" c", rows[0].Password);
        Assert.AreEqual(2, rows[0].LineNumber);
        Assert.AreEqual(4, rows[1].LineNumber);
        Assert.AreEqual("Name only", rows[1].Title);
        Assert.IsNull(rows[1].Url);
    }

    [TestMethod]
    public void AHeaderCanReorderColumnsAndCsvTabsInsideQuotesAreNotDelimiters()
    {
        var row = SharedListReader.ReadDelimited(
            "Archive password,Download URL,Title\r\n\"p, a\",https://example.com/a,\"A\tB\"\r\n").Single();

        Assert.AreEqual("A\tB", row.Title);
        Assert.AreEqual("p, a", row.Password);
        Assert.AreEqual("https://example.com/a", row.Url);
    }

    [TestMethod]
    public void TsvAndHeaderlessMultilineCsvRemainSupported()
    {
        var tsv = SharedListReader.ReadDelimited("\n\n名称\t下载链接\t解压密码\n作品\thttps://example.com/a\tpassword123").Single();
        Assert.AreEqual("作品", tsv.Title);
        Assert.AreEqual("password123", tsv.Password);
        Assert.AreEqual(4, tsv.LineNumber);

        var csv = SharedListReader.ReadDelimited("\"A, B\nC\",https://example.com/a,password: 1234").Single();
        Assert.AreEqual("A, B\nC", csv.Title);
        Assert.AreEqual("1234", csv.Password);
    }

    [TestMethod]
    public async Task ASheetSavedFromTheTemplateUsesTheSameHeaderMapping()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Resources");
        var values = new[]
        {
            new[] {"名称", "下载链接", "解压密码"},
            new[] {"作品, 特别版", "https://example.com/a?files=1,2", "bare password, with spaces"},
            new[] {"只有名称", "", ""}
        };
        for (var i = 0; i < values.Length; i++)
        {
            var row = sheet.CreateRow(i);
            for (var j = 0; j < values[i].Length; j++) row.CreateCell(j).SetCellValue(values[i][j]);
        }
        await using var content = new MemoryStream();
        workbook.Write(content, leaveOpen: true);
        content.Position = 0;

        var reader = new SharedListImportService(null!, null!, null!, null!, null!, null!);
        var rows = await reader.ReadAsync(content, "resources.xlsx");

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(values[1][0], rows[0].Title);
        Assert.AreEqual(values[1][1], rows[0].Url);
        Assert.AreEqual(values[1][2], rows[0].Password);
        Assert.AreEqual("只有名称", rows[1].Title);
        Assert.IsNull(rows[1].Url);
    }

    [TestMethod]
    public void ExplicitHeadersPreserveTheSuppliedFieldsInsteadOfGuessingTheirMeaning()
    {
        var row = SharedListReader.ReadDelimited(
            "Title,Download URL,Archive password\nhttps://example.com/title,magnet:?xt=urn:btih:example,password123").Single();

        Assert.AreEqual("https://example.com/title", row.Title);
        Assert.AreEqual("magnet:?xt=urn:btih:example", row.Url);
        Assert.AreEqual("password123", row.Password);
    }

    [TestMethod]
    public async Task AnUnclosedCsvQuoteReturnsAPreviewErrorAndACorrectedFileCanBeRead()
    {
        var controller = new AcquisitionController(null!, null!, null!, null!, null!, null!);
        var importer = new SharedListImportService(null!, null!, null!, null!, null!, null!);
        await using var broken = new MemoryStream(Encoding.UTF8.GetBytes("Title,Download URL,Archive password\n\"Unclosed title,,\nAnother row,,"));
        var failed = await controller.PreviewSharedList(
            new FormFile(broken, 0, broken.Length, "file", "resources.csv"), importer);

        Assert.AreNotEqual(0, failed.Code);
        StringAssert.Contains(failed.Message, "line 2");
        StringAssert.Contains(failed.Message, "unclosed quoted field");
        Assert.IsTrue(failed.Data == null || failed.Data.Count == 0);

        await using var corrected = new MemoryStream(Encoding.UTF8.GetBytes("Title,Download URL,Archive password\nCorrected title,,"));
        var preview = await controller.PreviewSharedList(
            new FormFile(corrected, 0, corrected.Length, "file", "resources.csv"), importer);

        Assert.AreEqual(0, preview.Code);
        Assert.AreEqual("Corrected title", preview.Data.Single().Title);
    }

}
