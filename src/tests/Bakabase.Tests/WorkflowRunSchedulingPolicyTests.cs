using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunSchedulingPolicyTests
{
    private sealed class SourcePolicy : IWorkflowRunSchedulingPolicy
    {
        public int Limit = 2;
        public bool AppliesTo(string triggerKind) => triggerKind == "test.manual";
        public bool ResumeOnStartup => false;
        public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) =>
            builder.WithConcurrencyLimit("test", () => Limit);
    }

    [TestMethod]
    public void OrdinaryWorkflowsRetainDefinitionSerializationAndAutomaticRecovery()
    {
        var resolver = new WorkflowRunSchedulingPolicyResolver([new SourcePolicy()]);
        var builder = resolver.Configure(BTaskBuilder.Create("run"), 12, "other.trigger");
        Assert.IsTrue(builder.ConflictKeys!.SetEquals(["workflow.definition.12"]));
        Assert.IsNull(builder.ConcurrencyGroup);
        Assert.IsTrue(resolver.ResumeOnStartup("other.trigger"));
    }

    [TestMethod]
    public void SourcePolicySharesCapacityAcrossDefinitionsAndReadsTheCurrentLimit()
    {
        var policy = new SourcePolicy();
        var resolver = new WorkflowRunSchedulingPolicyResolver([policy]);
        var first = resolver.Configure(BTaskBuilder.Create("first"), 1, "test.manual");
        var second = resolver.Configure(BTaskBuilder.Create("second"), 2, "test.manual");
        Assert.AreEqual(first.ConcurrencyGroup, second.ConcurrencyGroup);
        Assert.IsNull(first.ConflictKeys);
        Assert.IsFalse(resolver.ResumeOnStartup("test.manual"));
        Assert.AreEqual(2, first.GetConcurrencyLimit!());
        policy.Limit = 4;
        Assert.AreEqual(4, first.GetConcurrencyLimit!());
        Assert.AreEqual(4, second.GetConcurrencyLimit!());
    }
}
