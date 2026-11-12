using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionCreationOriginTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionPlanService _plans = null!;
    private IAcquisitionService _acquisitions = null!;
    private readonly GenericProvider _generic = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            // Provenance fixtures do not initialize installed download tools or real accounts.
            services.RemoveAll<DownloadMethodRegistry>();
            services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
            services.AddSingleton<IAcquisitionCreationOriginProvider>(_generic);
            services.AddSingleton<IAcquisitionPlanBuilder>(new SearchBuilder());
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _plans = _services.GetRequiredService<AcquisitionPlanService>();
        _acquisitions = _services.GetRequiredService<IAcquisitionService>();
    }
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<PostParserTaskDbModel> Post(string url = "https://forum.example/thread/123")
    {
        var post = new PostParserTaskDbModel {Title = "Original post", Link = url, Revision = 7,
            ContentSnapshotJson = "not readable: provenance must not load a post body", Results = "not parser result JSON"};
        _db.PostParserTasks.Add(post); await _db.SaveChangesAsync(); return post;
    }
    private static AcquisitionPlan Plan() => new() {Title = "Acquired work", OutputDirectory = "", Parts = [new()
    {
        Id = "main", SelectedSourceId = "download", Processing = new() {Requirement = "notRequired"},
        Sources = [new() {Id = "download", Url = "https://download.example/file.zip", Kind = "http"}]
    }]};
    private static WorkflowSourceRef Ref(PostParserTaskDbModel post) => new("postParser", "task", post.Id.ToString(), "5");

    [TestMethod]
    public async Task ListAndDetailExposeOneProducerPerRevisionAndReadsDoNotChangeHistory()
    {
        var post = await Post(); var source = Ref(post);
        var detail = await _plans.CreatePreparedPlanAsync(Plan(), [source, source with {ResultKey = "0"}, source with {ResultKey = "1"}]);
        var created = detail.Task.CreationOrigins.Single();
        Assert.AreEqual("postParser", created.Kind); Assert.AreEqual(post.Id.ToString(), created.EntityId);
        Assert.AreEqual("5", created.Revision, "The producer revision is frozen, not overwritten with its current revision.");
        Assert.AreEqual(post.Link, created.Url); Assert.AreEqual("available", created.SourceState);
        var saved = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == detail.Id);
        var page = await _services.GetRequiredService<AcquisitionTaskSearchService>().SearchAsync(new(Keyword: "#" + detail.Id));
        Assert.AreEqual(created, page.Items.Single().CreationOrigins.Single());
        Assert.AreEqual(created, (await _acquisitions.GetAsync(detail.Id))!.CreationOrigins.Single());
        Assert.AreEqual(created, (await _plans.GetAsync(detail.Id, default))!.Task.CreationOrigins.Single());
        var after = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == detail.Id);
        Assert.AreEqual(saved.BuilderStateJson, after.BuilderStateJson); Assert.AreEqual(saved.SourceRefsJson, after.SourceRefsJson);
        Assert.AreEqual(saved.PlanHistoryJson, after.PlanHistoryJson); Assert.AreEqual(saved.UpdatedAt, after.UpdatedAt);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync(), "Provenance reads never execute an acquisition.");
    }

    [TestMethod]
    public async Task CreationSnapshotRetainsOriginalUrlWhenPostChangesIsDeletedOrIsMissing()
    {
        var post = await Post(); var original = post.Link;
        var detail = await _plans.CreatePreparedPlanAsync(Plan(), [Ref(post)]);
        post.Link = "https://forum.example/thread/new"; post.Title = "Changed post"; post.IsDeleted = true;
        await _db.SaveChangesAsync();
        var deleted = (await _acquisitions.GetAsync(detail.Id))!.CreationOrigins.Single();
        Assert.AreEqual("deleted", deleted.SourceState); Assert.AreEqual(original, deleted.Url);
        Assert.AreEqual("Original post", deleted.Title);
        _db.PostParserTasks.Remove(post); await _db.SaveChangesAsync();
        var missing = (await _acquisitions.GetAsync(detail.Id))!.CreationOrigins.Single();
        Assert.AreEqual("missing", missing.SourceState); Assert.AreEqual(original, missing.Url);
        Assert.AreEqual(post.Id.ToString(), missing.EntityId);
    }

    [TestMethod]
    public async Task LegacyReferencesUseExactProducerIdentityWithoutWritingABackfill()
    {
        var post = await Post(); var source = Ref(post);
        var run = new WorkflowRunDbModel {Sources = [new() {Module = source.Module, EntityKind = source.EntityKind,
            EntityId = source.EntityId, Revision = source.Revision!, ResultKey = "0"}], StartedAt = DateTime.Now};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {BuilderId = "prepared", WorkflowRunId = run.Id,
            BuilderStateJson = "{\"sourceRefs\":[]}", SourceRefsJson = "malformed legacy JSON", Title = "Legacy",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        var origin = (await _acquisitions.GetAsync(task.Id))!.CreationOrigins.Single();
        Assert.AreEqual(source.EntityId, origin.EntityId); Assert.AreEqual(post.Link, origin.Url);
        var saved = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.AreEqual("malformed legacy JSON", saved.SourceRefsJson);
        Assert.AreEqual("{\"sourceRefs\":[]}", saved.BuilderStateJson);
    }

    [TestMethod]
    public async Task MissingProducerIsNotGuessedByMatchingTitleOrDownloadUrl()
    {
        var post = await Post();
        var task = new AcquisitionTaskDbModel {BuilderId = "prepared", Title = post.Title, LeadValue = post.Link,
            SourceRefsJson = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("postParser", "task", "999999", "1")}, Json)};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        var origin = (await _acquisitions.GetAsync(task.Id))!.CreationOrigins.Single();
        Assert.AreEqual("missing", origin.SourceState); Assert.AreEqual("999999", origin.EntityId);
        Assert.IsNull(origin.Url); Assert.IsNull(origin.Title);
    }

    [TestMethod]
    public async Task OtherModulesAndUpstreamAcquisitionsRemainGenericAndOnlySelfReferenceIsRemoved()
    {
        var first = new AcquisitionTaskDbModel {BuilderId = "prepared", Title = "First"};
        var second = new AcquisitionTaskDbModel {BuilderId = "prepared", Title = "Second"};
        _db.AcquisitionTasks.AddRange(first, second); await _db.SaveChangesAsync();
        first.SourceRefsJson = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("testProducer", "item", "a", "2"),
            new WorkflowSourceRef("testProducer", "item", "a", "2", "download"),
            new WorkflowSourceRef("acquisition", "task", first.Id.ToString()),
            new WorkflowSourceRef("acquisition", "task", second.Id.ToString())}, Json);
        second.SourceRefsJson = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("testProducer", "item", "b")}, Json);
        await _db.SaveChangesAsync();
        var tasks = await _acquisitions.GetByIdsAsync([first.Id, second.Id]);
        Assert.AreEqual(1, _generic.Calls); Assert.AreEqual(2, _generic.References.Count);
        var firstOrigins = tasks.Single(t => t.Id == first.Id).CreationOrigins;
        Assert.AreEqual(2, firstOrigins.Count); Assert.IsFalse(firstOrigins.Any(o => o.Module == "acquisition" && o.EntityId == first.Id.ToString()));
        Assert.IsTrue(firstOrigins.Any(o => o.Module == "acquisition" && o.EntityId == second.Id.ToString()));
        Assert.AreEqual("module", firstOrigins.First(o => o.Module == "testProducer").Kind);
    }

    [TestMethod]
    public async Task ManualAndNameSearchCreationRemainDistinctFromUnknownHistoricalProvenance()
    {
        var manual = await _plans.CreateDraftAsync(new("Manual target"), default);
        Assert.AreEqual("manual", manual.Task.CreationOrigins.Single().Kind);
        var search = await _plans.CreateAsync(new("Find work", "test.search"), default);
        Assert.AreEqual("nameSearch", search.Task.CreationOrigins.Single().Kind);
        Assert.AreEqual("test.search", search.Task.CreationOrigins.Single().BuilderId);
        var old = new AcquisitionTaskDbModel {Title = "Old unknown", BuilderId = "prepared", BuilderStateJson = "malformed", SourceRefsJson = "null"};
        _db.AcquisitionTasks.Add(old); await _db.SaveChangesAsync();
        Assert.AreEqual("unknown", (await _acquisitions.GetAsync(old.Id))!.CreationOrigins.Single().Kind);
    }

    [TestMethod]
    public async Task LegacyPostLeadCanRetainSafeOriginalUrlWithoutInventingATaskId()
    {
        var lead = new AcquisitionLeadDbModel {ResourceId = 1, Kind = AcquisitionLeadKind.DirectUrl,
            Value = "https://download.example/file", Origin = AcquisitionLeadOrigin.PostParser,
            SourceReference = "https://forum.example/thread/retained"};
        _db.Set<AcquisitionLeadDbModel>().Add(lead); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {AcquisitionLeadId = lead.Id, BuilderId = "prepared", Title = "Old lead"};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        var origin = (await _acquisitions.GetAsync(task.Id))!.CreationOrigins.Single();
        Assert.AreEqual("postParser", origin.Kind); Assert.AreEqual("snapshot", origin.SourceState);
        Assert.AreEqual(lead.SourceReference, origin.Url); Assert.IsNull(origin.EntityId);
        lead.SourceReference = "javascript:alert(1)"; await _db.SaveChangesAsync();
        var unsafeOrigin = (await _acquisitions.GetAsync(task.Id))!.CreationOrigins.Single();
        Assert.IsNull(unsafeOrigin.Url); Assert.AreEqual("unknown", unsafeOrigin.Kind);
    }

    private sealed class GenericProvider : IAcquisitionCreationOriginProvider
    {
        public string Module => "testProducer";
        public int Calls { get; private set; }
        public IReadOnlyList<WorkflowSourceRef> References { get; private set; } = [];
        public Task<IReadOnlyDictionary<WorkflowSourceRef, AcquisitionCreationOrigin>> ResolveAsync(IReadOnlyList<WorkflowSourceRef> references, CancellationToken ct)
        {
            Calls++; References = references;
            return Task.FromResult<IReadOnlyDictionary<WorkflowSourceRef, AcquisitionCreationOrigin>>(references.ToDictionary(r => r,
                r => new AcquisitionCreationOrigin {Kind = "module", Module = r.Module, EntityKind = r.EntityKind, EntityId = r.EntityId,
                    Revision = r.Revision, Title = "Producer " + r.EntityId, Url = "https://producer.example/item/" + r.EntityId, SourceState = "available"}));
        }
    }
    private sealed class SearchBuilder : IAcquisitionPlanBuilder
    {
        public AcquisitionPlanBuilderDescriptor Descriptor => new("test.search", "test.name", "test.description", ["manual"], "title");
        public Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AcquisitionPlanCandidate>>([]);
        public AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory) => Plan();
    }
}
