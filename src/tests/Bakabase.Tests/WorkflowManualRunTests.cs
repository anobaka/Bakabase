using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Starting a workflow run by hand: where the payload comes from, what is rejected before a run
/// row is ever written, and the gates a manual run deliberately ignores.
/// </summary>
[TestClass]
public sealed class WorkflowManualRunTests
{
    private IServiceProvider _sp = null!;
    private IWorkflowDefinitionService _workflows = null!;

    /// <summary>
    /// Stands in for a trigger whose inputs live on the definition rather than in an event —
    /// the shape <c>fs.manualScan</c> will take. It exercises the override half of the contract:
    /// no payload is asked of the user, and the args are ignored.
    /// </summary>
    private sealed class ConfiguredScanTrigger : IWorkflowTrigger
    {
        public const string TriggerKind = "test.configuredScan";

        public string Kind => TriggerKind;
        public string DisplayName => "Configured scan (test)";
        public Type PayloadType => typeof(DownloaderCompletedPayload);

        public bool Matches(object payload, string? triggerFilterJson) => true;
        public IReadOnlyList<object> ExtractItems(object payload) => [payload];
        public string ResolveOutputItemType(string? triggerFilterJson) => "item.downloader.completed";

        public bool RequiresManualPayload => false;

        public object BuildManualPayload(string? triggerFilterJson, string? argsJson) =>
            new DownloaderCompletedPayload {Key = triggerFilterJson ?? "no-config"};
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IWorkflowTrigger, ConfiguredScanTrigger>());
        _workflows = _sp.GetRequiredService<IWorkflowDefinitionService>();
    }

    private Task<Modules.Workflow.Abstractions.Models.Domain.WorkflowDefinition> AddDefinition(
        string triggerKind = "downloader.completed", bool enabled = true, string? filterJson = null) =>
        _workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = $"test-{Guid.NewGuid():N}",
            TriggerKind = triggerKind,
            TriggerFilterJson = filterJson,
            Enabled = enabled
        });

    private async Task<int> CountRuns(int definitionId) =>
        (await _workflows.SearchRunsAsync(new WorkflowRunSearchInputModel {WorkflowDefinitionId = definitionId}))
        .TotalCount;

    [TestMethod]
    public async Task RunManually_EventTrigger_PersistsTheSuppliedPayload()
    {
        var def = await AddDefinition();
        var argsJson = JsonSerializer.Serialize(new {taskId = 7, thirdPartyId = 3, key = "https://example/g/1"});

        var run = await _workflows.RunManuallyAsync(def.Id, argsJson);

        Assert.AreEqual(def.Id, run.WorkflowDefinitionId);
        var payload = JsonSerializer.Deserialize<DownloaderCompletedPayload>(run.PayloadJson!,
            WorkflowJson.Options);
        Assert.IsNotNull(payload);
        Assert.AreEqual(7, payload!.TaskId);
        Assert.AreEqual("https://example/g/1", payload.Key);
    }

    /// <summary>
    /// The whole point of running by hand is that it works while the definition is switched off —
    /// that is when a workflow is being built and most needs trying out.
    /// </summary>
    [TestMethod]
    public async Task RunManually_WorksOnADisabledDefinition()
    {
        var def = await AddDefinition(enabled: false);

        var run = await _workflows.RunManuallyAsync(def.Id, JsonSerializer.Serialize(new {taskId = 1}));

        Assert.AreEqual(def.Id, run.WorkflowDefinitionId);
    }

    [TestMethod]
    public async Task RunManually_MissingPayload_ThrowsAndWritesNoRun()
    {
        var def = await AddDefinition();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _workflows.RunManuallyAsync(def.Id, null));
        Assert.AreEqual(0, await CountRuns(def.Id));
    }

    [TestMethod]
    public async Task RunManually_MalformedPayload_ThrowsAndWritesNoRun()
    {
        var def = await AddDefinition();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _workflows.RunManuallyAsync(def.Id, "{ not json"));
        Assert.AreEqual(0, await CountRuns(def.Id));
    }

    [TestMethod]
    public async Task RunManually_UnknownDefinition_Throws()
        => await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _workflows.RunManuallyAsync(int.MaxValue, "{}"));

    [TestMethod]
    public async Task RunManually_TriggerBuildingItsOwnPayload_IgnoresArgs()
    {
        var def = await AddDefinition(ConfiguredScanTrigger.TriggerKind, filterJson: "roots-go-here");

        var run = await _workflows.RunManuallyAsync(def.Id, argsJson: null);

        var payload = JsonSerializer.Deserialize<DownloaderCompletedPayload>(run.PayloadJson!,
            WorkflowJson.Options);
        Assert.AreEqual("roots-go-here", payload!.Key);
    }

    /// <summary>
    /// The descriptor drives which panel the UI shows, so the two halves of the contract have to
    /// stay in step: a trigger that asks for nothing must also not reject an empty payload.
    /// </summary>
    [TestMethod]
    public void RequiresManualPayload_DefaultsToTrueAndIsOptedOutOf()
    {
        var triggers = _sp.GetRequiredService<IWorkflowTriggerRegistry>();

        Assert.IsTrue(triggers.Get("downloader.completed")!.RequiresManualPayload);
        Assert.IsFalse(triggers.Get(ConfiguredScanTrigger.TriggerKind)!.RequiresManualPayload);
        Assert.IsTrue(triggers.All.Where(t => !t.RequiresManualPayload)
            .All(t => t.BuildManualPayload(null, null) != null));
    }
}
