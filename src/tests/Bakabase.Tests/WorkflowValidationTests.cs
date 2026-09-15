using System;
using System.Collections.Generic;
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
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowValidationTests
{
    private const string TriggerKind = "test.validation.trigger";
    private const string NodeKind = "test.validation.node";
    private IServiceProvider _sp = null!;
    private CheckState _state = null!;
    private IWorkflowDefinitionService Definitions => _sp.GetRequiredService<IWorkflowDefinitionService>();
    private IWorkflowValidationService Validation => _sp.GetRequiredService<IWorkflowValidationService>();

    private sealed class CheckState
    {
        public bool Ready { get; set; }
        public int Validations { get; set; }
        public int Executions { get; set; }
        public int PayloadBuilds { get; set; }
        public int Extractions { get; set; }
        public List<Guid> ScopeIds { get; } = [];
    }

    private sealed class ScopeProbe { public Guid Id { get; } = Guid.NewGuid(); }
    private sealed record Payload { public string? Value { get; init; } }

    private sealed class Trigger(CheckState state) : IWorkflowTrigger
    {
        public string Kind => TriggerKind;
        public string DisplayName => "Validation trigger";
        public Type PayloadType => typeof(Payload);
        public bool Matches(object payload, string? filter) => filter switch
        {
            "throw" => throw new InvalidOperationException("The event filter could not be evaluated."),
            "no-match" => false,
            _ => true,
        };
        public string ResolveOutputItemType(string? filter) => filter == "other" ? "test.other" : "test.validation.item";
        public IReadOnlyList<object> ExtractItems(object payload)
        {
            state.Extractions++;
            return [payload];
        }
        public object BuildManualPayload(string? filter, string? args)
        {
            state.PayloadBuilds++;
            return new Payload { Value = args };
        }
    }

    private sealed class CheckedNode(CheckState state) : IWorkflowActivity
    {
        public string Kind => NodeKind;
        public string DisplayName => "Checked node";
        public string Description => "Check local readiness before processing an item.";
        public string DescriptionKey => "workflow.test.checkedNode";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";
        public IReadOnlyList<string> AcceptedInputItemTypes => ["test.validation.item"];

        public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
        {
            state.Validations++;
            state.ScopeIds.Add(context.Services.GetRequiredService<ScopeProbe>().Id);
            using var config = JsonDocument.Parse(context.ConfigJson);
            var mode = config.RootElement.TryGetProperty("mode", out var value) ? value.GetString() : null;
            if (mode == "throws") throw new InvalidOperationException("Local configuration is unavailable");
            if (mode == "payload" && context.IsExecution && context.Payload is Payload { Value: null })
                return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([
                    new() { Code = "missingPayloadValue", Message = "Choose an item before starting." },
                ]);
            if (mode == "warning")
                return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([
                    new() { Code = "optionalSetting", Message = "An optional setting is unset.", Severity = "warning" },
                ]);
            if (mode == "cached")
                return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(
                    context.Payload is Payload {Value: "cached"} ? [] :
                    [new() {Code = "providerMissing", Message = "This input needs an external provider.", DependsOnPayload = true}]);
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(state.Ready ? [] : [
                new() { Code = "localSettingMissing", Message = "Configure the local destination.", MessageKey = "workflow.test.localSettingMissing" },
            ]);
        }

        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext context, object item, CancellationToken ct)
        {
            state.Executions++;
            return Task.FromResult(WorkflowItemOutcome.KeepItem);
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _state = new CheckState();
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton(_state);
            services.AddScoped<ScopeProbe>();
            services.AddSingleton<IWorkflowTrigger, Trigger>();
            services.AddSingleton<IWorkflowActivity, CheckedNode>();
        });
    }

    private static WorkflowActivityInputModel Node(string config = "{}") =>
        new() { NodeId = "draft-node", Kind = NodeKind, ConfigJson = config, Notes = "Keep my note unchanged." };

    private Task<WorkflowDefinition> Create(string config = "{}") => Definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
    {
        Name = "My workflow", Description = "My detailed description", DescriptionKey = "workflow.test.builtinDescription",
        TriggerKind = TriggerKind, Activities = [Node(config)],
    });

    [TestMethod]
    public async Task DraftChecks_AggregateTypeAndConfigurationProblems_WithEditorIdentity_WithoutPersisting()
    {
        var result = await Validation.ValidateAsync(new WorkflowValidationInputModel
        {
            TriggerKind = TriggerKind, TriggerFilterJson = "other", Activities = [Node()],
        });
        Assert.IsFalse(result.IsValid);
        CollectionAssert.AreEquivalent(new[] { "typeMismatch", "localSettingMissing" }, result.Diagnostics.Select(d => d.Code).ToArray());
        Assert.IsTrue(result.Diagnostics.All(d => d.NodeId == "draft-node" && d.NodeIndex == 0 && d.Kind == NodeKind));
        Assert.AreEqual("workflow.validation.typeMismatch", result.Diagnostics.Single(d => d.Code == "typeMismatch").MessageKey);
        Assert.AreEqual("workflow.test.localSettingMissing", result.Diagnostics.Single(d => d.Code == "localSettingMissing").MessageKey);
        Assert.AreEqual(0, _state.Executions);
        Assert.AreEqual(0, _state.PayloadBuilds);
        Assert.AreEqual(0, await _sp.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task DescriptionAndNotes_RoundTrip_ClearAndPreserveUnrelatedUpdates_AndExposeDescriptors()
    {
        var definition = await Create(); // Incomplete environment can still be saved as a draft.
        var controller = ActivatorUtilities.CreateInstance<WorkflowController>(_sp);
        var view = (await controller.Get(definition.Id)).Data!;
        Assert.AreEqual("My detailed description", view.Description);
        Assert.AreEqual("Keep my note unchanged.", view.Activities.Single().Notes);
        var descriptor = controller.GetActivities().Data!.Single(d => d.Kind == NodeKind);
        Assert.AreEqual("workflow.test.checkedNode", descriptor.DescriptionKey);
        Assert.AreEqual("Check local readiness before processing an item.", descriptor.Description);
        await Definitions.UpdateAsync(definition.Id, new WorkflowDefinitionUpdateInputModel { Name = "Renamed" });
        Assert.AreEqual("My detailed description", (await Definitions.GetAsync(definition.Id))!.Description);
        await Definitions.UpdateAsync(definition.Id, new WorkflowDefinitionUpdateInputModel
        {
            Description = "", Activities = [Node() with { Notes = "" }],
        });
        var cleared = (await Definitions.GetAsync(definition.Id))!;
        Assert.AreEqual("", cleared.Description);
        Assert.IsNull(cleared.DescriptionKey);
        Assert.AreEqual("", cleared.Activities.Single().Notes);
        var savedCheck = (await controller.ValidateSaved(definition.Id, default)).Data!;
        Assert.AreEqual(cleared.Activities.Single().Id.ToString(), savedCheck.Diagnostics.Single().NodeId);
    }

    [TestMethod]
    public async Task BuiltinDescriptionAndNotes_CannotBeEdited()
    {
        var definition = await Create();
        await _sp.GetRequiredService<BakabaseDbContext>().Set<WorkflowDefinitionDbModel>()
            .Where(d => d.Id == definition.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true));
        await using var scope = _sp.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.UpdateAsync(definition.Id,
            new WorkflowDefinitionUpdateInputModel { Description = "Overwritten" }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.UpdateAsync(definition.Id,
            new WorkflowDefinitionUpdateInputModel { Activities = [Node() with { Notes = "Overwritten" }] }));
        Assert.AreEqual("My detailed description", (await service.GetAsync(definition.Id))!.Description);
    }

    [TestMethod]
    public async Task ManualRun_InvalidConfiguration_WritesNoRunAndDoesNotBuildOrExtractPayload()
    {
        var definition = await Create();
        var error = await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() => Definitions.RunManuallyAsync(definition.Id, "item"));
        Assert.AreEqual("localSettingMissing", error.Result.Diagnostics.Single().Code);
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new() { WorkflowDefinitionId = definition.Id })).TotalCount);
        Assert.AreEqual(0, _state.PayloadBuilds);
        Assert.AreEqual(0, _state.Extractions);
        Assert.AreEqual(0, _state.Executions);
    }

    [TestMethod]
    public async Task ManualRun_PayloadDependentError_IsCheckedBeforeRunCreation()
    {
        _state.Ready = true;
        var definition = await Create("{\"mode\":\"payload\"}");
        Assert.IsTrue((await Validation.ValidateAsync(definition)).IsValid, "Editor lacks an execution payload and must not invent a missing-input error.");
        var error = await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() => Definitions.RunManuallyAsync(definition.Id, null));
        Assert.AreEqual("missingPayloadValue", error.Result.Diagnostics.Single().Code);
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new() { WorkflowDefinitionId = definition.Id })).TotalCount);
        Assert.AreEqual(0, _state.Executions);
    }

    [TestMethod]
    public async Task ManualRun_InputDependentReadinessUsesActualPayloadAndStillRejectsUnreadyInputs()
    {
        var definition = await Create("{\"mode\":\"cached\"}");
        var draftCheck = await Validation.ValidateAsync(definition);
        Assert.IsFalse(draftCheck.IsValid);
        Assert.IsTrue(draftCheck.Diagnostics.Single().DependsOnPayload);

        var rejected = await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() =>
            Definitions.RunManuallyAsync(definition.Id, "fresh"));
        Assert.AreEqual("providerMissing", rejected.Result.Diagnostics.Single().Code);
        Assert.AreEqual(1, _state.PayloadBuilds);
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new() {WorkflowDefinitionId = definition.Id})).TotalCount);

        var run = await Definitions.RunManuallyAsync(definition.Id, "cached");
        Assert.AreEqual(WorkflowRunStatus.Pending, run.Status);
        Assert.AreEqual(2, _state.PayloadBuilds);
        Assert.AreEqual(0, _state.Extractions);
        Assert.AreEqual(0, _state.Executions);
    }

    [TestMethod]
    public async Task ManualRun_DeferredRequirementDoesNotHideUnconditionalConfigurationErrors()
    {
        var definition = await Definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = "Mixed validation", TriggerKind = TriggerKind,
            Activities = [Node("{\"mode\":\"cached\"}"), Node()]
        });
        var error = await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() =>
            Definitions.RunManuallyAsync(definition.Id, "cached"));
        Assert.AreEqual("localSettingMissing", error.Result.Diagnostics.Single().Code);
        Assert.AreEqual(0, _state.PayloadBuilds);
        Assert.AreEqual(0, (await Definitions.SearchRunsAsync(new() {WorkflowDefinitionId = definition.Id})).TotalCount);
        Assert.AreEqual(0, _state.Extractions);
    }

    [TestMethod]
    public async Task EventTrigger_InvalidConfiguration_RecordsAVisibleFailedStartWithoutExecuting()
    {
        var definition = await Create();
        await _sp.GetRequiredService<IWorkflowEventBus>().PublishAsync(TriggerKind, new Payload { Value = "item" });
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.WorkflowDefinitionId == definition.Id);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        Assert.IsNotNull(run.CompletedAt);
        Assert.IsNull(run.CurrentStepIndex);
        StringAssert.Contains(run.ErrorMessage, "Configure the local destination");
        StringAssert.Contains(run.PayloadJson, "item");
        var saved = await Definitions.GetAsync(definition.Id);
        Assert.AreEqual(run.ErrorMessage, saved!.LastError);
        Assert.AreEqual(run.CompletedAt, saved.LastRunAt);
        Assert.AreEqual(0, _state.Executions);
        Assert.AreEqual(0, _state.Extractions);
    }

    [TestMethod]
    public async Task EventTrigger_FilterFailureIsRecordedWhileOtherSubscribersContinueAndNonmatchesStaySilent()
    {
        _state.Ready = true;
        async Task<WorkflowDefinition> Subscriber(string filter, bool enabled = true) =>
            await Definitions.CreateAsync(new()
            {
                Name = filter, TriggerKind = TriggerKind, TriggerFilterJson = filter,
                Enabled = enabled, Activities = [Node()],
            });
        var broken = await Subscriber("throw");
        var ignored = await Subscriber("no-match");
        var disabled = await Subscriber("throw", false);
        var ready = await Subscriber("match");

        await _sp.GetRequiredService<IWorkflowEventBus>().PublishAsync(TriggerKind, new Payload {Value = "item"});

        var runs = await _sp.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().AsNoTracking().ToListAsync();
        Assert.AreEqual(2, runs.Count);
        var failed = runs.Single(r => r.WorkflowDefinitionId == broken.Id);
        Assert.AreEqual(WorkflowRunStatus.Failed, failed.Status);
        StringAssert.Contains(failed.ErrorMessage, "event filter could not be evaluated");
        Assert.IsNotNull(failed.CompletedAt);
        Assert.IsTrue(runs.Any(r => r.WorkflowDefinitionId == ready.Id));
        Assert.IsFalse(runs.Any(r => r.WorkflowDefinitionId == ignored.Id || r.WorkflowDefinitionId == disabled.Id));
        Assert.IsNull((await Definitions.GetAsync(ignored.Id))!.LastError);
    }

    [TestMethod]
    public async Task EventTrigger_MalformedPersistedFilterIsReportedInsteadOfLookingLikeANormalNonmatch()
    {
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var definition = new WorkflowDefinitionDbModel
        {
            Name = "Legacy damaged filter", TriggerKind = "collection.membersAdded",
            Enabled = true, TriggerFilterJson = "[invalid json", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
        };
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        await _sp.GetRequiredService<IWorkflowEventBus>().PublishAsync("collection.membersAdded",
            new Bakabase.Modules.Collection.Components.Workflow.CollectionMembersAddedPayload
            {
                CollectionId = 7, ResourceIds = [12],
            });
        var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        StringAssert.Contains(run.ErrorMessage, "trigger filter");
        Assert.AreEqual(definition.Id, run.WorkflowDefinitionId);
    }

    [TestMethod]
    public async Task QueuedRun_RechecksChangedEnvironmentBeforeExtractingOrExecutingItems()
    {
        var definition = await Create();
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var queued = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Pending,
            StartedAt = DateTime.Now, PayloadJson = JsonSerializer.Serialize(new Payload { Value = "item" }),
        };
        db.Set<WorkflowRunDbModel>().Add(queued);
        await db.SaveChangesAsync();
        await _sp.GetRequiredService<WorkflowRunner<BakabaseDbContext>>().ExecuteAsync(queued.Id,
            new BTaskArgs(new PauseToken(), CancellationToken.None, new BTask("test", () => "test"), _ => Task.CompletedTask, _sp));
        await db.Entry(queued).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Failed, queued.Status);
        StringAssert.Contains(queued.ErrorMessage, "Configure the local destination");
        Assert.AreEqual(0, _state.Extractions);
        Assert.AreEqual(0, _state.Executions);
    }

    [TestMethod]
    public async Task Warning_DoesNotBlockStartingAWorkflow()
    {
        var definition = await Create("{\"mode\":\"warning\"}");
        var result = await Validation.ValidateAsync(definition);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("warning", result.Diagnostics.Single().Severity);
        var run = await Definitions.RunManuallyAsync(definition.Id, "item");
        Assert.IsTrue(run.Id > 0);
    }

    [TestMethod]
    public async Task MalformedConfigAndFailingValidator_AreStructuredErrors()
    {
        var invalidJson = await Validation.ValidateAsync(new WorkflowValidationInputModel
        {
            TriggerKind = TriggerKind, Activities = [Node("{")],
        });
        Assert.AreEqual("invalidJson", invalidJson.Diagnostics.Single().Code);
        Assert.AreEqual(0, _state.Validations);
        var failed = await Validation.ValidateAsync(new WorkflowValidationInputModel
        {
            TriggerKind = TriggerKind, Activities = [Node("{\"mode\":\"throws\"}")],
        });
        Assert.IsFalse(failed.IsValid);
        Assert.AreEqual("validationFailed", failed.Diagnostics.Single().Code);
        Assert.AreEqual(0, _state.Executions);
    }

    [TestMethod]
    public async Task SingletonNode_ReceivesTheCurrentScopedServicesForEachCheck()
    {
        var definition = await Create();
        for (var i = 0; i < 2; i++)
        {
            await using var scope = _sp.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWorkflowValidationService>().ValidateAsync(definition);
        }
        Assert.AreEqual(2, _state.ScopeIds.Distinct().Count());
    }

    [TestMethod]
    public async Task InvalidFilterOnlyUpdate_DoesNotMutateDescriptionOrDisableTheWorkflow()
    {
        var definition = await Create();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Definitions.UpdateAsync(definition.Id,
            new WorkflowDefinitionUpdateInputModel { TriggerFilterJson = "other", Description = "Should not persist", Enabled = false }));
        await _sp.GetRequiredService<BakabaseDbContext>().SaveChangesAsync();
        var unchanged = (await Definitions.GetAsync(definition.Id))!;
        Assert.AreEqual("My detailed description", unchanged.Description);
        Assert.IsTrue(unchanged.Enabled);
        Assert.IsNull(unchanged.TriggerFilterJson);
    }

    [TestMethod]
    public async Task ResumeAndRetry_InvalidConfiguration_PreserveExistingRunState()
    {
        var definition = await Create();
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var resumer = _sp.GetRequiredService<IWorkflowRunResumer>();
        foreach (var status in new[] { WorkflowRunStatus.Waiting, WorkflowRunStatus.Failed })
        {
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = definition.Id, Status = status, CurrentStepIndex = 0,
                StartedAt = DateTime.Now, PayloadJson = JsonSerializer.Serialize(new Payload { Value = "item" }),
                ErrorMessage = "Original failure",
            };
            db.Set<WorkflowRunDbModel>().Add(run);
            await db.SaveChangesAsync();
            await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() => status == WorkflowRunStatus.Waiting
                ? resumer.ResumeAsync(run.Id, "new signal") : resumer.RequeueAsync(run.Id));
            await db.SaveChangesAsync();
            await db.Entry(run).ReloadAsync();
            Assert.AreEqual(status, run.Status);
            Assert.AreEqual("Original failure", run.ErrorMessage);
            Assert.IsNull(run.PendingSignalJson);
        }
    }

    [TestMethod]
    public async Task ResumedChecks_IgnoreCompletedNodesReadiness_WithoutSkippingTypeValidation()
    {
        var input = new WorkflowValidationInputModel
        {
            TriggerKind = TriggerKind, Activities = [Node(), Node("{\"mode\":\"warning\"}")],
        };
        Assert.IsFalse((await Validation.ValidateAsync(input)).IsValid);
        var remaining = await Validation.ValidateAsync(input, isExecution: true, startNodeIndex: 1);
        Assert.IsTrue(remaining.IsValid);
        Assert.AreEqual("optionalSetting", remaining.Diagnostics.Single().Code);
        Assert.AreEqual(1, remaining.Diagnostics.Single().NodeIndex);
        input.TriggerFilterJson = "other";
        var brokenChain = await Validation.ValidateAsync(input, isExecution: true, startNodeIndex: 1);
        Assert.IsFalse(brokenChain.IsValid);
        Assert.IsTrue(brokenChain.Diagnostics.Any(d => d.NodeIndex == 0 && d.Code == "typeMismatch"));
    }
}
