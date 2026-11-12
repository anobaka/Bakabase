using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowCheckpointFailureRecoveryTests
{
    private sealed class FaultContext(DbContextOptions<FaultContext> options) : DbContext(options)
    {
        public DbSet<WorkflowRunDbModel> Runs => Set<WorkflowRunDbModel>();
        public bool FailAfterCommit { get; set; }
        public Exception? InjectedFailure { get; set; }
        public Func<Task>? BeforeFailure { get; set; }
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            if (InjectedFailure != null && !FailAfterCommit)
            {
                if (BeforeFailure != null) await BeforeFailure();
                throw InjectedFailure;
            }
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
            if (InjectedFailure != null) throw InjectedFailure;
            return result;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _keepAlive;
        public ServiceProvider Services { get; }
        public Fixture()
        {
            var connectionString = $"Data Source=checkpoint-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
            Services = new ServiceCollection().AddDbContext<FaultContext>(o => o.UseSqlite(connectionString)).BuildServiceProvider();
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await _keepAlive.DisposeAsync(); }
    }

    private static async Task<WorkflowRunDbModel> Seed(FaultContext db)
    {
        await db.Database.EnsureCreatedAsync();
        var run = new WorkflowRunDbModel
        {
            Status = WorkflowRunStatus.Running, StartedAt = DateTime.Now,
            CurrentStepIndex = 2, CurrentItemJson = "{\"downloadTaskId\":42,\"receipt\":\"preserve\"}",
            PlanSnapshotJson = "frozen-plan", PayloadJson = "original-payload", StepStatsJson = "upstream-history",
            NodeExecutions = [new() {NodeId = "await", Kind = "download.awaitCompletion", Attempt = 1,
                StartedAt = DateTime.UtcNow, Status = "running", OutputsJson = "[{\"taskId\":\"site:42\"}]"}]
        };
        db.Runs.Add(run); await db.SaveChangesAsync(); return run;
    }

    [TestMethod]
    public async Task InvalidOriginalConnectionRecordsInterruptionWithoutPublishingUncommittedCheckpoint()
    {
        await using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FaultContext>();
        var run = await Seed(db);
        var guard = WorkflowCheckpointFailureRecovery.Capture(run);
        var originalNode = run.NodeExecutions.Single();
        run.CurrentStepIndex = 3; run.CurrentItemJson = "uncommitted-item"; run.Status = WorkflowRunStatus.Success;
        run.OutputItemsJson = "uncommitted-output"; originalNode.Status = "succeeded";
        originalNode.OutputsJson = "uncommitted-node-output";
        db.InjectedFailure = new ObjectDisposedException("SQLitePCL.sqlite3");
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => WorkflowCheckpointFailureRecovery.SaveFinalAsync(
            db, fixture.Services.GetRequiredService<IServiceScopeFactory>(), guard, NullLogger.Instance));
        await using var inspect = fixture.Services.CreateAsyncScope();
        var fresh = inspect.ServiceProvider.GetRequiredService<FaultContext>();
        Assert.AreNotEqual(db.ContextId, fresh.ContextId);
        var actual = await fresh.Runs.Include(r => r.NodeExecutions).SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Interrupted, actual.Status);
        Assert.AreEqual(2, actual.CurrentStepIndex);
        Assert.AreEqual(guard.Run.CurrentItemJson, actual.CurrentItemJson);
        Assert.AreEqual(guard.Run.PlanSnapshotJson, actual.PlanSnapshotJson);
        Assert.AreEqual(guard.Run.StepStatsJson, actual.StepStatsJson);
        Assert.AreEqual(guard.Run.PayloadJson, actual.PayloadJson);
        Assert.IsNull(actual.OutputItemsJson);
        Assert.AreEqual("running", actual.NodeExecutions.Single().Status);
        Assert.AreEqual(guard.LastNodeOutputs, actual.NodeExecutions.Single().OutputsJson);
        Assert.AreEqual(WorkflowCheckpointFailureRecovery.FailureMessage, actual.ErrorMessage);
        Assert.IsNotNull(actual.CompletedAt);
    }

    [DataTestMethod]
    [DataRow("cursor")]
    [DataRow("status")]
    [DataRow("attempt")]
    [DataRow("metadata")]
    [DataRow("truncated")]
    [DataRow("nodeId")]
    [DataRow("nodeKind")]
    [DataRow("nodeAttempt")]
    [DataRow("nodeStart")]
    [DataRow("signal")]
    [DataRow("plan")]
    [DataRow("item")]
    public async Task IndependentConcurrentProgressIsNeverOverwritten(string change)
    {
        await using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FaultContext>();
        var run = await Seed(db); var guard = WorkflowCheckpointFailureRecovery.Capture(run);
        db.InjectedFailure = new ObjectDisposedException("SQLitePCL.sqlite3");
        db.BeforeFailure = async () =>
        {
            await using var concurrent = fixture.Services.CreateAsyncScope();
            var other = concurrent.ServiceProvider.GetRequiredService<FaultContext>();
            var row = await other.Runs.Include(r => r.NodeExecutions).SingleAsync();
            switch (change)
            {
                case "cursor": row.CurrentStepIndex++; break;
                case "status": row.Status = WorkflowRunStatus.Success; break;
                case "attempt": row.NodeExecutions.Add(new() {NodeId="await", Kind="download.awaitCompletion", Attempt=2, StartedAt=DateTime.UtcNow}); break;
                case "metadata": row.NodeExecutions.Single().OutputsJson = "new-durable-output"; break;
                case "truncated": row.NodeExecutions.Single().OutputsTruncated = true; break;
                case "nodeId": row.NodeExecutions.Single().NodeId = "new-node"; break;
                case "nodeKind": row.NodeExecutions.Single().Kind = "new-kind"; break;
                case "nodeAttempt": row.NodeExecutions.Single().Attempt = 2; break;
                case "nodeStart": row.NodeExecutions.Single().StartedAt = row.NodeExecutions.Single().StartedAt.AddSeconds(1); break;
                case "signal": row.PendingSignalJson = "new-signal"; break;
                case "plan": row.PlanSnapshotJson = "new-plan"; break;
                case "item": row.CurrentItemJson = "new-item"; break;
            }
            await other.SaveChangesAsync();
        };
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => WorkflowCheckpointFailureRecovery.SaveFinalAsync(
            db, fixture.Services.GetRequiredService<IServiceScopeFactory>(), guard, NullLogger.Instance));
        await using var inspect = fixture.Services.CreateAsyncScope();
        var actual = await inspect.ServiceProvider.GetRequiredService<FaultContext>().Runs.Include(r=>r.NodeExecutions).SingleAsync();
        Assert.AreEqual(change == "status" ? WorkflowRunStatus.Success : WorkflowRunStatus.Running, actual.Status);
        Assert.IsNull(actual.ErrorMessage); Assert.IsNull(actual.CompletedAt);
        switch (change)
        {
            case "cursor": Assert.AreEqual(3, actual.CurrentStepIndex); break;
            case "attempt": Assert.AreEqual(2, actual.NodeExecutions.Count); break;
            case "metadata": Assert.AreEqual("new-durable-output",actual.NodeExecutions.Single().OutputsJson); break;
            case "truncated": Assert.IsTrue(actual.NodeExecutions.Single().OutputsTruncated); break;
            case "nodeId": Assert.AreEqual("new-node",actual.NodeExecutions.Single().NodeId); break;
            case "nodeKind": Assert.AreEqual("new-kind",actual.NodeExecutions.Single().Kind); break;
            case "nodeAttempt": Assert.AreEqual(2,actual.NodeExecutions.Single().Attempt); break;
            case "nodeStart": Assert.AreEqual(guard.LastNodeStartedAt!.Value.AddSeconds(1),actual.NodeExecutions.Single().StartedAt); break;
            case "signal": Assert.AreEqual("new-signal",actual.PendingSignalJson); break;
            case "plan": Assert.AreEqual("new-plan",actual.PlanSnapshotJson); break;
            case "item": Assert.AreEqual("new-item",actual.CurrentItemJson); break;
        }
    }

    [TestMethod]
    public async Task AmbiguousFailureAfterSuccessfulCommitPreservesCommittedSuccess()
    {
        await using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FaultContext>();
        var run = await Seed(db); var guard = WorkflowCheckpointFailureRecovery.Capture(run);
        run.Status = WorkflowRunStatus.Success; run.CurrentStepIndex = null; run.OutputItemsJson = "committed-output";
        db.InjectedFailure = new ObjectDisposedException("SQLitePCL.sqlite3"); db.FailAfterCommit = true;
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => WorkflowCheckpointFailureRecovery.SaveFinalAsync(
            db, fixture.Services.GetRequiredService<IServiceScopeFactory>(), guard, NullLogger.Instance));
        await using var inspect = fixture.Services.CreateAsyncScope();
        var actual = await inspect.ServiceProvider.GetRequiredService<FaultContext>().Runs.SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Success,actual.Status); Assert.IsNull(actual.CurrentStepIndex);
        Assert.AreEqual("committed-output",actual.OutputItemsJson); Assert.IsNull(actual.ErrorMessage);
    }

    [TestMethod]
    public async Task UnrelatedPersistenceFailureIsNotReclassified()
    {
        await using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FaultContext>();
        var run = await Seed(db); var guard = WorkflowCheckpointFailureRecovery.Capture(run);
        db.InjectedFailure = new InvalidOperationException("arbitrary untrusted message");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => WorkflowCheckpointFailureRecovery.SaveFinalAsync(
            db, fixture.Services.GetRequiredService<IServiceScopeFactory>(), guard, NullLogger.Instance));
        await using var inspect = fixture.Services.CreateAsyncScope();
        var actual = await inspect.ServiceProvider.GetRequiredService<FaultContext>().Runs.SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Running,actual.Status); Assert.IsNull(actual.ErrorMessage);
    }
}
