using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class SourceAvailabilityBatchTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private SourceAvailabilityStore _store = null!;
    private SourceAvailabilityService _availability = null!;
    private SourceAvailabilityBatchService _batches = null!;
    private AcquisitionTaskDbModel _task = null!;
    private string _root = null!;
    private Probe _probe = null!;
    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _root = Path.Combine(Path.GetTempPath(), "source-batch-tests-" + Guid.NewGuid().ToString("N"));
        _store = new(_root); _probe = new(); _availability = new(_store, [_probe]);
        _batches = new(_db, _store, _availability, null!, null!);
        _task = new() {Title = "Target", Status = AcquisitionStatus.Waiting, PlanRevision = 1, PlanHistoryJson = "retained history",
            PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = "Target", Parts = [new() {Id = "main", SelectedSourceId = "one",
                Sources = [new() {Id = "one", Url = "https://example.org/file.zip", ProviderId = "http", Kind = "http"}]}]}, JsonSerializerOptions.Web)};
        _db.AcquisitionTasks.Add(_task); await _db.SaveChangesAsync();
    }
    [TestCleanup] public void Cleanup() {(_services as IDisposable)?.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true);}
    private async Task<SourceAvailabilityBatch> Preview()
    {
        var first = (await _batches.SnapshotAsync([_task.Id], default)).Single();
        await _availability.CheckAsync(first.Source, true, default);
        var batch = new SourceAvailabilityBatch {BatchId = Guid.NewGuid().ToString("N"), Status = "ready", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Items = await _batches.SnapshotAsync([_task.Id], default), Processed = 1};
        await _store.SaveBatchAsync(batch, default); return batch;
    }
    [TestMethod]
    public async Task ApplyIsReversibleAndNeverEditsPlanHistoryOrStartsAWorkflow()
    {
        var original = _task.PlanJson; var batch = await Preview(); var key = batch.Items.Single().Row.Key;
        Assert.IsTrue(batch.Items.Single().Row.CanDisable);
        var applied = await _batches.ApplyAsync(new(batch.BatchId, [key], "disable"), default);
        CollectionAssert.AreEqual(new[] {key}, applied.AppliedKeys);
        Assert.IsTrue((await _batches.GetAsync(_task.Id, default)).Single().Disabled);
        var repeated = await _batches.ApplyAsync(new(batch.BatchId, [key], "disable"), default);
        Assert.AreEqual("DECISION_CHANGED", repeated.Skipped.Single().Reason);
        var restore = await Preview();
        Assert.IsTrue(restore.Items.Single().Row.CanRestore);
        Assert.AreEqual(1, (await _batches.ApplyAsync(new(restore.BatchId, [key], "restore"), default)).AppliedKeys.Count);
        Assert.IsFalse((await _batches.GetAsync(_task.Id, default)).Single().Disabled);
        await _db.Entry(_task).ReloadAsync();
        Assert.AreEqual(original, _task.PlanJson); Assert.AreEqual("retained history", _task.PlanHistoryJson);
        Assert.IsNull(_task.WorkflowRunId);
    }
    [TestMethod]
    public async Task PreviewCannotDisableAfterTaskStartsOrPlanChangesOrEvidenceExpires()
    {
        var batch = await Preview(); var key = batch.Items.Single().Row.Key;
        _task.Status = AcquisitionStatus.Running; await _db.SaveChangesAsync();
        Assert.AreEqual("SOURCE_IN_USE", (await _batches.ApplyAsync(new(batch.BatchId, [key], "disable"), default)).Skipped.Single().Reason);
        _task.Status = AcquisitionStatus.Waiting; _task.PlanRevision++; await _db.SaveChangesAsync();
        Assert.AreEqual("SOURCE_CHANGED", (await _batches.ApplyAsync(new(batch.BatchId, [key], "disable"), default)).Skipped.Single().Reason);
        var fresh = await Preview();
        _probe.Status = SourceAvailabilityStatuses.Available;
        await _availability.CheckAsync(fresh.Items.Single().Source, true, default);
        Assert.AreEqual("EVIDENCE_CHANGED", (await _batches.ApplyAsync(new(fresh.BatchId, [key], "disable"), default)).Skipped.Single().Reason);
        await _store.SaveBatchAsync(fresh with {ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)}, default);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _batches.ApplyAsync(new(fresh.BatchId, [key], "disable"), default));
        Assert.IsFalse((await _batches.GetAsync(_task.Id, default)).Single().Disabled);
    }
    [TestMethod]
    public async Task CorrectedCredentialsUseNewEvidenceAndInvalidateAnOldDisablePreviewWithoutChangingThePlan()
    {
        var originalPlan = _task.PlanJson;
        var definition = new WorkflowDefinitionDbModel {Name = "credential view", TriggerKind = CloudWorkflow.Trigger};
        _db.WorkflowDefinitions.Add(definition); await _db.SaveChangesAsync();
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Waiting};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        _task.WorkflowRunId = run.Id; await _db.SaveChangesAsync();
        var oldPreview = await Preview();
        var source = oldPreview.Items.Single().Source;
        var current = source with {AccessCode = "correct", AccessCodeRevisions = [new()
        {CorrectionId = Guid.NewGuid().ToString("N"), ExpectedSourceIdentity = CloudSourceIdentity.Key(source),
            PreviousAccessCode = null, NewAccessCode = "correct", Basis = "manual", AppliedAt = DateTime.UtcNow}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(new DownloadWorkset {Targets = [new()
            {Id = "main", SelectedSourceId = current.Id, Sources = [current]}]});
        await _db.SaveChangesAsync();
        var corrected = (await _batches.SnapshotAsync([_task.Id], default)).Single();
        Assert.AreEqual("correct", corrected.Source.AccessCode);
        Assert.AreEqual("correct", corrected.Row.AccessCode);
        Assert.IsNull(corrected.Row.Availability, "Old credential's cached absence is not evidence for the new credential.");
        Assert.AreNotEqual(oldPreview.Items.Single().PlanHash, corrected.PlanHash);
        var applied = await _batches.ApplyAsync(new(oldPreview.BatchId, [oldPreview.Items.Single().Row.Key], "disable"), default);
        Assert.AreEqual("SOURCE_CHANGED", applied.Skipped.Single().Reason);
        _probe.Status = SourceAvailabilityStatuses.Available;
        await _availability.CheckAsync(corrected.Source, true, default);
        Assert.AreEqual(SourceAvailabilityStatuses.Available, (await _batches.GetAsync(_task.Id, default)).Single().Availability!.Status);
        await _db.Entry(_task).ReloadAsync();
        Assert.AreEqual(originalPlan, _task.PlanJson);
        Assert.AreEqual("retained history", _task.PlanHistoryJson);
    }

    [TestMethod]
    public void CredentialProjectionRejectsBrokenHistoryAndDifferentLocators()
    {
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(_task.PlanJson!, JsonSerializerOptions.Web)!;
        var source = new CloudSource {Id = "one", Url = "https://example.org/file.zip", AccessCode = "new",
            AccessCodeRevisions = [new() {CorrectionId = Guid.NewGuid().ToString("N"), ExpectedSourceIdentity = new string('a', 64),
                PreviousAccessCode = "different", NewAccessCode = "new", Basis = "manual"}]};
        DownloadWorkset Workset(CloudSource s) => new() {Targets = [new() {Id = "main", Sources = [s]}]};
        Assert.IsNull(AcquisitionEffectiveSourceProjection.Apply(plan, Workset(source)).Parts.Single().Sources.Single().AccessCode);
        source = source with {AccessCodeRevisions = [source.AccessCodeRevisions[0] with {PreviousAccessCode = null}]};
        Assert.AreEqual("new", AcquisitionEffectiveSourceProjection.Apply(plan, Workset(source)).Parts.Single().Sources.Single().AccessCode);
        Assert.IsNull(AcquisitionEffectiveSourceProjection.Apply(plan, Workset(source with {Url = "https://other.example/file.zip"}))
            .Parts.Single().Sources.Single().AccessCode);
        Assert.IsNull(plan.Parts.Single().Sources.Single().AccessCode, "Read projection must not mutate the frozen plan.");
    }

    [DataTestMethod]
    [DataRow("{\"targets\":null}")]
    [DataRow("{\"targets\":[null]}")]
    [DataRow("{\"targets\":[{\"id\":\"main\",\"sources\":null}]}")]
    [DataRow("{\"targets\":[{\"id\":\"main\",\"sources\":[null]}]}")]
    [DataRow("{\"targets\":[{\"id\":\"main\",\"sources\":[{\"id\":\"one\",\"url\":\"https://example.org/file.zip\",\"accessCodeRevisions\":[null]}]}]}")]
    public void MalformedCheckpointShapesFailClosedWithoutBreakingListProjection(string itemJson)
    {
        var type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name;
        var snapshot = JsonSerializer.Serialize(new {type, item = JsonSerializer.Deserialize<JsonElement>(itemJson)}, JsonSerializerOptions.Web);
        Assert.IsNull(AcquisitionEffectiveSourceProjection.Read(snapshot));
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(_task.PlanJson!, JsonSerializerOptions.Web)!;
        var malformed = JsonSerializer.Deserialize<DownloadWorkset>(itemJson, JsonSerializerOptions.Web);
        Assert.AreSame(plan, AcquisitionEffectiveSourceProjection.Apply(plan, malformed));
        Assert.AreEqual(_task.PlanJson, AcquisitionEffectiveSourceProjection.Apply(_task.PlanJson, null));
    }

    [TestMethod]
    public void CorrectionProjectionRequiresTypedCheckpointAndDoesNotTrustPromptShapes()
    {
        var source = CorrectedSource("https://example.org/file.zip");
        var workset = new DownloadWorkset {Targets = [new() {Id = "main", Sources = [source]}]};
        Assert.IsNotNull(AcquisitionEffectiveSourceProjection.Read(WorkflowItemSnapshot.Capture(workset)));
        Assert.IsNull(AcquisitionEffectiveSourceProjection.Read(JsonSerializer.Serialize(workset, JsonSerializerOptions.Web)));
        Assert.IsNull(AcquisitionEffectiveSourceProjection.Read(JsonSerializer.Serialize(new {type = "other.Type", item = workset}, JsonSerializerOptions.Web)));
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(_task.PlanJson!, JsonSerializerOptions.Web)!;
        foreach (var malformed in new[] {plan with {Parts = null!}, plan with {Parts = [null!]},
                     plan with {Parts = [plan.Parts[0] with {Sources = null!}]},
                     plan with {Parts = [plan.Parts[0] with {Sources = [null!]}]}})
            Assert.AreSame(malformed, AcquisitionEffectiveSourceProjection.Apply(malformed, workset));
    }

    [TestMethod]
    public void StoredRedirectEvidenceProjectsResolvedCredentialsWithoutResolvingAgain()
    {
        const string original = "https://cm1.hk/s/saved";
        const string resolved = "https://pan.baidu.com/s/saved-final";
        var plan = new AcquisitionPlan {Parts = [new() {Id = "main", Sources = [new() {Id = "one", Url = original}]}]};
        var current = CorrectedSource(resolved) with {ProviderId = "baidu", OriginalUrl = original, ResolutionChain = [original, resolved]};
        AcquisitionPlanSource Project(CloudSource s) => AcquisitionEffectiveSourceProjection.Apply(plan,
            new() {Targets = [new() {Id = "main", Sources = [s]}]}).Parts.Single().Sources.Single();
        var effective = Project(current);
        Assert.AreEqual("new", effective.AccessCode); Assert.AreEqual(resolved, effective.Url);
        Assert.AreEqual("baidu", effective.ProviderId); Assert.AreEqual(original, effective.OriginalUrl);
        CollectionAssert.AreEqual(new[] {original, resolved}, effective.ResolutionChain);
        Assert.AreEqual(original, plan.Parts[0].Sources[0].Url); Assert.IsNull(plan.Parts[0].Sources[0].AccessCode);
        foreach (var invalid in new[]
        {
            current with {OriginalUrl = null}, current with {ResolutionChain = []},
            current with {ResolutionChain = ["https://cm1.hk/s/another", resolved]},
            current with {ResolutionChain = [original, "https://pan.baidu.com/s/another"]},
            current with {ResolutionChain = [original, original, resolved]},
            current with {ResolutionChain = [original, "http://other.example/redirect", resolved]},
            current with {ResolutionChain = [original, "https://127.0.0.1/redirect", resolved]},
            current with {ResolutionChain = [original, "https://user:secret@other.example/redirect", resolved]},
            current with {ResolutionChain = [original, "https://example.org/1", "https://example.org/2", "https://example.org/3",
                "https://example.org/4", "https://example.org/5", resolved]}
        })
        {
            Assert.IsNull(Project(invalid).AccessCode, "Incomplete or unsafe provenance must not authorize a different locator.");
            Assert.AreEqual(original, Project(invalid).Url);
        }
        var whitespace = plan with {Parts = [new() {Id = "main", Sources = [new() {Id = "one", Url = " " + resolved + " "}]}]};
        Assert.AreEqual("new", AcquisitionEffectiveSourceProjection.Apply(whitespace,
            new() {Targets = [new() {Id = "main", Sources = [current]}]}).Parts[0].Sources[0].AccessCode);
    }

    [TestMethod]
    public async Task OrdinaryCheckpointsAreNotLoadedAsCredentialCorrections()
    {
        var ordinary = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting,
            CurrentItemJson = WorkflowItemSnapshot.Capture(new DownloadWorkset {Targets = [new() {Id = "main",
                Sources = [new() {Id = "one", Url = "https://example.org/file.zip"}]}]})};
        var corrected = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting,
            CurrentItemJson = WorkflowItemSnapshot.Capture(new DownloadWorkset {Targets = [new() {Id = "main",
                Sources = [CorrectedSource("https://example.org/file.zip")]}]})};
        _db.WorkflowRuns.AddRange(ordinary, corrected); await _db.SaveChangesAsync();
        var loaded = await AcquisitionEffectiveSourceProjection.ReadCorrectedRunsAsync(_db, [ordinary.Id, corrected.Id], default);
        Assert.IsFalse(loaded.ContainsKey(ordinary.Id)); Assert.IsNotNull(loaded[corrected.Id]);
    }

    [TestMethod]
    public void CompletedSourceDisplayOverlayPreservesOtherTargetsAndFileMetadata()
    {
        var original = new CloudSource {Id = "one", Url = "https://example.org/file.zip"};
        var targets = new List<CloudDownloadTarget>
        {
            new() {Id = "main", Sources = [original], DownloadArtifactId = "artifact", PublishedDirectory = "/retained/delivery",
                Files = ["/retained/file.zip"], PublishedFiles = ["/retained/delivery/file"]},
            new() {Id = "other", Sources = [new() {Id = "two", Url = "https://example.org/other.zip", AccessCode = "other-code"}],
                ProcessedArtifactId = "other-artifact", ProcessedFiles = ["/retained/other/file"]}
        };
        var partial = new DownloadWorkset {Targets = [new() {Id = "main", Sources = [CorrectedSource(original.Url)]}]};
        var displayed = AcquisitionEffectiveSourceProjection.ApplyDisplaySources(targets, partial);
        Assert.AreEqual(2, displayed.Count); Assert.AreEqual("new", displayed[0].Sources[0].AccessCode);
        Assert.AreEqual("artifact", displayed[0].DownloadArtifactId);
        Assert.AreEqual("/retained/delivery", displayed[0].PublishedDirectory);
        CollectionAssert.AreEqual(targets[0].Files, displayed[0].Files);
        CollectionAssert.AreEqual(targets[0].PublishedFiles, displayed[0].PublishedFiles);
        Assert.AreEqual("other-code", displayed[1].Sources[0].AccessCode);
        Assert.AreEqual("other-artifact", displayed[1].ProcessedArtifactId);
        CollectionAssert.AreEqual(targets[1].ProcessedFiles, displayed[1].ProcessedFiles);
        Assert.IsNull(targets[0].Sources[0].AccessCode, "Display overlays never modify the retained payload.");
    }

    private static CloudSource CorrectedSource(string url) => new() {Id = "one", Url = url, AccessCode = "new",
        AccessCodeRevisions = [new() {CorrectionId = Guid.NewGuid().ToString("N"), ExpectedSourceIdentity = new string('a', 64),
            PreviousAccessCode = null, NewAccessCode = "new", Basis = "manual"}]};

    private sealed class Probe : ISourceAvailabilityProbe
    {
        public string Status = SourceAvailabilityStatuses.ConfirmedUnavailable;
        public bool CanHandle(CloudSource source) => true;
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct) => Task.FromResult(new SourceAvailabilityResult
        {Status = Status, ReasonCode = "PROVIDER_REMOVED", ProbeId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)});
    }
}
