using Bakabase.Modules.Workflow.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class WorkflowRunPlanTests
{
    private const string TriggerKind = "test.frozenPlan";
    private const string ManagedTriggerKind = "test.frozenPlan.managed";
    private const string NodeKind = "test.frozenPlan.node";
    private const string ItemType = "test.frozenPlan.item";
    private IServiceProvider _sp = null!;
    private State _state = null!;
    private IWorkflowDefinitionService Definitions => _sp.GetRequiredService<IWorkflowDefinitionService>();

    private sealed class State
    {
        public List<string> Calls { get; } = [];
        public bool Fail { get; set; }
        public int ManualPayloadBuilds { get; set; }
        public List<int> Completed { get; } = [];
    }

    private sealed class CompletionObserver(BakabaseDbContext db, State state) : IWorkflowRunCompletionObserver
    {
        public async Task StageAsync(WorkflowRunDbModel run, object payload, CancellationToken ct)
        {
            Assert.AreEqual(WorkflowRunStatus.Success, run.Status);
            Assert.AreNotEqual(WorkflowRunStatus.Success, await db.WorkflowRuns.AsNoTracking()
                .Where(r => r.Id == run.Id).Select(r => r.Status).SingleAsync(ct),
                "Success observers must run before the final commit, not after it.");
            state.Completed.Add(run.Id);
        }
    }

    public sealed record Item : IWorkflowOutputSummary, IWorkflowSourcePayload
    {
        public string Value { get; init; } = "input";
        public IReadOnlyList<WorkflowSourceRef> SourceRefs => [new("test", "input", "42")];
        public object ToWorkflowOutputSummary() => new {Value};
    }

    private class Trigger(State state) : IWorkflowTrigger
    {
        public virtual string Kind => TriggerKind;
        public string DisplayName => "Frozen plan test";
        public virtual WorkflowActivationMode ActivationMode => WorkflowActivationMode.Manual;
        public virtual bool SupportsManualRun => true;
        public string SourceModule => "test";
        public Type PayloadType => typeof(Item);
        public bool Matches(object payload, string? filter) => true;
        public string ResolveOutputItemType(string? filter) => ItemType;
        public IReadOnlyList<object> ExtractItems(object payload) => [payload];
        public object BuildManualPayload(string? filter, string? args)
        {
            state.ManualPayloadBuilds++;
            return new Item();
        }
    }

    private sealed class ManagedTrigger(State state) : Trigger(state)
    {
        public override string Kind => ManagedTriggerKind;
        public override WorkflowActivationMode ActivationMode => WorkflowActivationMode.Module;
        public override bool SupportsManualRun => false;
    }

    private sealed class ManagedPolicy : IWorkflowRunSchedulingPolicy
    {
        public bool AppliesTo(string kind) => kind == ManagedTriggerKind;
        public bool ResumeOnStartup => false;
        public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) => builder.ConflictsWith("test.managed-plan");
    }

    private sealed class ItemDescriptor : IWorkflowItemTypeDescriptor
    {
        public string ItemType => WorkflowRunPlanTests.ItemType;
        public string DisplayName => "Plan input";
        public Type ClrType => typeof(Item);
    }

    private sealed record Config(string Value, bool Wait = false, bool Fail = false, string? Secret = null);

    private sealed class Node(State state) : IWorkflowActivity, IResumableWorkflowActivity, IWorkflowCheckpointedActivity
    {
        public string Kind => NodeKind;
        public string DisplayName => "Plan node";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";
        public IReadOnlyList<string> AcceptedInputItemTypes => [ItemType];
        public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context,
            CancellationToken ct)
        {
            var config = JsonSerializer.Deserialize<Config>(context.ConfigJson, WorkflowJson.Options)!;
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(config.Value == "invalid"
                ? [new() {Code = "invalidPlanTestValue", Message = "Invalid value."}] : []);
        }

        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
        {
            var config = ctx.GetConfig<Config>()!;
            state.Calls.Add("process:" + config.Value);
            if (config.Fail && state.Fail) throw new InvalidOperationException("Transient test failure.");
            return Task.FromResult(config.Wait
                ? WorkflowItemOutcome.Suspend(new WorkflowSuspension("test.wait", "{}", item))
                : WorkflowItemOutcome.ReplaceWith((Item)item with {Value = ((Item)item).Value + "/" + config.Value}));
        }

        public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson,
            CancellationToken ct)
        {
            var config = ctx.GetConfig<Config>()!;
            state.Calls.Add("resume:" + config.Value);
            return Task.FromResult(WorkflowItemOutcome.ReplaceWith((Item)item with {Value = ((Item)item).Value + "/" + config.Value}));
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _state = new State();
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton(_state);
            services.AddSingleton<IWorkflowTrigger, Trigger>();
            services.AddSingleton<IWorkflowTrigger, ManagedTrigger>();
            services.AddSingleton<IWorkflowRunSchedulingPolicy, ManagedPolicy>();
            services.AddSingleton<IWorkflowRunSourceSchedulingPolicy, OwnerSchedulingPolicy>();
            services.AddSingleton<IWorkflowActivity, Node>();
            services.AddSingleton<IWorkflowItemTypeDescriptor, ItemDescriptor>();
            services.AddScoped<IWorkflowRunCompletionObserver, CompletionObserver>();
        });
    }

    private static WorkflowRunPlanNode Step(string id, string value, bool wait = false, bool fail = false,
        string? secret = null) => new()
    {
        NodeId = id, Kind = NodeKind, Notes = "Safe note " + id,
        ConfigJson = JsonSerializer.Serialize(new Config(value, wait, fail, secret), WorkflowJson.Options)
    };

    private static WorkflowRunPlan Plan(params WorkflowRunPlanNode[] nodes) => new()
    {
        Name = "An execution request", TriggerKind = TriggerKind, Nodes = nodes
    };

    private static WorkflowActivityInputModel TemplateStep(string value, bool wait = false) => new()
    {
        Kind = NodeKind, ConfigJson = Step("unused", value, wait).ConfigJson
    };

    private Task<WorkflowDefinition> Template(params WorkflowActivityInputModel[] nodes) => Definitions.CreateAsync(new()
    {
        Name = "Reusable template", TriggerKind = TriggerKind, Enabled = true, Activities = nodes.ToList()
    });

    private async Task Execute(int id)
    {
        // The fixture drives the engine itself, without the hosted BTask scheduler.
        await _sp.GetRequiredService<BTaskManager>().Clean($"workflow.run.{id}");
        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        await runner.ExecuteAsync(id, new BTaskArgs(new PauseToken(), CancellationToken.None,
            new BTask("test", () => "test"), _ => Task.CompletedTask, _sp));
    }

    private async Task<WorkflowRunDbModel> Read(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>()
            .AsNoTracking().Include(r => r.NodeExecutions).Include(r => r.Sources).SingleAsync(r => r.Id == id);
    }

    private async Task Resume(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().ResumeAsync(id, "{}");
    }

    [TestMethod]
    public async Task CompletionObserversOnlySeeNewSuccessfulTransitions()
    {
        var waiting = await Definitions.RunPlanAsync(Plan(Step("wait", "wait", wait: true)), new Item());
        await Execute(waiting.Id);
        Assert.AreEqual(0, _state.Completed.Count);
        _state.Fail = true;
        var failed = await Definitions.RunPlanAsync(Plan(Step("fail", "fail", fail: true)), new Item());
        await Execute(failed.Id);
        Assert.AreEqual(0, _state.Completed.Count);
        var success = await Definitions.RunPlanAsync(Plan(Step("success", "ok")), new Item());
        await Execute(success.Id); await Execute(success.Id);
        CollectionAssert.AreEqual(new[] {success.Id}, _state.Completed);
        await Read(success.Id);
        CollectionAssert.AreEqual(new[] {success.Id}, _state.Completed, "Reading historical runs cannot re-publish completion.");
    }

    [TestMethod]
    public async Task DynamicPlanNeedsNoTemplateAndFreezesNodesWithoutExposingConfiguration()
    {
        var count = (await Definitions.SearchAsync(new())).Count;
        var nodes = new[] {Step("prepare", "first", secret: "private-config-canary"), Step("finish", "second")};
        var run = await Definitions.RunPlanAsync(Plan(nodes), new Item(), [new("test", "owner", "7")]);
        nodes[0] = Step("replacement", "changed");
        Assert.AreEqual(0, run.WorkflowDefinitionId);
        Assert.AreEqual(count, (await Definitions.SearchAsync(new())).Count);
        Assert.AreEqual(0, _state.ManualPayloadBuilds, "Prepared source input must not be rebuilt as manual input.");
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => Definitions.DeleteAsync(0));

        await Execute(run.Id);
        var done = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.Status, done.ErrorMessage);
        CollectionAssert.AreEqual(new[] {"process:first", "process:second"}, _state.Calls);
        CollectionAssert.AreEqual(new[] {"prepare", "finish"}, done.NodeExecutions.OrderBy(n => n.Id).Select(n => n.NodeId).ToArray());
        CollectionAssert.AreEquivalent(new[] {"input", "owner"}, done.Sources.Select(s => s.EntityKind).ToArray());
        var vm = WorkflowRunViewModel.From(done.ToDomainModel());
        Assert.IsTrue(vm.HasPlanSnapshot);
        Assert.AreEqual("An execution request", vm.PlanName);
        Assert.AreEqual(TriggerKind, vm.TriggerKind);
        CollectionAssert.AreEqual(new[] {0, 1}, vm.PlanNodes.Select(n => n.Order).ToArray());
        Assert.IsFalse(JsonSerializer.Serialize(vm).Contains("private-config-canary", StringComparison.Ordinal));
        StringAssert.Contains(done.PlanSnapshotJson!, "private-config-canary");
    }

    [TestMethod]
    public async Task WaitingTemplateRunResumesItsFrozenPlanAfterTemplateEditAndDeletion()
    {
        var definition = await Template(TemplateStep("before"), TemplateStep("wait", true), TemplateStep("after"));
        var run = await Definitions.RunManuallyAsync(definition.Id, null);
        await Execute(run.Id);
        var waiting = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, waiting.Status, waiting.ErrorMessage);
        var originalPlan = waiting.PlanSnapshotJson;
        var waitingNodeId = waiting.NodeExecutions.Single(n => n.Status == "waiting").NodeId;

        // A different, valid template is for future requests, even when it cannot execute now.
        await Definitions.UpdateAsync(definition.Id, new()
        {
            Name = "Changed template", Activities = [TemplateStep("invalid")]
        });
        await Definitions.DeleteAsync(definition.Id);
        Assert.IsNull(await Definitions.GetAsync(definition.Id));
        Assert.AreEqual(run.Id, (await Definitions.SearchRunsAsync(new() {WorkflowDefinitionId = definition.Id})).Data!.Single().Id);
        Assert.AreEqual("42", (await Read(run.Id)).Sources.Single().EntityId);
        await Resume(run.Id);
        await Execute(run.Id);
        var done = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.Status, done.ErrorMessage);
        Assert.AreEqual(originalPlan, done.PlanSnapshotJson);
        Assert.AreEqual("Reusable template", done.ToDomainModel().PlanName);
        CollectionAssert.AreEqual(new[] {"process:before", "process:wait", "resume:wait", "process:after"}, _state.Calls);
        CollectionAssert.AreEqual(new[] {1, 2}, done.NodeExecutions.Where(n => n.NodeId == waitingNodeId)
            .OrderBy(n => n.Id).Select(n => n.Attempt).ToArray());
    }

    [TestMethod]
    public async Task FailedDynamicPlanRequeuesFromItsCursorAndKeepsAttemptHistory()
    {
        _state.Fail = true;
        var run = await Definitions.RunPlanAsync(Plan(Step("first", "first"), Step("retry", "retry", fail: true),
            Step("last", "last")), new Item());
        await Execute(run.Id);
        var failed = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Failed, failed.Status);
        Assert.AreEqual(1, failed.CurrentStepIndex);
        _state.Fail = false;
        await using (var scope = _sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().RequeueAsync(run.Id);
        await Execute(run.Id);
        var done = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.Status, done.ErrorMessage);
        Assert.AreEqual(failed.PlanSnapshotJson, done.PlanSnapshotJson);
        CollectionAssert.AreEqual(new[] {"process:first", "process:retry", "process:retry", "process:last"}, _state.Calls);
        CollectionAssert.AreEqual(new[] {"failed", "succeeded"}, done.NodeExecutions.Where(n => n.NodeId == "retry")
            .OrderBy(n => n.Id).Select(n => n.Status).ToArray());
    }

    [TestMethod]
    public async Task CorruptSnapshotCannotResumeAgainstTheCurrentTemplate()
    {
        var definition = await Template(TemplateStep("wait", true));
        var run = await Definitions.RunManuallyAsync(definition.Id, null);
        await Execute(run.Id);
        await using (var scope = _sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>()
                .Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.PlanSnapshotJson, "{invalid"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Resume(run.Id));
        var stillWaiting = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, stillWaiting.Status);
        Assert.IsTrue(WorkflowRunViewModel.From(stillWaiting.ToDomainModel()).HasPlanSnapshot);
        Assert.AreEqual(1, _state.Calls.Count);
    }

    [TestMethod]
    public async Task EventRunFreezesItsPlanAndLegacyRunStillUsesItsDefinition()
    {
        var definition = await Template(TemplateStep("original"));
        await _sp.GetRequiredService<IWorkflowEventBus>().PublishAsync(TriggerKind, new Item());
        var eventRun = (await Definitions.SearchRunsAsync(new() {WorkflowDefinitionId = definition.Id})).Data!.Single();
        var legacy = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Pending, StartedAt = DateTime.Now,
            PayloadJson = JsonSerializer.Serialize(new Item(), WorkflowJson.Options)
        };
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            db.Add(legacy);
            await db.SaveChangesAsync();
        }
        // No active durable legacy cursor is present in this compatibility fixture.
        var changedConfig = Step("changed", "changed").ConfigJson;
        await using (var scope = _sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowActivityDbModel>()
                .Where(a => a.WorkflowDefinitionId == definition.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ConfigJson, changedConfig));
        await Execute(eventRun.Id);
        await Execute(legacy.Id);
        CollectionAssert.AreEqual(new[] {"process:original", "process:changed"}, _state.Calls);
        Assert.IsTrue((await Read(eventRun.Id)).ToDomainModel().HasPlanSnapshot);
        Assert.IsFalse((await Read(legacy.Id)).ToDomainModel().HasPlanSnapshot);
    }

    [TestMethod]
    public async Task DynamicCheckpointSurvivesRestartWithoutAnyDefinition()
    {
        var run = await Definitions.RunPlanAsync(Plan(Step("wait", "wait", wait: true)), new Item());
        await Execute(run.Id);
        await Resume(run.Id);
        await _sp.GetRequiredService<BTaskManager>().Clean($"workflow.run.{run.Id}");
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<WorkflowRunDbModel>().Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
            var rehydrator = scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>();
            await rehydrator.MarkInterruptedRunsAsync();
            await rehydrator.ReEnqueuePendingRunsAsync();
        }
        Assert.AreEqual(WorkflowRunStatus.Pending, (await Read(run.Id)).Status);
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Read(run.Id)).Status);
        CollectionAssert.AreEqual(new[] {"process:wait", "resume:wait"}, _state.Calls);
    }

    [TestMethod]
    public async Task DuplicateStableNodeIdsAndWrongPreparedPayloadAreRejectedBeforeRunCreation()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Definitions.RunPlanAsync(
            Plan(Step("same", "one"), Step("same", "two")), new Item()));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Definitions.RunPlanAsync(
            Plan(Step("one", "one")), new {Other = "untyped"}));
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new())).TotalCount);
        Assert.AreEqual(0, _state.Calls.Count);
    }

    [TestMethod]
    public void DynamicPlansDoNotShareTheDefinitionZeroSchedulingKey()
    {
        var resolver = new WorkflowRunSchedulingPolicyResolver([]);
        var first = resolver.Configure(BTaskBuilder.Create("first"), 0, TriggerKind, 101);
        var second = resolver.Configure(BTaskBuilder.Create("second"), 0, TriggerKind, 102);
        Assert.IsTrue(first.ConflictKeys!.SetEquals(["workflow.run.101"]));
        Assert.IsTrue(second.ConflictKeys!.SetEquals(["workflow.run.102"]));
    }

    [TestMethod]
    public async Task ManagedRunRetainsItsFrozenSourceRestartPolicyAfterTemplateChanges()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Managed request", TriggerKind = ManagedTriggerKind,
            Enabled = true, Activities = [TemplateStep("original")]
        });
        var run = await Definitions.RunManagedAsync(definition.Id, new Item());
        Assert.IsTrue(run.HasPlanSnapshot);
        Assert.AreEqual(0, _state.ManualPayloadBuilds);
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        await db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == definition.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.TriggerKind, TriggerKind));
        await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().MarkInterruptedRunsAsync();
        Assert.AreEqual(WorkflowRunStatus.Interrupted, (await Read(run.Id)).Status,
            "The original source requires an explicit restart even if today's template has another trigger.");
    }

    [TestMethod]
    public async Task InvalidPendingSnapshotFailsClosedWithoutExecutingOrReadingTheCurrentTemplate()
    {
        var definition = await Template(TemplateStep("unexpected"));
        var run = await Definitions.RunManuallyAsync(definition.Id, null);
        await using (var scope = _sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>()
                .Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.PlanSnapshotJson, "{invalid"));
        await Execute(run.Id);
        var failed = await Read(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Failed, failed.Status);
        Assert.IsNotNull(failed.CompletedAt);
        Assert.AreEqual(0, _state.Calls.Count);
    }
    [TestMethod]
    public async Task RequiredInvocationParameterCanBeSavedButMustResolveBeforePayloadPreparation()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Public inputs", TriggerKind = TriggerKind,
            Parameters = [new() { Key = "value", Required = true, DisplayInRun = true }],
            Activities = [TemplateStep("invalid") with { ParameterBindings = [new() { ParameterKey = "value", ConfigPath = "/value" }] }]
        });
        var validation = await _sp.GetRequiredService<IWorkflowValidationService>().ValidateAsync(definition);
        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
        Assert.IsTrue(validation.Diagnostics.Any(d => d.Code == "parameterRequired" && d.Severity == "warning"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Definitions.RunManuallyAsync(definition.Id, null));
        Assert.AreEqual(0, _state.ManualPayloadBuilds);
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new())).TotalCount);
        var run = await Definitions.RunManuallyAsync(definition.Id, null, new WorkflowInvocationInput
        {
            Overrides = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("supplied") }
        });
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Read(run.Id)).Status);
        CollectionAssert.AreEqual(new[] { "process:supplied" }, _state.Calls);
        Assert.AreEqual("supplied", run.ParameterSources.Single().DisplayValue);
    }

    [TestMethod]
    public async Task ParameterizedRunResumesFrozenValuesAfterDefaultsChange()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Frozen values", TriggerKind = TriggerKind,
            Parameters = [new() { Key = "value", DefaultValue = JsonSerializer.SerializeToElement("original") }],
            Activities = [TemplateStep("fixed", true) with { ParameterBindings = [new() { ParameterKey = "value", ConfigPath = "/value" }] }]
        });
        var run = await Definitions.RunManuallyAsync(definition.Id, null, new WorkflowInvocationInput
        {
            CallerDefaults = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("caller") }
        });
        await Execute(run.Id);
        var snapshot = (await Read(run.Id)).PlanSnapshotJson;
        await Definitions.UpdateAsync(definition.Id, new()
        {
            Parameters = [new() { Key = "value", DefaultValue = JsonSerializer.SerializeToElement("changed") }]
        });
        await Resume(run.Id);
        await Execute(run.Id);
        Assert.AreEqual(snapshot, (await Read(run.Id)).PlanSnapshotJson);
        CollectionAssert.AreEqual(new[] { "process:caller", "resume:caller" }, _state.Calls);
    }

    [TestMethod]
    public async Task EventAndManagedEntryPointsUseTheSamePublicParameterContract()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Event parameters", TriggerKind = TriggerKind, Enabled = true,
            Parameters = [new() { Key = "value", DefaultValue = JsonSerializer.SerializeToElement("event-default") }],
            Activities = [TemplateStep("fixed") with { ParameterBindings = [new() { ParameterKey = "value", ConfigPath = "/value" }] }]
        });
        await _sp.GetRequiredService<IWorkflowEventBus>().PublishAsync(TriggerKind, new Item());
        var eventRun = (await Definitions.SearchRunsAsync(new() { WorkflowDefinitionId = definition.Id })).Data!.Single();
        await Execute(eventRun.Id);
        var managed = await Definitions.CreateAsync(new()
        {
            Name = "Managed parameters", TriggerKind = ManagedTriggerKind,
            Parameters = [new() { Key = "value", Required = true }],
            Activities = [TemplateStep("fixed") with { ParameterBindings = [new() { ParameterKey = "value", ConfigPath = "/value" }] }]
        });
        var managedRun = await Definitions.RunManagedAsync(managed.Id, new Item(), new WorkflowInvocationInput
        {
            CallerDefaults = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("managed") }
        });
        await Execute(managedRun.Id);
        CollectionAssert.AreEqual(new[] { "process:event-default", "process:managed" }, _state.Calls);
    }

}
