using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            new {id = "two", selectedSourceId = "gofile", sources = new[] {gofile}},
            new {id = "three", selectedSourceId = "http", sources = new[] {http}}
        }}, Json);
        var (other, _) = await Seed("Gofile only", ["LOCAL_STORAGE_FULL"]);
        other.PlanJson = JsonSerializer.Serialize(new {parts = new[]
            {new {id = "one", selectedSourceId = "gofile", sources = new[] {http, gofile}}}}, Json);
        await _db.SaveChangesAsync();

        var page = await _search.SearchAsync(new(DownloadSource: "http", PageSize: 1, Page: 2));
        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual(draft.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {multipart.Id, draft.Id, chosen.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(4, page.DownloadSourceCounts["all"]);
        Assert.AreEqual(3, page.DownloadSourceCounts["http"], "Two HTTP parts count as one task.");
        Assert.AreEqual(3, page.DownloadSourceCounts["site:16"], "Unselected sources still show all matching tasks across pages.");
        Assert.AreEqual(4, page.TotalTasks);
        Assert.AreEqual(4, page.StageCounts["attention"], "Stage counters stay global.");
        Assert.AreEqual(1, page.AttentionCounts["localStorage"]);
        Assert.AreEqual(1, page.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(1, page.FactCounts["spaceInsufficient"]);
        var tagged = await _search.SearchAsync(new(DownloadSource: "http", AttentionTag: "localStorage"));
        CollectionAssert.AreEqual(new[] {chosen.Id}, tagged.FilteredTaskIds.ToArray());
        Assert.AreEqual(1, tagged.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(2, tagged.DownloadSourceCounts["all"]);
        Assert.AreEqual(1, tagged.DownloadSourceCounts["http"]);
        Assert.AreEqual(1, tagged.DownloadSourceCounts["site:16"]);
        var byFact = await _search.SearchAsync(new(Keyword: "Gofile only", Stage: "attention", DownloadSource: "http", Facts: ["spaceInsufficient"]));
        Assert.AreEqual(0, byFact.TotalCount);
        Assert.AreEqual(1, byFact.DownloadSourceCounts["all"]);
        Assert.IsFalse(byFact.DownloadSourceCounts.ContainsKey("http"));
        Assert.AreEqual(1, byFact.DownloadSourceCounts["site:16"]);
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
        var all = await _search.SearchAsync(new(PageSize: 1));
        Assert.AreEqual(9, all.TotalCount, "Unknown source evidence remains visible without a source filter.");
        Assert.AreEqual(9, all.DownloadSourceCounts["all"], "All sources includes tasks with no recognized route.");
        Assert.AreEqual(1, all.DownloadSourceCounts["site:19"]);
        Assert.IsFalse(all.DownloadSourceCounts.ContainsKey("site:16"));
        await _db.Entry(run).ReloadAsync();
        Assert.IsTrue(run.CurrentItemJson!.Contains("pixeldrain"), "Filtering never writes execution state.");
    }

    [TestMethod]
    public async Task SourceCountsCanSwitchSourcesWithinSelectedCapabilityStateAndReason()
    {
        var (first, firstRun) = await Seed("HTTP", ["LOCAL_STORAGE_FULL"]);
        first.PlanJson = """{"parts":[{"selectedSourceId":"a","sources":[{"id":"a","url":"https://files.example.test/a","kind":"http"}]}]}""";
        var (second, secondRun) = await Seed("GoFile", ["LOCAL_STORAGE_FULL"]);
        second.PlanJson = """{"parts":[{"selectedSourceId":"b","sources":[{"id":"b","url":"https://gofile.io/d/abcdef"}]}]}""";
        var (wrongState, thirdRun) = await Seed("Waiting on a different capability state", ["LOCAL_STORAGE_FULL"]);
        wrongState.PlanJson = second.PlanJson;
        var (wrongCapability, fourthRun) = await Seed("Waiting on a different capability", ["LOCAL_STORAGE_FULL"]);
        wrongCapability.PlanJson = first.PlanJson;
        var (wrongReason, fifthRun) = await Seed("Different issue", ["SOURCE_SHARE_EXPIRED"]);
        wrongReason.PlanJson = second.PlanJson;
        foreach (var node in _db.WorkflowNodeExecutions.Local) node.Kind = "test.files";
        await _db.SaveChangesAsync();
        var runtime = new SearchRuntime(new Dictionary<int, WorkflowCapabilityRuntime> {
            [firstRun.Id] = new("files", "Files", Status: "waiting"), [secondRun.Id] = new("files", "Files", Status: "waiting"),
            [thirdRun.Id] = new("files", "Files", Status: "queued"), [fourthRun.Id] = new("analysis", "Analysis", Status: "waiting"),
            [fifthRun.Id] = new("files", "Files", Status: "waiting")
        });
        var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>(),
            runtimeProviders: new WorkflowActivityRuntimeRegistry([runtime]));
        var page = await search.SearchAsync(new(DownloadSource: "http", CapabilityId: "files",
            ExecutionState: "waiting", AttentionTags: ["localStorage"], Facts: ["spaceInsufficient"]));
        CollectionAssert.AreEqual(new[] {first.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(2, page.DownloadSourceCounts["all"]);
        Assert.AreEqual(1, page.DownloadSourceCounts["http"]);
        Assert.AreEqual(1, page.DownloadSourceCounts["site:16"]);
        Assert.AreEqual(1, page.RuntimeCapabilityCounts["analysis"]);
        Assert.IsFalse(page.RuntimeExecutionStateCounts.ContainsKey("queued"), "The runtime filter still respects the selected HTTP source.");
    }

    [TestMethod]
    public async Task CountsIncludeEverySourceBatchAndExcludeCancelledOrDeletedTasks()
    {
        var seeded = Enumerable.Range(1, 105).Select(i => new AcquisitionTaskDbModel
        {
            Title = "Batch " + i, LeadKind = AcquisitionLeadKind.DirectUrl, LeadValue = "https://files.example.test/archive",
            Status = i == 105 ? AcquisitionStatus.Cancelled : AcquisitionStatus.Pending,
            DeletedAt = i == 104 ? DateTime.UtcNow : null
        }).ToArray();
        _db.AcquisitionTasks.AddRange(seeded);
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(Keyword: "Batch", DownloadSource: "bt", PageSize: 1));
        Assert.AreEqual(0, page.TotalCount);
        Assert.AreEqual(103, page.DownloadSourceCounts["all"]);
        Assert.AreEqual(103, page.DownloadSourceCounts["http"]);
        Assert.IsFalse(page.DownloadSourceCounts.ContainsKey("bt"));
        var cancelled = await _search.SearchAsync(new(Keyword: "Batch", Stage: "cancelled"));
        Assert.AreEqual(1, cancelled.DownloadSourceCounts["all"]);
        Assert.AreEqual(1, cancelled.DownloadSourceCounts["http"]);
    }

    private sealed class SearchRuntime(Dictionary<int, WorkflowCapabilityRuntime> states) : IWorkflowActivityRuntimeProvider
    {
        public bool CanHandle(string activityKind) => true;
        public ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context, CancellationToken ct) =>
            ValueTask.FromResult(new WorkflowActivityRuntimeSnapshot {Capability = states[context.WorkflowRunId]});
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
