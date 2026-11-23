using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionAttentionSearchTests
{
    [TestMethod]
    public async Task DownloadSourceFiltersChosenPartsAndDraftAlternativesBeforePagingAndFacets()
    {
        var http = new {id = "http", url = "https://files.example.test/archive", kind = "http"};
        var gofile = new {id = "gofile", url = "https://gofile.io/d/abcdef", kind = "auto"};
        var (chosen, _) = await Seed("Selected HTTP", ["LOCAL_STORAGE_FULL"]);
        chosen.PlanJson = JsonSerializer.Serialize(new {parts = new[]
            {new {id = "one", selectedSourceId = "http", sources = new[] {http, gofile}}}}, Json);
        var (draft, _) = await Seed("Draft alternatives", ["SOURCE_SHARE_EXPIRED"]);
        draft.PlanJson = null;
        draft.BuilderStateJson = JsonSerializer.Serialize(new {draftSources = new[] {http, gofile}}, Json);
        var (multipart, _) = await Seed("Both required parts", ["DEPENDENCY_REQUIRED"]);
        multipart.PlanJson = JsonSerializer.Serialize(new {parts = new[]
        {
            new {id = "one", selectedSourceId = "http", sources = new[] {http}},
            new {id = "two", selectedSourceId = "gofile", sources = new[] {gofile}}
        }}, Json);
        var (other, _) = await Seed("Gofile only", ["LOCAL_STORAGE_FULL"]);
        other.PlanJson = JsonSerializer.Serialize(new {parts = new[]
            {new {id = "one", selectedSourceId = "gofile", sources = new[] {http, gofile}}}}, Json);
        await _db.SaveChangesAsync();

        var page = await _search.SearchAsync(new(DownloadSource: "http", PageSize: 1, Page: 2));
        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual(draft.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {multipart.Id, draft.Id, chosen.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(4, page.TotalTasks);
        Assert.AreEqual(4, page.StageCounts["attention"], "Stage counters stay global.");
        Assert.AreEqual(1, page.AttentionCounts["localStorage"]);
        Assert.AreEqual(1, page.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(1, page.FactCounts["spaceInsufficient"]);
        var tagged = await _search.SearchAsync(new(DownloadSource: "http", AttentionTag: "localStorage"));
        CollectionAssert.AreEqual(new[] {chosen.Id}, tagged.FilteredTaskIds.ToArray());
        Assert.AreEqual(1, tagged.AttentionCounts["sourceUnavailable"]);
        var site = await _search.SearchAsync(new(DownloadSource: "site:16"));
        CollectionAssert.AreEquivalent(new[] {draft.Id, multipart.Id, other.Id}, site.FilteredTaskIds.ToArray());
        Assert.IsFalse(site.FilteredTaskIds.Contains(chosen.Id), "Unselected mirrors do not change a prepared task's source.");
        Assert.IsTrue((await _db.AcquisitionTasks.ToListAsync()).All(t => t.PlanHistoryJson == "unchanged history"));
    }

    [TestMethod]
    public async Task DownloadSourceUsesSavedExecutionAndLegacyRoutesWithoutTreatingPostOriginsAsDownloads()
    {
        var (changed, run) = await Seed("Actual runtime source", []);
        changed.PlanJson = "{\"parts\":[{\"id\":\"p\",\"selectedSourceId\":\"a\",\"sources\":[{\"id\":\"a\",\"url\":\"https://gofile.io/d/abcdef\"}]}]}";
        run.CurrentItemJson = "{\"item\":{\"targets\":[{\"selectedSourceId\":\"b\",\"sources\":[{\"id\":\"b\",\"url\":\"https://pixeldrain.com/u/abcdefgh\"}]}]}}";
        var (legacyLink, legacyRun) = await Seed("Legacy chosen link", []);
        legacyLink.PlanJson = null;
        legacyRun.CurrentItemJson = "{\"item\":{\"selectedLinkIndex\":1,\"links\":[{\"url\":\"https://gofile.io/d/abcdef\",\"driveKind\":12},{\"url\":\"https://files.example.test/get\",\"driveKind\":1}]}}";
        var direct = new AcquisitionTaskDbModel {Title = "Direct", LeadKind = AcquisitionLeadKind.DirectUrl, LeadValue = "https://files.example.test/get"};
        var magnet = new AcquisitionTaskDbModel {Title = "Magnet", LeadKind = AcquisitionLeadKind.Magnet, LeadValue = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789"};
        var torrent = new AcquisitionTaskDbModel {Title = "Uploaded torrent", LeadKind = AcquisitionLeadKind.Torrent, LeadValue = "managed-torrent-reference"};
        var holding = new AcquisitionTaskDbModel {Title = "Holding", LeadKind = AcquisitionLeadKind.PlatformHolding, LeadValue = "ExHentai:123/token"};
        var search = new AcquisitionTaskDbModel {Title = "Explicit search", BuilderId = "exhentai.search", Phase = "preparing"};
        var origin = new AcquisitionTaskDbModel {Title = "Post origin", LeadKind = AcquisitionLeadKind.SharedPage, LeadValue = "https://files.example.test/post.zip"};
        var malformed = new AcquisitionTaskDbModel {Title = "Malformed fixed plan", BuilderId = "prepared", PlanJson = "not JSON",
            BuilderStateJson = "{\"draftSources\":[{\"url\":\"https://gofile.io/d/abcdef\"}]}"};
        _db.AcquisitionTasks.AddRange(direct, magnet, torrent, holding, search, origin, malformed);
        await _db.SaveChangesAsync();

        CollectionAssert.AreEquivalent(new[] {legacyLink.Id, direct.Id}, (await _search.SearchAsync(new(DownloadSource: "http"))).FilteredTaskIds.ToArray());
        CollectionAssert.AreEquivalent(new[] {magnet.Id, torrent.Id}, (await _search.SearchAsync(new(DownloadSource: "bt"))).FilteredTaskIds.ToArray());
        CollectionAssert.AreEquivalent(new[] {holding.Id, search.Id}, (await _search.SearchAsync(new(DownloadSource: "site:2"))).FilteredTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {changed.Id}, (await _search.SearchAsync(new(DownloadSource: "site:19"))).FilteredTaskIds.ToArray());
        Assert.AreEqual(0, (await _search.SearchAsync(new(DownloadSource: "site:16"))).TotalCount);
        Assert.AreEqual(9, (await _search.SearchAsync(new())).TotalCount, "Unknown source evidence remains visible without a source filter.");
        await _db.Entry(run).ReloadAsync();
        Assert.IsTrue(run.CurrentItemJson!.Contains("pixeldrain"), "Filtering never writes execution state.");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("general")]
    [DataRow("site")]
    [DataRow("site:016")]
    [DataRow("site:999")]
    [DataRow("gofile")]
    public async Task DownloadSourceRejectsUnknownFilterKeys(string source)
    {
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => _search.SearchAsync(new(DownloadSource: source)));
        StringAssert.Contains(error.Message, "download source");
    }
}
