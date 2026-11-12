using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionLifecycleTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionPlanService _plans = null!;
    private AcquisitionDraftImportService _imports = null!;
    private readonly CountingBuilder _builder = new();

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services => services.AddScoped(_ =>
            new DownloadMethodRegistry([], [], null!, null!)));
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _plans = new(_db, [_builder], _services.GetRequiredService<IAcquisitionService>(),
            _services.GetRequiredService<IWorkflowDefinitionService>(), _services.GetRequiredService<DownloadIntentService>(),
            _services.GetRequiredService<IUserStoragePolicy>());
        _imports = new(_db, _plans, _services.GetRequiredService<IResourceService>(),
            _services.GetRequiredService<IReservedPropertyValueService>(), _services.GetRequiredService<IResourceSourceLinkService>());
    }

    [TestMethod]
    public async Task TitleOnlyDraftDoesNotSearchCreateResourceOrStartAndContinuesOnSameTask()
    {
        var beforeResources = await _db.ResourcesV2.CountAsync();
        var draft = await _plans.CreateDraftAsync(new("A title with no source"), default);
        Assert.AreEqual("preparing", draft.Task.Stage);
        Assert.AreEqual("draft", draft.Task.BuilderId);
        Assert.IsNull(draft.Task.ResourceId); Assert.IsNull(draft.Task.WorkflowRunId);
        Assert.AreEqual(0, _builder.Searches);
        var chosen = await _plans.SetDraftBuilderAsync(draft.Id, new(draft.Task.PlanRevision, "fixture.search"), default);
        Assert.AreEqual(0, _builder.Searches, "Choosing a builder must not start network work.");
        var found = await _plans.SearchAsync(draft.Id, "A refined title", default);
        var selected = await _plans.SelectAsync(draft.Id, new("one", found.Task.PlanRevision), default);
        Assert.AreEqual(draft.Id, chosen.Id); Assert.AreEqual(draft.Id, selected.Id);
        Assert.AreEqual(1, await _db.AcquisitionTasks.CountAsync());
        Assert.AreEqual(beforeResources, await _db.ResourcesV2.CountAsync());
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual("preparing", selected.Task.Stage, "A prepared source without an output directory still needs information.");
    }

    [TestMethod]
    public async Task DraftEditsUseRevisionAndKeepSourcesWithoutCombiningThemAsMirrors()
    {
        var draft = await _plans.CreateDraftAsync(new("Two leads", [
            new() {Id = "a", Url = "https://example.invalid/a", ArchivePassword = "fixture-password"},
            new() {Id = "b", Url = "https://example.invalid/b"}]), default);
        Assert.IsNull(draft.Plan);
        Assert.AreEqual(2, draft.DraftSources.Count);
        var edited = await _plans.UpdateDraftAsync(draft.Id, new(draft.Task.PlanRevision, Title: "Renamed"), default);
        Assert.AreEqual(2, edited.DraftSources.Count);
        Assert.AreEqual("fixture-password", edited.DraftSources[0].ArchivePassword);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _plans.UpdateDraftAsync(draft.Id,
            new(draft.Task.PlanRevision, Title: "stale"), default));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _plans.UpdateDraftAsync(draft.Id,
            new(edited.Task.PlanRevision, Sources: [new() {Id = "a", Url = "https://user:password@example.invalid/"}]), default));
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task ExplicitPreparationRechecksCapabilitiesAndDoesNotTrustForgedProducerBindings()
    {
        var draft = await _plans.CreateDraftAsync(new("Unknown source"), default);
        var plan = _builder.BuildPlan(new("one", "One", "https://unknown.invalid/page"), "Unknown source", "");
        plan = plan with {Parts = [plan.Parts.Single() with
        {Sources = [new() {Id = "one", Url = "https://unknown.invalid/page", Kind = "auto"}],
            ProcessingSource = new("postParser", "task", "123", "1", "0"), AppliedProcessingRevisionId = "forged"}]};
        var result = await _plans.PrepareDraftAsync(draft.Id, new(draft.Task.PlanRevision, plan), default);
        Assert.IsNull(result.Plan!.Parts.Single().ProcessingSource);
        Assert.IsNull(result.Plan.Parts.Single().AppliedProcessingRevisionId);
        Assert.IsTrue(result.Issues.Any(i => i.Blocking));
        Assert.AreEqual("attention", result.Task.Stage);
        Assert.AreEqual(1, result.PlanHistory.Count);
        Assert.IsNull(result.Task.WorkflowRunId);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task ListImportCreatesOnlyIndependentDraftsAndRepeatedRowsAreIdempotent()
    {
        var beforeResources = await _db.ResourcesV2.CountAsync();
        var rows = new AcquisitionDraftListInput([
            new("Title only", null, "unbound-fixture-password", 1), new("With source", "https://example.invalid/a", "fixture", 2),
            new("Invalid", "file:///private/file", null, 3)]);
        var first = await _imports.ImportListAsync(rows, default);
        var again = await _imports.ImportListAsync(rows, default);
        Assert.AreEqual(2, first.Created); Assert.AreEqual(1, first.Problems.Count);
        Assert.AreEqual(0, again.Created); Assert.AreEqual(2, again.Matched);
        CollectionAssert.AreEqual(first.TaskIds.ToArray(), again.TaskIds.ToArray());
        var tasks = await _db.AcquisitionTasks.AsNoTracking().ToListAsync();
        Assert.IsTrue(tasks.All(t => t.ResourceId == null && t.WorkflowRunId == null && t.BuilderId == "draft"));
        Assert.AreEqual(beforeResources, await _db.ResourcesV2.CountAsync());
        Assert.AreEqual("unbound-fixture-password", (await _plans.GetAsync(first.TaskIds[0], default))!.ImportedListRow!.Password);
        Assert.AreEqual(0, await _db.AcquisitionLeads.CountAsync());
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task LegacyImportPreservesExactSnapshotAndExistingTasksWithoutLibraryMutation()
    {
        var placeholders = _services.GetRequiredService<IPlaceholderResourceService>();
        var id = (await placeholders.CreateByTitle("Preserved overview item")).ResourceId;
        var already = (await placeholders.CreateByTitle("Existing acquisition")).ResourceId;
        var refsJson = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("fixture", "source", "42", "2", "1")}, WorkflowJson.Options);
        var processing = "{\"requirement\":\"required\",\"steps\":[]}";
        var lead = new AcquisitionLeadDbModel {ResourceId = id, Kind = AcquisitionLeadKind.SharedPage,
            Value = "https://example.invalid/share", AccessCode = "code", Password = "fixture-password", IsResolved = true,
            ExtractionPlanJson = processing, SourceRefsJson = refsJson, SourceReference = "saved snapshot", Note = "keep note", CreatedAt = DateTime.Now};
        _db.AcquisitionLeads.Add(lead);
        var old = new AcquisitionTaskDbModel {ResourceId = already, Status = AcquisitionStatus.Cancelled,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        _db.AcquisitionTasks.Add(old); await _db.SaveChangesAsync();
        await _services.GetRequiredService<IResourceSourceLinkService>().EnsureLinks(id,
            [new ResourceSourceLink {Source = ResourceSource.Steam, SourceKey = "fixture-holding"}]);
        var resourceCount = await _db.ResourcesV2.CountAsync();
        var first = await _imports.ImportLegacyAsync(new([id, already]), default);
        var repeated = await _imports.ImportLegacyAsync(new([id, already]), default);
        Assert.AreEqual(1, first.Created); Assert.AreEqual(1, first.Matched);
        Assert.AreEqual(0, repeated.Created); Assert.AreEqual(2, repeated.Matched);
        CollectionAssert.AreEquivalent(first.TaskIds.ToArray(), repeated.TaskIds.ToArray());
        var detail = await _plans.GetAsync(first.TaskIds.Single(t => t != old.Id), default);
        Assert.IsNotNull(detail); Assert.IsNull(detail.Task.ResourceId); Assert.AreEqual(id, detail.Task.OriginResourceId);
        Assert.AreEqual(2, detail.LegacyLeads.Count);
        var saved = detail.LegacyLeads.Single(l => l.Kind == AcquisitionLeadKind.SharedPage);
        Assert.AreEqual("fixture-password", saved.Password); Assert.AreEqual("code", saved.AccessCode);
        Assert.AreEqual(processing, saved.ExtractionPlanJson); Assert.AreEqual(refsJson, saved.SourceRefsJson);
        Assert.AreEqual("keep note", saved.Note); Assert.AreEqual("saved snapshot", saved.SourceReference);
        Assert.IsTrue(detail.LegacyLeads.Any(l => l.Kind == AcquisitionLeadKind.PlatformHolding && l.Value == "Steam:fixture-holding"));
        Assert.IsTrue(detail.SourceRefs.Any(r => r.Module == "resource" && r.EntityId == id.ToString()));
        Assert.AreEqual(resourceCount, await _db.ResourcesV2.CountAsync());
        Assert.AreEqual(1, await _db.AcquisitionLeads.CountAsync());
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(AcquisitionStatus.Cancelled, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == old.Id)).Status);
        var beforeRead = await _db.AcquisitionTasks.CountAsync();
        await _services.GetRequiredService<IAcquisitionService>().SearchAsync();
        await _services.GetRequiredService<IAcquisitionService>().SearchAsync();
        Assert.AreEqual(beforeRead, await _db.AcquisitionTasks.CountAsync(), "Task reads never synchronize library candidates.");
    }

    [TestMethod]
    public async Task OversizedLegacyInputsRemainInSnapshotAndConcurrentImportsShareOneDraft()
    {
        var originalTitle = new string('t', 600);
        var id = (await _services.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(originalTitle)).ResourceId;
        var oversized = "https://example.invalid/" + new string('a', 2100);
        _db.AcquisitionLeads.AddRange(
            new AcquisitionLeadDbModel {ResourceId = id, Kind = AcquisitionLeadKind.SharedPage, Value = oversized,
                AccessCode = new string('c', 513), Password = "preserved", CreatedAt = DateTime.Now},
            new AcquisitionLeadDbModel {ResourceId = id, Kind = AcquisitionLeadKind.DirectUrl,
                Value = "https://pan.baidu.com/s/1fixture", IsResolved = true, ExtractionPlanJson = "null", CreatedAt = DateTime.Now});
        await _db.SaveChangesAsync();
        var results = await Task.WhenAll(_imports.ImportLegacyAsync(new([id]), default), _imports.ImportLegacyAsync(new([id]), default));
        Assert.AreEqual(1, results.Sum(r => r.Created)); Assert.AreEqual(1, results.Sum(r => r.Matched));
        Assert.AreEqual(1, results.SelectMany(r => r.TaskIds).Distinct().Count());
        var detail = (await _plans.GetAsync(results[0].TaskIds.Single(), default))!;
        Assert.AreEqual(originalTitle, detail.LegacyTitle);
        Assert.AreEqual(512, detail.Task.Title!.Length);
        Assert.AreEqual(oversized, detail.LegacyLeads.Single(l => l.Value == oversized).Value);
        Assert.AreEqual("preserved", detail.LegacyLeads.Single(l => l.Value == oversized).Password);
        Assert.AreEqual(1, detail.DraftSources.Count);
        Assert.AreEqual("auto", detail.DraftSources.Single().Kind, "A known site lead is not downgraded to generic HTTP.");
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [DataTestMethod]
    [DataRow(AcquisitionStatus.Waiting, "preparing", false, "preparing")]
    [DataRow(AcquisitionStatus.Waiting, "ready", false, "ready")]
    [DataRow(AcquisitionStatus.Waiting, "executing", true, "attention")]
    [DataRow(AcquisitionStatus.Running, "executing", true, "running")]
    [DataRow(AcquisitionStatus.Failed, "ready", false, "attention")]
    [DataRow(AcquisitionStatus.Completed, "executing", true, "completed")]
    [DataRow(AcquisitionStatus.Cancelled, "completed", true, "cancelled")]
    public void LifecycleProjectionNeverTreatsFailedOrCancelledAsReadyOrCompleted(AcquisitionStatus status, string phase, bool hasRun, string expected)
    {
        var task = new AcquisitionTask {Status = status, Phase = phase, BuilderId = "draft",
            WorkflowRunId = hasRun ? 1 : null, TargetDirectory = "/fixture"};
        Assert.AreEqual(expected, task.Stage);
    }

    [TestMethod]
    public async Task LegacyAndDynamicListAndSourceSummaryFollowAssociatedRunStatus()
    {
        var source = new WorkflowSourceRef("fixture", "entry", "42");
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var tasks = new[] {new AcquisitionTaskDbModel {Title = "legacy"}, new AcquisitionTaskDbModel {Title = "dynamic", BuilderId = "prepared", Phase = "ready"}};
        foreach (var row in tasks)
        {row.Status = AcquisitionStatus.Running; row.WorkflowRunId = run.Id; row.SourceRefsJson = JsonSerializer.Serialize(new[] {source}, WorkflowJson.Options); row.CreatedAt = row.UpdatedAt = DateTime.Now;}
        _db.AcquisitionTasks.AddRange(tasks); await _db.SaveChangesAsync();
        var service = _services.GetRequiredService<IAcquisitionService>();
        var list = await service.SearchAsync();
        Assert.IsTrue(list.All(t => t.Stage == "attention"));
        Assert.AreEqual("attention", (await service.GetAsync(tasks[0].Id))!.Stage);
        Assert.AreEqual("attention", (await _plans.BySourcesAsync([source], default)).Single().Tasks.Single().Stage);
    }

    private sealed class CountingBuilder : IAcquisitionPlanBuilder
    {
        public int Searches { get; private set; }
        public AcquisitionPlanBuilderDescriptor Descriptor => new("fixture.search", "fixture", "fixture", ["manual"], "title");
        public Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct)
        {Searches++; return Task.FromResult<IReadOnlyList<AcquisitionPlanCandidate>>([new("one", "One", "https://example.invalid/file.zip")]);}
        public AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory) => new()
        {Title = title, OutputDirectory = outputDirectory, Parts = [new() {Id = "main", SelectedSourceId = candidate.Id,
            Sources = [new() {Id = candidate.Id, Url = candidate.Url, Kind = "http"}], Processing = new() {Requirement = "notRequired"}}]};
    }
}
