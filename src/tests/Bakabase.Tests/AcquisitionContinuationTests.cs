using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass, DoNotParallelize]
public sealed partial class AcquisitionContinuationTests
{
    [TestMethod]
    public async Task PreviewAndEnqueueDoNotRunDiagnosisAndEachDispatchVerifiesOnlyOnce()
    {
        using var f = await Fixture.Create();
        var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), false);
        Assert.AreEqual(0, f.Diagnostics.Calls);
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [1, 2]), false);
        Assert.AreEqual(0, f.Diagnostics.Calls); Assert.AreEqual(0, f.Host.Calls);
        await f.Service.TickAsync(default);
        Assert.AreEqual(1, f.Diagnostics.Calls); Assert.AreEqual(1, f.Host.Calls);
        var first = await f.Service.GetAsync(job.BatchId);
        Assert.AreEqual("submitted", first.Rows[0].Status); Assert.AreEqual("queued", first.Rows[1].Status);
        await f.Service.TickAsync(default);
        Assert.AreEqual(2, f.Diagnostics.Calls); Assert.AreEqual(2, f.Host.Calls);
        Assert.AreEqual("finished", (await f.Service.GetAsync(job.BatchId)).Status);
        Assert.IsTrue((await f.Db.AcquisitionTasks.AsNoTracking().ToArrayAsync()).All(t => t.Status == AcquisitionStatus.Waiting),
            "A submitted continuation is not delivered-resource completion.");
    }
    [TestMethod]
    public async Task ApplyIsIdempotentAndCommittedJobCanBeFoundAfterPreviewStoreIsLost()
    {
        using var f = await Fixture.Create(); var preview = await f.Service.PreviewAsync(new([1], "workflow"), false);
        var input = new AcquisitionContinuationApplyInput(preview.PreviewToken, [1]);
        var job = await f.Service.ApplyAsync(input, false);
        Assert.AreEqual(job.BatchId, (await f.Service.ApplyAsync(input, false)).BatchId);
        Assert.AreEqual(job.BatchId, (await f.NewService().GetByPreviewAsync(preview.PreviewToken)).BatchId);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.ApplyAsync(new(preview.PreviewToken, [2]), false));
    }
    [TestMethod]
    public async Task ChangedPlanIsNotContinuedAndNoStorageGrantIsInstalled()
    {
        using var f = await Fixture.Create(); var preview = await f.Service.PreviewAsync(new([1], "workflow"), false);
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [1]), false);
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, 2));
        await f.Service.TickAsync(default);
        Assert.AreEqual(0, f.Host.Calls); Assert.AreEqual("skipped", (await f.Service.GetAsync(job.BatchId)).Rows.Single().Status);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
    }
    [TestMethod]
    public async Task LostResponseAndRestartNeverRepeatAcceptedContinuation()
    {
        using var f = await Fixture.Create(); f.Host.LoseResponse = true;
        var preview = await f.Service.PreviewAsync(new([1], "workflow"), false);
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [1]), false);
        await f.Service.TickAsync(default); await f.Service.TickAsync(default);
        Assert.AreEqual(1, f.Host.Calls); Assert.AreEqual("attention", (await f.Service.GetAsync(job.BatchId)).Rows.Single().Status);
        var row = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var state = AcquisitionContinuationEntry.Read(row)!;
        var interruptedJson = AcquisitionContinuationEntry.Write(row.BuilderStateJson, state with {Status = "checking"});
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson,
            interruptedJson));
        var restarted = f.NewService(); await restarted.RecoverInterruptedAsync(default); await restarted.TickAsync(default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual("RECOVERY_OUTCOME_UNKNOWN", (await restarted.GetAsync(job.BatchId)).Rows.Single().ReasonCode);
    }
    [TestMethod]
    public async Task CancelOnlyPendingRequestsAndRestrictedPathsAreRechecked()
    {
        using var f = await Fixture.Create(); var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), true);
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [1, 2]), true);
        f.Paths.Allow = false;
        await f.Service.TickAsync(default);
        Assert.AreEqual("CONTINUATION_PATH_FORBIDDEN", (await f.Service.GetAsync(job.BatchId)).Rows[0].ReasonCode);
        await f.Service.CancelAsync(job.BatchId);
        Assert.AreEqual("cancelled", (await f.Service.GetAsync(job.BatchId)).Rows[1].Status);
        Assert.AreEqual(0, f.Diagnostics.Calls); Assert.AreEqual(0, f.Host.Calls);
    }
    [TestMethod]
    public async Task ExistingLocalQueueCannotBeSelectedIntoContinuation()
    {
        using var f = await Fixture.Create(); var task = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var state = new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"), Version = "v", Signal = "{}",
            RecoveryScope = "workflow", StorageStamp = "s", Status = "queued"};
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson,
            AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, state)));
        var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), false);
        Assert.IsFalse(preview.Rows[0].CanQueue); Assert.AreEqual("LOCAL_BATCH_ALREADY_QUEUED", preview.Rows[0].ReasonCode);
        Assert.IsTrue(preview.Rows[1].CanQueue);
    }

    [TestMethod]
    public async Task ActiveLocalRecoveryOnlyExcludesItsOwnTaskFromDeferredContinuation()
    {
        using var f = await Fixture.Create();
        var task = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var state = new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"), Version = "v", Signal = "{}",
            RecoveryScope = "workflow", StorageStamp = "s", Status = "running", WorkflowRunId = 1};
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson,
            AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, state)));
        await f.Db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), false);
        Assert.IsFalse(preview.Rows[0].CanQueue);
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [2]), false);
        await f.Service.TickAsync(default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual("submitted", (await f.Service.GetAsync(job.BatchId)).Rows.Single().Status);
        Assert.AreEqual(WorkflowRunStatus.Running, (await f.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 1)).Status);
    }
    [TestMethod]
    public async Task RelativeProcessingInstructionsAndFileMetadataAreNotFilesystemPaths()
    {
        using var f = await Fixture.Create();
        var plan = JsonSerializer.Serialize(new {outputDirectory = Path.GetFullPath(Path.GetTempPath()),
            processing = new {steps = new[] {new {op = "moveFile", targetDirectory = "author-folder", sourceDirectory = "old"}}},
            files = new[] {new {name = "relative.txt", sha256 = "not-a-path", length = 12}}});
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanJson, plan));
        var preview = await f.Service.PreviewAsync(new([1], "workflow"), true);
        Assert.IsTrue(preview.Rows.Single().CanQueue);
        Assert.AreEqual(0, f.Diagnostics.Calls);
    }
    [TestMethod]
    public async Task InvalidCheckpointOnlyBlocksItsOwnRow()
    {
        using var f = await Fixture.Create();
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanJson, "broken json"));
        var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), false);
        Assert.IsFalse(preview.Rows[0].CanQueue);
        Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", preview.Rows[0].ReasonCode);
        Assert.IsTrue(preview.Rows[1].CanQueue);
    }
    [TestMethod]
    public async Task RelatedDownloadPathFailureDoesNotStarveTheFollowingRequest()
    {
        using var f = await Fixture.Create();
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "continuation-test"));
        f.Db.DownloadTasks.Add(new() {Id = 101, DownloadPath = directory,
            Options = JsonSerializer.Serialize(new {operationId = "op", source = new {id = "source", url = "https://example.invalid/file"}, outputDirectory = directory})});
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var plan = JsonSerializer.Serialize(new {outputDirectory = directory, downloadTaskId = 101});
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanJson,
            plan));
        var preview = await f.Service.PreviewAsync(new([1, 2], "workflow"), false);
        Assert.IsTrue(preview.Rows.All(r => r.CanQueue));
        var job = await f.Service.ApplyAsync(new(preview.PreviewToken, [1, 2]), false);
        // A related row can change without changing the task/run version. Dispatch must
        // reject it once, before invoking diagnostics or the workflow host.
        await f.Db.DownloadTasks.Where(t => t.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(t => t.Options, "broken json"));
        await f.Service.TickAsync(default);
        var failed = (await f.Service.GetAsync(job.BatchId)).Rows[0];
        Assert.AreEqual("attention", failed.Status); Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", failed.ReasonCode);
        Assert.AreEqual(0, f.Diagnostics.Calls); Assert.AreEqual(0, f.Host.Calls);
        await f.Service.TickAsync(default);
        Assert.AreEqual("submitted", (await f.Service.GetAsync(job.BatchId)).Rows[1].Status);
        Assert.AreEqual(1, f.Diagnostics.Calls); Assert.AreEqual(1, f.Host.Calls);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection;
        internal readonly BakabaseDbContext Db;
        internal readonly Diagnostics Diagnostics = new();
        internal readonly Host Host;
        internal readonly Paths Paths = new();
        internal readonly AcquisitionContinuationService Service;
        private Fixture(SqliteConnection c)
        {
            connection = c; Db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(c).Options);
            Host = new(Db); Service = NewService();
        }
        internal AcquisitionContinuationService NewService() => new(Db, new(Db, Diagnostics, new(), Host), new(), null!, Paths, null!);
        internal static async Task<Fixture> Create()
        {
            var c = new SqliteConnection("Data Source=:memory:"); await c.OpenAsync(); var f = new Fixture(c);
            await f.Db.Database.EnsureCreatedAsync();
            for (var id = 1; id <= 2; id++)
            {
                f.Db.AcquisitionTasks.Add(new() {Id = id, Title = "Task", BuilderId = "test", PlanRevision = 1,
                    PlanJson = JsonSerializer.Serialize(new {outputDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "continuation-test"))}),
                    Status = AcquisitionStatus.Waiting, WorkflowRunId = id, BuilderStateJson = "{}"});
                f.Db.WorkflowRuns.Add(new() {Id = id, Status = WorkflowRunStatus.Waiting, PlanSnapshotJson = "{}"});
            }
            await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); return f;
        }
        public void Dispose() {Db.Dispose(); connection.Dispose();}
    }
    private sealed class Diagnostics : IAcquisitionRecoveryDiagnostics
    {
        internal int Calls;
        public Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
            CancellationToken ct, bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false,
            bool includeStoppedVerification = false, bool deferCompletedDownloadVerification = false)
        {
            Assert.IsTrue(verifyFiles && verifyContent); Calls++;
            return Task.FromResult(new AcquisitionRecoveryDiagnosis(new(task.Id, task.Title, run?.Id, true, "resume", "READY")));
        }
    }
    private sealed class Host(BakabaseDbContext db) : IWorkflowRunResumer
    {
        internal int Calls; internal bool LoseResponse;
        public Task ResumeAsync(int id, string signal, CancellationToken ct = default) => throw new AssertFailedException();
        public Task RequeueAsync(int id, CancellationToken ct = default) => throw new AssertFailedException();
        public Task UpdateWaitAsync(int id, string reason, string? prompt, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int id, bool waiting, string signal, Func<CancellationToken, Task> validate, CancellationToken ct = default)
        {
            await validate(ct); Calls++;
            await db.WorkflowRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Pending), ct);
            if (LoseResponse) throw new InvalidOperationException("response lost");
        }
    }
    private sealed class Paths : IMediaPathGuard
    {
        internal bool Allow = true;
        public Task<bool> IsServableAsync(string? path, CancellationToken ct = default) => Task.FromResult(Allow);
        public Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public void Invalidate() { }
    }
}
