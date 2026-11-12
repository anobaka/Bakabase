using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionSizeEstimateTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionTaskSearchService _search = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize] public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _search = new(_db, _services.GetRequiredService<IAcquisitionService>());
    }
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<AcquisitionTaskDbModel> TaskWithParts(params string[] parts)
    {
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Pending, StartedAt = DateTime.Now};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Size test", WorkflowRunId = run.Id, Status = AcquisitionStatus.Running,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Parts = parts.Select(id => new AcquisitionPlanPart {Id = id}).ToList()}, Json)};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        return task;
    }

    private async Task<ArtifactDbModel> Artifact(AcquisitionTaskDbModel task, string slot, long bytes,
        string? existingOperation = null, DateTime? updated = null)
    {
        var id = Guid.NewGuid().ToString("N");
        if (existingOperation != null)
        {
            _db.ArtifactOperationBindings.Add(new() {Id = id, OwnerKind = "workflow", OwnerId = task.WorkflowRunId!.Value.ToString(),
                Slot = slot, OperationId = existingOperation});
            await _db.SaveChangesAsync();
            return await _db.Artifacts.SingleAsync(a => a.OperationId == existingOperation);
        }
        var artifact = new ArtifactDbModel {Id = id, OperationId = id, RootPath = "/never-inspect-size-test",
            ManifestJson = JsonSerializer.Serialize(new ArtifactManifest([new("file", bytes, "hash")], "fingerprint"), Json)};
        _db.Artifacts.Add(artifact);
        _db.ArtifactOperations.Add(new() {Id = id, OutputArtifactId = id, State = ArtifactOperationState.Completed,
            UpdatedAt = updated ?? DateTime.UtcNow});
        _db.ArtifactOperationBindings.Add(new() {Id = id, OwnerKind = "workflow", OwnerId = task.WorkflowRunId!.Value.ToString(),
            Slot = slot, OperationId = id});
        await _db.SaveChangesAsync(); return artifact;
    }

    [TestMethod]
    public async Task RecentProcessingUsesActualAttemptsAcrossRevisionsBeforePagination()
    {
        var resumed = await TaskWithParts("p");
        var tied = await TaskWithParts("p");
        var newerRun = await TaskWithParts("p");
        var untouched = await TaskWithParts("p");
        var clock = new DateTime(2026, 6, 1, 4, 0, 0, DateTimeKind.Utc);
        foreach (var run in _db.WorkflowRuns.Local)
            run.StartedAt = clock.ToLocalTime().AddHours(-3);
        var tieAt = clock.AddMinutes(5);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = resumed.WorkflowRunId!.Value, NodeId = "process",
            StartedAt = clock, CompletedAt = tieAt, Status = "waiting"});
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = tied.WorkflowRunId!.Value, NodeId = "process",
            StartedAt = tieAt, Status = "running"});
        _db.WorkflowRuns.Local.Single(r => r.Id == newerRun.WorkflowRunId).WaitingSince = clock.ToLocalTime();
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = resumed.WorkflowRunId.Value,
            Module = "acquisition", EntityKind = "task", EntityId = resumed.Id.ToString()});
        resumed.WorkflowRunId = null;
        untouched.WorkflowRunId = null;
        untouched.UpdatedAt = clock.AddDays(1).ToLocalTime();
        await _db.SaveChangesAsync();

        var page = await _search.SearchAsync(new(Sort: "lastProcessed", PageSize: 1, Page: 2));
        CollectionAssert.AreEqual(new[] {tied.Id, resumed.Id, newerRun.Id, untouched.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(resumed.Id, page.Items.Single().Id);
    }

    [TestMethod]
    public async Task SortsAllMatchesBeforePaginationWithUnknownLastAndStableTies()
    {
        var small = await TaskWithParts("p"); await Artifact(small, "p:artifact.download", 10);
        var large = await TaskWithParts("p"); await Artifact(large, "p:artifact.publish", 100);
        var tie = await TaskWithParts("p"); await Artifact(tie, "p:artifact.download", 10);
        var unknown = await TaskWithParts("p");
        var asc = await _search.SearchAsync(new(Sort: "sizeAsc", PageSize: 1));
        Assert.AreEqual(tie.Id, asc.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {tie.Id, small.Id, large.Id, unknown.Id}, asc.FilteredTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {tie.Id}, asc.SizeEstimates.Keys.ToArray());
        var desc = await _search.SearchAsync(new(Sort: "sizeDesc", PageSize: 1));
        Assert.AreEqual(large.Id, desc.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {large.Id, tie.Id, small.Id, unknown.Id}, desc.FilteredTaskIds.ToArray());
        var last = await _search.SearchAsync(new(Sort: "sizeDesc", Page: 4, PageSize: 1));
        Assert.IsNull(last.SizeEstimates[unknown.Id].Bytes);
        Assert.AreEqual("unknown", last.SizeEstimates[unknown.Id].Basis);
        Assert.AreEqual(unknown.Id, (await _search.SearchAsync(new(PageSize: 1))).Items.Single().Id);
        Assert.AreEqual(unknown.Id, (await _search.SearchAsync(new(PageSize: 1, Sort: null))).Items.Single().Id);
    }

    [TestMethod]
    public async Task ChoosesBestCurrentArtifactPerPartAndDeduplicatesSharedOutputsAndFiles()
    {
        var task = await TaskWithParts("a", "b");
        await Artifact(task, "a:artifact.download", 1000);
        await Artifact(task, "a:process:old", 900, updated: DateTime.UtcNow.AddHours(-2));
        await Artifact(task, "a:process:new", 800);
        var published = await Artifact(task, "a:artifact.publish", 200);
        // Two bindings to the same artifact and repeated manifest entries are one measured output.
        await Artifact(task, "b:artifact.publish", 0, published.OperationId);
        published.ManifestJson = JsonSerializer.Serialize(new ArtifactManifest(
            [new("file", 200, "hash"), new("file", 200, "hash")], "fingerprint"), Json);
        var other = await TaskWithParts("a"); await Artifact(other, "a:artifact.publish", 9999);
        // Old revisions retain history, but the current run pointer owns estimates.
        task.PlanHistoryJson = "unread malformed history";
        await _db.SaveChangesAsync();
        var estimate = (await new AcquisitionSizeEstimateProjection(_db).ReadAsync([task.Id]))[task.Id];
        Assert.AreEqual(200L, estimate.Bytes);
        Assert.AreEqual("delivered", estimate.Basis);
        Assert.IsFalse(estimate.Partial);
        Assert.AreEqual("unread malformed history", task.PlanHistoryJson);
    }

    [TestMethod]
    public async Task DownloadInputsRemainReferencesAndMissingPartsRemainPartial()
    {
        var task = await TaskWithParts("known", "missing");
        await Artifact(task, "known:artifact.download", 40);
        var estimate = (await new AcquisitionSizeEstimateProjection(_db).ReadAsync([task.Id]))[task.Id];
        Assert.AreEqual(40L, estimate.Bytes);
        Assert.AreEqual("downloadReference", estimate.Basis);
        Assert.IsTrue(estimate.Partial);
        await Artifact(task, "known:process:complete", 400);
        await Artifact(task, "missing:process:complete", 25);
        estimate = (await new AcquisitionSizeEstimateProjection(_db).ReadAsync([task.Id]))[task.Id];
        Assert.AreEqual(425L, estimate.Bytes);
        Assert.AreEqual("processed", estimate.Basis);
        Assert.IsFalse(estimate.Partial);
    }

    [TestMethod]
    public async Task OldRunsAndUnboundArtifactsCannotSupplyCurrentEstimates()
    {
        var task = await TaskWithParts("p"); await Artifact(task, "p:artifact.publish", 10);
        var nextRun = new WorkflowRunDbModel {Status = WorkflowRunStatus.Pending, StartedAt = DateTime.Now};
        _db.WorkflowRuns.Add(nextRun); await _db.SaveChangesAsync();
        task.WorkflowRunId = nextRun.Id; await _db.SaveChangesAsync();
        Assert.IsNull((await new AcquisitionSizeEstimateProjection(_db).ReadAsync([task.Id]))[task.Id].Bytes);
    }

    [TestMethod]
    public async Task MalformedOversizedAndConflictingManifestsAreUnknownWhileMeasuredEmptyIsZero()
    {
        var task = await TaskWithParts("p"); var artifact = await Artifact(task, "p:artifact.publish", 10);
        var projection = new AcquisitionSizeEstimateProjection(_db);
        foreach (var invalid in new[] {"malformed", "{\"entries\":[\"not an object\"]}",
                     JsonSerializer.Serialize(new ArtifactManifest([new("same", 1, "a"), new("same", 2, "b")], "f"), Json),
                     new string(' ', 1048577) + "{}"})
        {
            artifact.ManifestJson = invalid; await _db.SaveChangesAsync();
            Assert.IsNull((await projection.ReadAsync([task.Id]))[task.Id].Bytes);
        }
        artifact.ManifestJson = JsonSerializer.Serialize(new ArtifactManifest([], "empty"), Json);
        await _db.SaveChangesAsync();
        Assert.AreEqual(0L, (await projection.ReadAsync([task.Id]))[task.Id].Bytes);
    }
}
