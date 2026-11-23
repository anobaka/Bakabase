using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Real workflow executions share process-wide task/run gates with other database fixtures.
public sealed class AcquisitionQueueSchedulingTests
{
    // Only the interval is shortened. Persistence and the reconcile/pump body remain production code.
    private sealed class FastQueue(IServiceProvider services, IBakabaseLocalizer localizer)
        : AcquisitionQueueTask(services, localizer)
    {
        public int Ticks;
        public override TimeSpan? GetInterval() => TimeSpan.FromMilliseconds(20);
        public override async Task RunAsync(BTaskArgs args)
        {
            await base.RunAsync(args);
            Interlocked.Increment(ref Ticks);
        }
    }

    private sealed class LocalStep : IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.queue-scheduling";
        public string Kind => StepKind;
        public string DisplayName => "Local scheduling fixture";
        public Type? ConfigType => null;
        public IReadOnlyList<AcquisitionLeadKind> AcceptedLeadKinds => [AcquisitionLeadKind.DirectUrl];
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _entered = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _release = new();
        public Task Entered(int taskId) => Gate(_entered, taskId).Task;
        public void Release(int taskId) => Gate(_release, taskId).TrySetResult();

        private static TaskCompletionSource Gate(ConcurrentDictionary<int, TaskCompletionSource> gates, int taskId) =>
            gates.GetOrAdd(taskId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct)
        {
            var taskId = ctx.AcquisitionTaskId ?? throw new InvalidOperationException("Fixture requires an acquisition task.");
            Gate(_entered, taskId).TrySetResult();
            await Gate(_release, taskId).Task.WaitAsync(ct);
            return new AcquisitionStepOutcome.Continue(item);
        }
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Success, AcquisitionStatus.Completed)]
    [DataRow(WorkflowRunStatus.Waiting, AcquisitionStatus.Waiting)]
    public async Task PendingStartupTasksStartTogether_AndReconcileIndependently(
        WorkflowRunStatus releaseStatus, AcquisitionStatus expectedStatus)
    {
        var services = await TestServiceBuilder.BuildServiceProvider(collection => collection.AddAcquisitionStep<LocalStep>());
        var manager = services.GetRequiredService<BTaskManager>();
        try
        {
            int firstId;
            int secondId;
            int thirdId;
            int firstRunId;
            await using (var scope = services.CreateAsyncScope())
            {
                var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
                var recipe = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
                {
                    Name = "Startup queue fixture",
                    Enabled = true,
                    TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                    Activities = [new WorkflowActivityInputModel { Kind = LocalStep.StepKind, ConfigJson = "{}" }]
                });
                var placeholders = scope.ServiceProvider.GetRequiredService<IPlaceholderResourceService>();
                var firstResource = await placeholders.CreateByTitle("Already dispatched");
                var secondResource = await placeholders.CreateByTitle("Pending at startup");
                var thirdResource = await placeholders.CreateByTitle("Also pending at startup");
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                var first = await acquisitions.CreateAsync(firstResource.ResourceId, AcquisitionLeadKind.DirectUrl,
                    "https://example.invalid/first", recipeDefinitionId: recipe.Id);
                firstId = first.Id;
                firstRunId = first.WorkflowRunId!.Value;

                // The daemon is deliberately not started. A dispatched workflow remains Pending
                // without preventing the pump from dispatching every unfinished startup row.
                var queue = scope.ServiceProvider.GetRequiredService<IAcquisitionQueue>();
                await queue.ReconcileAsync();
                Assert.AreEqual(AcquisitionStatus.Running, (await acquisitions.GetAsync(firstId))!.Status);
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                var pending = new[] { secondResource.ResourceId, thirdResource.ResourceId }
                    .Select(resourceId => new AcquisitionTaskDbModel
                    {
                        ResourceId = resourceId,
                        RecipeDefinitionId = recipe.Id,
                        LeadKind = AcquisitionLeadKind.DirectUrl,
                        LeadValue = $"https://example.invalid/{resourceId}",
                        Status = AcquisitionStatus.Pending,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    }).ToArray();
                db.Set<AcquisitionTaskDbModel>().AddRange(pending);
                await db.SaveChangesAsync();
                var second = pending[0];
                secondId = second.Id;
                thirdId = pending[1].Id;
                await queue.PumpAsync();
                foreach (var id in new[] { secondId, thirdId })
                {
                    var dispatched = (await acquisitions.GetAsync(id))!;
                    Assert.AreEqual(AcquisitionStatus.Running, dispatched.Status);
                    Assert.IsNotNull(dispatched.WorkflowRunId);
                }
                await queue.PumpAsync();
                Assert.AreEqual(3, await db.Set<WorkflowRunDbModel>().CountAsync(),
                    "Repeated queue ticks must not duplicate already dispatched workflows.");
            }

            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                await db.Set<WorkflowRunDbModel>().Where(r => r.Id == firstRunId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, releaseStatus)
                    .SetProperty(r => r.CompletedAt, releaseStatus == WorkflowRunStatus.Success ? DateTime.Now : (DateTime?) null));
                var queue = scope.ServiceProvider.GetRequiredService<IAcquisitionQueue>();
                await queue.ReconcileAsync();
                await queue.PumpAsync();
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                Assert.AreEqual(expectedStatus, (await acquisitions.GetAsync(firstId))!.Status);
                foreach (var id in new[] { secondId, thirdId })
                    Assert.AreEqual(AcquisitionStatus.Running, (await acquisitions.GetAsync(id))!.Status,
                        "One workflow completing or waiting must not change the other workflows.");
            }
        }
        finally
        {
            await manager.DisposeAsync();
        }
    }

    [TestMethod]
    [Timeout(90000)]
    public async Task PeriodicQueueSurvivesCleanup_AndSameRecipeExecutionsOverlap()
    {
        var services = await TestServiceBuilder.BuildServiceProvider(collection =>
        {
            collection.AddAcquisitionStep<LocalStep>();
            collection.RemoveAll<IPredefinedBTaskBuilder>();
            collection.AddSingleton<DynamicTaskRegistry>();
            collection.AddSingleton<FastQueue>(sp => new FastQueue(sp, sp.GetRequiredService<IBakabaseLocalizer>()));
            collection.AddSingleton<IPredefinedBTaskBuilder>(sp => sp.GetRequiredService<FastQueue>());
        });
        var manager = services.GetRequiredService<BTaskManager>();
        var queue = services.GetRequiredService<FastQueue>();
        var step = services.GetRequiredService<LocalStep>();
        var acquisitionIds = new List<int>();

        async Task<string> DescribeState()
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var acquisitions = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                .Where(t => acquisitionIds.Contains(t.Id)).ToListAsync();
            var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().ToListAsync();
            return $"Queue ticks: {Volatile.Read(ref queue.Ticks)}. " +
                   string.Join("; ", acquisitions.Select(t => $"acquisition {t.Id}: {t.Status}, run={t.WorkflowRunId}, completed={t.CompletedAt}, error={t.Error}")) +
                   ". " + string.Join("; ", runs.Select(r => $"run {r.Id}: {r.Status}, error={r.ErrorMessage}")) +
                   ". " + string.Join("; ", manager.GetTasksViewModel().Select(t => $"BTask {t.Id}: {t.Status}, error={t.Error}, blocked={t.ReasonForUnableToStart}"));
        }

        async Task<bool> IsCompleted(int taskId)
        {
            // Observe persisted state without repeatedly hydrating names, resources and recipe
            // metadata. The daemon scans once per second even with the fixture's 20 ms interval.
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var task = await db.Set<AcquisitionTaskDbModel>().AsNoTracking().SingleAsync(t => t.Id == taskId);
            if (task.Status is AcquisitionStatus.Failed or AcquisitionStatus.Cancelled)
                Assert.Fail(await DescribeState());
            return task.Status == AcquisitionStatus.Completed && task.CompletedAt != null && task.WorkflowRunId != null;
        }

        try
        {
            await services.GetRequiredService<DynamicTaskRegistry>().RegisterAllTasksAsync();
            await manager.Initialize();
            await WaitUntil("first queue tick", () => Task.FromResult(Volatile.Read(ref queue.Ticks) >= 1 &&
                manager.GetTaskViewModel(queue.Id)?.Status == BTaskStatus.Completed), DescribeState);

            await manager.CleanInactive();
            Assert.IsNotNull(manager.GetTaskViewModel(queue.Id), "A completed queue heartbeat must survive task-list cleanup.");
            await WaitUntil("queue runs after cleanup", () => Task.FromResult(Volatile.Read(ref queue.Ticks) >= 2), DescribeState);

            int firstId;
            int secondId;
            int secondResourceId;
            int recipeId;
            await using (var scope = services.CreateAsyncScope())
            {
                var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
                var recipe = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
                {
                    Name = "Scheduler fixture",
                    Enabled = true,
                    TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                    Activities = [new WorkflowActivityInputModel {Kind = LocalStep.StepKind, ConfigJson = "{}"}]
                });
                var placeholders = scope.ServiceProvider.GetRequiredService<IPlaceholderResourceService>();
                var firstResource = await placeholders.CreateByTitle("First scheduler resource");
                var secondResource = await placeholders.CreateByTitle("Second scheduler resource");
                secondResourceId = secondResource.ResourceId;
                recipeId = recipe.Id;
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                var first = await acquisitions.CreateAsync(firstResource.ResourceId, AcquisitionLeadKind.DirectUrl,
                    "https://example.invalid/first", recipeDefinitionId: recipe.Id);
                Assert.AreEqual(AcquisitionStatus.Running, first.Status);
                firstId = first.Id;
                acquisitionIds.Add(firstId);
            }

            // Wait for actual execution, then keep the first activity blocked while creating a
            // second task with the same recipe. Row status alone cannot prove concurrency.
            await WaitUntil("first workflow enters its step", () => Task.FromResult(step.Entered(firstId).IsCompleted), DescribeState);
            await using (var scope = services.CreateAsyncScope())
            {
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                var second = await acquisitions.CreateAsync(secondResourceId, AcquisitionLeadKind.DirectUrl,
                    "https://example.invalid/second", recipeDefinitionId: recipeId);
                Assert.AreEqual(AcquisitionStatus.Running, second.Status);
                Assert.IsNotNull(second.WorkflowRunId);
                secondId = second.Id;
                acquisitionIds.Add(secondId);
            }

            await WaitUntil("second workflow enters while the first is blocked",
                () => Task.FromResult(step.Entered(secondId).IsCompleted), DescribeState);
            Assert.IsFalse(await IsCompleted(firstId), "The first activity has not been released.");

            // Release the second workflow first to prove independent completion. The real daemon
            // and periodic queue perform all execution and reconciliation.
            step.Release(secondId);
            await WaitUntil("second acquisition completes independently", () => IsCompleted(secondId), DescribeState);
            Assert.IsFalse(await IsCompleted(firstId));
            step.Release(firstId);
            await WaitUntil("first acquisition completes", () => IsCompleted(firstId), DescribeState);
            Assert.IsTrue(Volatile.Read(ref queue.Ticks) >= 3);
        }
        finally
        {
            await manager.DisposeAsync();
        }
    }

    private static async Task WaitUntil(string phase, Func<Task<bool>> ready, Func<Task<string>> describeState)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(12))
        {
            if (await ready()) return;
            await Task.Delay(100);
        }
        Assert.Fail($"The real scheduler did not reach '{phase}' within 12 seconds. {await describeState()}");
    }
}
