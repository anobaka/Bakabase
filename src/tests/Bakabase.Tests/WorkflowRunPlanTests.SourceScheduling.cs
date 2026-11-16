using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class WorkflowRunPlanTests
{
    private sealed class OwnerSchedulingPolicy : IWorkflowRunSourceSchedulingPolicy
    {
        public (string? Snapshot, int? Cursor) LastRequest { get; private set; }
        public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder, string? snapshot, int? cursor)
        {
            LastRequest = (snapshot, cursor);
            return Configure(builder);
        }
        public bool AppliesTo(string kind, IReadOnlyList<WorkflowSourceRef> sources) =>
            sources.Any(s => s.Module == "test" && s.EntityKind == "scheduling-owner");
        public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) =>
            builder.WithConcurrencyLimit("test-owner-pool", () => 2);
    }

    [DataTestMethod]
    [DataRow("start")]
    [DataRow("resume")]
    [DataRow("retry")]
    [DataRow("restart")]
    public async Task PersistedSourcesApplyTheSameLimitToEveryDynamicRunEntry(string entry)
    {
        _state.Fail = entry == "retry";
        var run = await Definitions.RunPlanAsync(Plan(Step("one", "one", wait: entry == "resume", fail: entry == "retry")),
            new Item(), [new("test", "scheduling-owner", "17")]);
        if (entry == "resume")
        {
            await Execute(run.Id);
            await Resume(run.Id);
        }
        else if (entry == "retry")
        {
            await Execute(run.Id);
            await using var scope = _sp.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().RequeueAsync(run.Id);
        }
        else if (entry == "restart")
        {
            await _sp.GetRequiredService<BTaskManager>().Clean($"workflow.run.{run.Id}");
            await using var scope = _sp.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>()
                .ReEnqueuePendingRunsAsync();
        }
        var queued = _sp.GetRequiredService<BTaskManager>().Tasks.Single(t => t.Id == $"workflow.run.{run.Id}");
        Assert.AreEqual("test-owner-pool", queued.Task.ConcurrencyGroup);
        Assert.AreEqual(2, queued.Task.GetConcurrencyLimit!());
        Assert.IsTrue(queued.Task.ConflictKeys!.SetEquals([$"workflow.run.{run.Id}"]));
        var stored = await Read(run.Id);
        var scheduling = _sp.GetServices<IWorkflowRunSourceSchedulingPolicy>().OfType<OwnerSchedulingPolicy>().Single();
        Assert.IsNotNull(scheduling.LastRequest.Snapshot);
        Assert.AreEqual("one", WorkflowRunPlans.Read(scheduling.LastRequest.Snapshot)!.Nodes.Single().NodeId);
        Assert.AreEqual(stored.CurrentStepIndex, scheduling.LastRequest.Cursor);
        Assert.IsTrue(stored.Sources.Any(s => s.Module == "test" && s.EntityKind == "scheduling-owner"));
    }
}
