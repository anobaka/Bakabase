using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionThroughputDownloadSourceTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private readonly Reader _reader = new();
    private readonly DateTimeOffset _at = DateTimeOffset.UtcNow;

    [TestInitialize] public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
    }
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<(int Task, int Run)> Owner(WorkflowRunStatus state, string kind, bool deleted = false)
    {
        var run = new WorkflowRunDbModel {Status = state, CurrentStepIndex = 0, PlanSnapshotJson = "unreadable history", CurrentItemJson = "unreadable workset"};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Acquisition", WorkflowRunId = run.Id, DeletedAt = deleted ? DateTime.UtcNow : null};
        _db.AcquisitionTasks.Add(task);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, Kind = kind, Status = "running", StepIndex = 0});
        await _db.SaveChangesAsync(); return (task.Id, run.Id);
    }
    private AcquisitionRuntimeDownload Transfer(string id, string? parent, int? run, string status, double? rate,
        string? stage = null, int age = 0, string? problem = null) =>
        new(id, parent, run, status, 50, 1500, 3000, _at.AddSeconds(-age), problem, rate, stage);

    [TestMethod]
    public async Task Counts_each_physical_leaf_once_and_keeps_unknown_zero_and_processing_distinct()
    {
        var owner = await Owner(WorkflowRunStatus.Waiting, "download.awaitCompletion");
        var shared = await Owner(WorkflowRunStatus.Running, "download.awaitCompletion");
        var working = await Owner(WorkflowRunStatus.Running, "files.process");
        var queued = await Owner(WorkflowRunStatus.Pending, "files.process");
        var deleted = await Owner(WorkflowRunStatus.Running, "files.process", true);
        _db.DownloadTasks.Add(new() {Id = 75, Key = "shared", Status = DownloadTaskDbModelStatus.InProgress});
        _db.DownloadResultOwners.Add(new() {DownloadTaskId = 75, AcquisitionTaskId = owner.Task, WorkflowRunId = owner.Run});
        _db.CloudDownloadOperations.Add(new() {Id = "shared", WorkflowRunId = shared.Run, Kind = "download", DownloadTaskId = 75});
        await _db.SaveChangesAsync();
        _reader.Rows = [
            Transfer("site:75", null, null, "running", 900), Transfer("http:child", "site:75", 999999, "running", null),
            Transfer("http:zero", "site:75", null, "running", 0), Transfer("bt:current", "site:75", null, "running", 30),
            Transfer("bt:current", "site:75", null, "running", 10, age: 3),
            Transfer("http:stale", null, shared.Run, "running", 200, age: 90),
            Transfer("bt:error", null, shared.Run, "running", 100, problem: "CONNECTION_FAILED"),
            Transfer("bt:checking", null, shared.Run, "running", 500, "Checking"),
            Transfer("bt:queued", null, queued.Run, "queued", 200), Transfer("site:unknown", null, null, "running", 1000),
            Transfer("bt:deleted", null, deleted.Run, "running", 500), Transfer("http:complete", null, shared.Run, "completed", 50)
        ];
        var result = await new AcquisitionThroughputDownloadSource(_db, _reader, new Clock(_at)).ReadAsync([], default);
        var rows = result.Tasks.ToDictionary(t => t.TaskId);
        CollectionAssert.AreEquivalent(new double?[] {30, 0, null}, rows[owner.Task].Downloads.ToArray());
        CollectionAssert.AreEqual(new double?[] {null, null}, rows[shared.Task].Downloads.ToArray());
        Assert.IsTrue(rows[working.Task].MayHaveFileIo);
        Assert.AreEqual(0, rows[working.Task].Downloads.Count);
        Assert.IsFalse(rows[queued.Task].MayHaveFileIo);
        Assert.AreEqual(0, rows[queued.Task].Downloads.Count);
        Assert.IsFalse(rows.ContainsKey(deleted.Task));
        Assert.AreEqual(4, rows.Count);
        Assert.AreEqual(1, _reader.Calls);
    }

    [TestMethod]
    public async Task Last_window_resolves_real_owners_without_attaching_independent_or_ambiguous_native_jobs()
    {
        var first = await Owner(WorkflowRunStatus.Success, "files.publish");
        var second = await Owner(WorkflowRunStatus.Success, "files.publish");
        var id = Guid.NewGuid().ToString("N"); var sharedId = Guid.NewGuid().ToString("N");
        var unsolicited = Guid.NewGuid().ToString("N");
        _db.DownloadResultOwners.Add(new() {DownloadTaskId = 83, AcquisitionTaskId = first.Task, WorkflowRunId = first.Run});
        _db.CloudDownloadOperations.Add(new() {Id = "cloud-op", Kind = "download", WorkflowRunId = first.Run, DownloadTaskId = 83});
        _db.ArtifactOperationBindings.Add(new() {Id = "binding", Slot = "part:files", OwnerKind = "workflow", OwnerId = first.Run.ToString(), OperationId = "op"});
        _db.ManagedDownloadTasks.AddRange(new() {Id = "http:" + id, Provider = "http", ProviderTaskId = id, ParentId = "site:83"},
            new() {Id = "http:" + sharedId, Provider = "http", ProviderTaskId = sharedId, WorkflowRunId = first.Run}, new() {Id = "bt:" + sharedId, Provider = "bt", ProviderTaskId = sharedId, WorkflowRunId = second.Run},
            new() {Id = "http:" + unsolicited, Provider = "http", ProviderTaskId = unsolicited});
        await _db.SaveChangesAsync();
        FileIoObservationSample Io(string kind, string owner) => new(kind, owner, 4, 0, true, false);
        var result = await new AcquisitionThroughputDownloadSource(_db, _reader, new Clock(_at)).ReadAsync([
            Io("workflow", first.Run.ToString()), Io("downloadTask", "83"), Io("cloudOperation", "cloud-op"), Io("artifactOperation", "op"),
            Io("independentDownload", id), Io("independentDownload", sharedId), Io("independentDownload", unsolicited),
            Io("downloadTask", "84"), Io("unrelated", first.Run.ToString())], default);
        Assert.AreEqual(first.Task, result.Tasks.Single().TaskId);
        Assert.IsFalse(result.Tasks.Single().MayHaveFileIo);
        Assert.AreEqual(5, result.FileOwners.Count);
        Assert.IsTrue(result.FileOwners.Values.All(value => value == first.Task));
        Assert.AreEqual(first.Task, result.FileOwners[("independentDownload", id)]);
        Assert.AreEqual(first.Task, result.FileOwners[("artifactOperation", "op")]);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider {public override DateTimeOffset GetUtcNow() => at;}
    private sealed class Reader : IAcquisitionRuntimeDownloadReader
    {
        public int Calls;
        public IReadOnlyList<AcquisitionRuntimeDownload> Rows = [];
        public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> sites,
            IReadOnlyCollection<string> native, IReadOnlyCollection<int> runs, CancellationToken ct) =>
            throw new AssertFailedException("Sampling must use the lightweight reader.");
        public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadThroughputAsync(IReadOnlyCollection<int> sites,
            IReadOnlyCollection<string> native, IReadOnlyCollection<int> runs, CancellationToken ct)
        { Calls++; return Task.FromResult(Rows); }
    }
}
