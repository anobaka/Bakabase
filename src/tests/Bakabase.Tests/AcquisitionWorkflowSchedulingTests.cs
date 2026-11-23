using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionWorkflowSchedulingTests
{
    private sealed class Options : IBOptions<AcquisitionOptions>
    {
        public AcquisitionOptions Value { get; } = new();
    }

    [DataTestMethod]
    [DataRow(AcquisitionWorkflowKinds.TriggerRequested, 12)]
    [DataRow(CloudWorkflow.Trigger, 0)]
    public async Task AcquisitionsReachTheirOwnActivitiesWhileOtherRunsRemainBusy(string trigger, int definitionId)
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var manager = services.GetRequiredService<BTaskManager>();
        var resolver = services.GetRequiredService<WorkflowRunSchedulingPolicyResolver>();
        var entered = Enumerable.Range(0, 4).Select(_ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        using var ai = new SemaphoreSlim(1, 1);
        var firstAiEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAiEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            for (var index = 0; index < entered.Length; index++)
            {
                var i = index;
                var id = $"workflow.run.{i + 1}";
                var builder = resolver.Configure(BTaskBuilder.Create(id), definitionId, trigger, i + 1,
                    [new WorkflowSourceRef("acquisition", "task", (i + 1).ToString())]);
                Assert.AreEqual(AcquisitionWorkflowSchedulingPolicy.Group, builder.ConcurrencyGroup);
                Assert.IsTrue(builder.ConflictKeys!.SetEquals([id]));
                await manager.Enqueue(builder.Run(async args =>
                {
                    entered[i].TrySetResult();
                    if (i < 2)
                    {
                        await ai.WaitAsync(args.CancellationToken);
                        try
                        {
                            (i == 0 ? firstAiEntered : secondAiEntered).TrySetResult();
                            await releaseAi.Task.WaitAsync(args.CancellationToken);
                        }
                        finally { ai.Release(); }
                    }
                    else await Task.Delay(Timeout.Infinite, args.CancellationToken);
                }));
            }
            await manager.Start("workflow.run.1");
            await firstAiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(Enumerable.Range(2, 3).Select(id => manager.Start($"workflow.run.{id}")));
            await Task.WhenAll(entered.Select(t => t.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(secondAiEntered.Task.IsCompleted, "The activity's AI semaphore still serializes its callers.");
            Assert.AreEqual(4, manager.Tasks.Count(t => t.Task.Status == BTaskStatus.Running));
            releaseAi.TrySetResult();
            await secondAiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseAi.TrySetResult();
            await manager.PrepareForShutdown();
        }
    }

    [TestMethod]
    public void IndependentWorkflowPoliciesKeepTheirOwnConstraints()
    {
        var resolver = new WorkflowRunSchedulingPolicyResolver([new AcquisitionWorkflowSchedulingPolicy(new Options())]);
        var unrelated = resolver.Configure(BTaskBuilder.Create("workflow.run.8"), 12, "other.trigger");
        Assert.IsTrue(unrelated.ConflictKeys!.SetEquals(["workflow.definition.12"]));
        Assert.IsTrue(resolver.ResumeOnStartup(AcquisitionWorkflowKinds.TriggerRequested));
    }

    [TestMethod]
    public async Task LegacyAndDynamicRunsShareTheConfiguredLimitWithoutKnowingTheirCapabilities()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var manager = services.GetRequiredService<BTaskManager>();
        var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        Assert.AreEqual(20, new AcquisitionOptions().Concurrency);
        options.Value.Concurrency = 2;
        var resolver = services.GetRequiredService<WorkflowRunSchedulingPolicyResolver>();
        try
        {
            for (var i = 1; i <= 3; i++)
            {
                var builder = resolver.Configure(BTaskBuilder.Create($"workflow.run.{i}"), i == 1 ? 12 : 0,
                    i == 1 ? AcquisitionWorkflowKinds.TriggerRequested : "custom.capability.trigger", i,
                    [new WorkflowSourceRef("acquisition", "task", i.ToString())]);
                Assert.AreEqual(AcquisitionWorkflowSchedulingPolicy.Group, builder.ConcurrencyGroup);
                await manager.Enqueue(builder.Run(args => Task.Delay(Timeout.Infinite, args.CancellationToken)));
            }
            await Task.WhenAll(Enumerable.Range(1, 3).Select(i => manager.Start($"workflow.run.{i}")));
            Assert.AreEqual(2, manager.Tasks.Count(t => t.Task.Status == BTaskStatus.Running));
            var pending = manager.Tasks.Single(t => t.Task.Status == BTaskStatus.NotStarted);
            options.Value.Concurrency = 1;
            await manager.Start(pending.Id);
            Assert.AreEqual(2, manager.Tasks.Count(t => t.Task.Status == BTaskStatus.Running));
            Assert.AreEqual(BTaskStatus.NotStarted, pending.Task.Status);
            options.Value.Concurrency = 3;
            await manager.Start(pending.Id);
            Assert.AreEqual(3, manager.Tasks.Count(t => t.Task.Status == BTaskStatus.Running));
        }
        finally { await manager.PrepareForShutdown(); }
    }

    [DataTestMethod]
    [DataRow("acquisition", "task", "1", true)]
    [DataRow("postParser", "task", "1", false)]
    [DataRow("acquisition", "resource", "1", false)]
    [DataRow("acquisition", "task", "0", false)]
    [DataRow("acquisition", "task", "not-an-id", false)]
    public void OnlyOwnedRunsReceiveTheAcquisitionLimit(string module, string kind, string id, bool expected)
    {
        var policy = new AcquisitionWorkflowSchedulingPolicy(new Options());
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [policy]);
        var builder = resolver.Configure(BTaskBuilder.Create("workflow.run.27"), 0, "any.trigger", 27,
            [new(module, kind, id)]);
        Assert.AreEqual(expected ? AcquisitionWorkflowSchedulingPolicy.Group : null, builder.ConcurrencyGroup);
        Assert.IsTrue(builder.ConflictKeys!.SetEquals(["workflow.run.27"]));
    }
}
