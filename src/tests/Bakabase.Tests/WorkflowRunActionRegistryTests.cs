using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunActionRegistryTests
{
    private sealed class Provider(string capabilityId, params WorkflowRunActionDescriptor[] actions)
        : IWorkflowRunActionProvider
    {
        public bool CanHandle(WorkflowRunActionContext context) =>
            context.Run.TriggerKind == capabilityId;

        public ValueTask<IReadOnlyList<WorkflowRunActionDescriptor>> DescribeAsync(
            WorkflowRunActionContext context, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkflowRunActionDescriptor>>(actions);
    }

    [TestMethod]
    public async Task CollectsCapabilityActionsByStableActionId()
    {
        var first = new WorkflowRunActionDescriptor("continue", "download", WorkflowRunActionMode.Continue,
            "Continue", NodeId: "download-node");
        var duplicate = first with { CapabilityId = "other" };
        var recompute = new WorkflowRunActionDescriptor("recompute", "ai", WorkflowRunActionMode.Recompute,
            "Re-analyze", NodeId: "ai-node", RequiresInput: true);
        var registry = new WorkflowRunActionRegistry([
            new Provider("acquisition", first, recompute),
            new Provider("acquisition", duplicate),
        ]);

        var actions = await registry.DescribeAsync(new WorkflowRunActionContext(
            new WorkflowRun { Id = 7, TriggerKind = "acquisition" }, CurrentNodeId: "download-node"));

        Assert.AreEqual(2, actions.Count);
        CollectionAssert.AreEqual(new[] { "continue", "recompute" }, actions.Select(x => x.Id).ToArray());
        Assert.AreEqual("ai-node", actions.Single(x => x.Id == "recompute").NodeId);
        Assert.IsTrue(actions.Single(x => x.Id == "recompute").RequiresInput);
    }

    [TestMethod]
    public async Task CancellationIsForwardedBeforeProviderDiscovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new Provider("acquisition");
        var registry = new WorkflowRunActionRegistry([provider]);

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            await registry.DescribeAsync(new WorkflowRunActionContext(
                new WorkflowRun { TriggerKind = "acquisition" }), cancellation.Token));
    }
}
