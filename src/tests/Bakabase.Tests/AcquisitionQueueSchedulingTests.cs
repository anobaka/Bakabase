using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
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
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct)
        {
            await Release.Task.WaitAsync(ct);
            return new AcquisitionStepOutcome.Continue(item);
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task PeriodicQueueSurvivesItsFirstTick_ThenCompletesAndStartsLaterAcquisitions()
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
        services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.Concurrency = 1;
        try
        {
            await services.GetRequiredService<DynamicTaskRegistry>().RegisterAllTasksAsync();
            await manager.Initialize();
            await WaitUntil(() => Task.FromResult(Volatile.Read(ref queue.Ticks) >= 1 &&
                manager.GetTaskViewModel(queue.Id)?.Status == BTaskStatus.Completed));

            await manager.CleanInactive();
            Assert.IsNotNull(manager.GetTaskViewModel(queue.Id), "A completed queue heartbeat must survive task-list cleanup.");
            await WaitUntil(() => Task.FromResult(Volatile.Read(ref queue.Ticks) >= 2));

            int firstId;
            int secondId;
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
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                var first = await acquisitions.CreateAsync(firstResource.ResourceId, AcquisitionLeadKind.DirectUrl,
                    "https://example.invalid/first", recipeDefinitionId: recipe.Id);
                var second = await acquisitions.CreateAsync(secondResource.ResourceId, AcquisitionLeadKind.DirectUrl,
                    "https://example.invalid/second", recipeDefinitionId: recipe.Id);
                Assert.AreEqual(AcquisitionStatus.Running, first.Status);
                Assert.AreEqual(AcquisitionStatus.Pending, second.Status);
                Assert.IsNull(second.WorkflowRunId);
                firstId = first.Id;
                secondId = second.Id;
            }

            services.GetRequiredService<LocalStep>().Release.TrySetResult();

            // The real daemon executes workflow.run.*, then later queue ticks mirror success and
            // release the next slot. No manual runner, reconciliation, pump or status update.
            await WaitUntil(async () =>
            {
                await using var scope = services.CreateAsyncScope();
                var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();
                var first = await acquisitions.GetAsync(firstId);
                var second = await acquisitions.GetAsync(secondId);
                return first?.Status == AcquisitionStatus.Completed && first.CompletedAt != null &&
                       second?.Status == AcquisitionStatus.Completed && second.CompletedAt != null && second.WorkflowRunId != null;
            });
            Assert.IsTrue(Volatile.Read(ref queue.Ticks) >= 3);
        }
        finally
        {
            await manager.DisposeAsync();
        }
    }

    private static async Task WaitUntil(Func<Task<bool>> ready)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(12))
        {
            if (await ready()) return;
            await Task.Delay(20);
        }
        Assert.Fail("The real scheduler did not reach the expected state within 12 seconds.");
    }
}
