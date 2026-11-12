using Bakabase.InsideWorld.Business;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunMutationGuardTests
{
    private sealed class HeldAtStartup : IWorkflowRunStartupPolicy
    {
        public Task<bool> CanResumeOnStartupAsync(int runId, CancellationToken ct = default) => Task.FromResult(false);
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Running)]
    [DataRow(WorkflowRunStatus.Pending)]
    public async Task DurableOwnerPauseSurvivesStartupAndPreservesOriginalCursor(WorkflowRunStatus status)
    {
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IWorkflowRunStartupPolicy, HeldAtStartup>());
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel {Status = status, CurrentStepIndex = 3,
            CurrentItemJson = "original checkpoint", PendingSignalJson = "original answer"};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        var rehydrator = scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>();
        await rehydrator.MarkInterruptedRunsAsync(); await rehydrator.ReEnqueuePendingRunsAsync();
        var saved = await db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Interrupted, saved.Status); Assert.AreEqual(3, saved.CurrentStepIndex);
        Assert.AreEqual("original checkpoint", saved.CurrentItemJson); Assert.AreEqual("original answer", saved.PendingSignalJson);
        Assert.IsFalse(scope.ServiceProvider.GetRequiredService<BTaskManager>().IsPending("workflow.run." + run.Id));
    }

    private sealed class Guard : IWorkflowRunMutationGuard
    {
        public bool Blocked = true;
        public int Calls;
        public Task EnsureCanMutateAsync(int runId, CancellationToken ct = default)
        {
            Calls++;
            if (Blocked) throw new InvalidOperationException("OWNER_REMOVED");
            return Task.CompletedTask;
        }
    }

    [DataTestMethod]
    [DataRow("resume", WorkflowRunStatus.Waiting)]
    [DataRow("retry", WorkflowRunStatus.Failed)]
    [DataRow("updateWait", WorkflowRunStatus.Waiting)]
    [DataRow("conditional", WorkflowRunStatus.Waiting)]
    [DataRow("conditionalRetry", WorkflowRunStatus.Failed)]
    public async Task RemovedOwnerBlocksEveryDirectRunMutationWithoutChangingHistory(string action, WorkflowRunStatus status)
    {
        var guard = new Guard();
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IWorkflowRunMutationGuard>(guard));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel {Status = status, WaitReason = "evidence", WaitPromptJson = "{\"original\":true}",
            CurrentStepIndex = 3, CurrentItemJson = "{\"files\":[\"preserved\"]}", ErrorMessage = "original failure"};
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync();
        var before = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        var resumer = scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>();
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => action switch
        {
            "resume" => resumer.ResumeAsync(run.Id, "new signal"),
            "retry" => resumer.RequeueAsync(run.Id),
            "conditional" or "conditionalRetry" => resumer.ContinueConditionallyAsync(run.Id, status == WorkflowRunStatus.Waiting,
                "{}", _ => throw new AssertFailedException("The owner guard must run before recovery validation")),
            _ => resumer.UpdateWaitAsync(run.Id, "new reason", "new prompt")
        });
        Assert.AreEqual("OWNER_REMOVED", error.Message);
        Assert.AreEqual(1, guard.Calls);
        var after = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(before.WaitReason, after.WaitReason);
        Assert.AreEqual(before.WaitPromptJson, after.WaitPromptJson);
        Assert.AreEqual(before.PendingSignalJson, after.PendingSignalJson);
        Assert.AreEqual(before.CurrentItemJson, after.CurrentItemJson);
        Assert.AreEqual(before.ErrorMessage, after.ErrorMessage);
        Assert.AreEqual(before.CompletedAt, after.CompletedAt);
        Assert.AreEqual(0, await db.WorkflowNodeExecutions.CountAsync());
    }

    [TestMethod]
    public async Task ConditionalRecoveryValidationRunsUnderRunGateAndRejectionPreservesCheckpoint()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Failed, CurrentStepIndex = 2,
            CurrentItemJson = "retained checkpoint", ErrorMessage = "retained error"};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        var called = false;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>()
            .ContinueConditionallyAsync(run.Id, false, "{\"observeExistingOnly\":true}", async ct =>
            {
                called = true;
                Assert.IsFalse(await WorkflowRunMutationGate.For(run.Id).WaitAsync(0, ct));
                throw new InvalidOperationException("PREVIEW_CHANGED");
            }));
        Assert.IsTrue(called);
        var after = await db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Failed, after.Status); Assert.AreEqual("retained checkpoint", after.CurrentItemJson);
        Assert.AreEqual("retained error", after.ErrorMessage); Assert.IsNull(after.PendingSignalJson);
    }

    [TestMethod]
    public async Task OwnershipIsCheckedAfterConcurrentRemovalReleasesRunGate()
    {
        var guard = new Guard {Blocked = false};
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IWorkflowRunMutationGuard>(guard));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        var gate = WorkflowRunMutationGate.For(run.Id);
        await gate.WaitAsync();
        Task pending;
        try
        {
            pending = scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().ResumeAsync(run.Id, "{}");
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(0, guard.Calls);
            guard.Blocked = true;
        }
        finally { gate.Release(); }
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => pending);
        Assert.AreEqual("OWNER_REMOVED", error.Message);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status);
        Assert.IsNull((await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).PendingSignalJson);
    }

    [TestMethod]
    public async Task StartupReEnqueueHonorsOwnerGuardWithoutChangingCheckpointOrStartingBackgroundTask()
    {
        var guard = new Guard();
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IWorkflowRunMutationGuard>(guard));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var definition = new WorkflowDefinitionDbModel {Name = "Owner guarded", TriggerKind = "manual", Enabled = true};
        db.Set<WorkflowDefinitionDbModel>().Add(definition); await db.SaveChangesAsync();
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Pending,
            CurrentStepIndex = 2, CurrentItemJson = "retained checkpoint", PendingSignalJson = "retained answer"};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        var before = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().ReEnqueuePendingRunsAsync();
        Assert.AreEqual(1, guard.Calls);
        Assert.IsFalse(scope.ServiceProvider.GetRequiredService<BTaskManager>().Tasks.Any(t => t.Task.Id == "workflow.run." + run.Id));
        var after = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(before.CurrentItemJson, after.CurrentItemJson);
        Assert.AreEqual(before.PendingSignalJson, after.PendingSignalJson);
        Assert.AreEqual(before.CurrentStepIndex, after.CurrentStepIndex);
        Assert.AreEqual(0, await db.WorkflowNodeExecutions.CountAsync());
    }
}
