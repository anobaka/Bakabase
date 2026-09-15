using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowManagedRunTests
{
    private IServiceProvider _sp = null!;
    private IWorkflowDefinitionService Definitions => _sp.GetRequiredService<IWorkflowDefinitionService>();
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private WorkflowController Controller => ActivatorUtilities.CreateInstance<WorkflowController>(_sp);

    [TestInitialize]
    public async Task Setup() => _sp = await TestServiceBuilder.BuildServiceProvider();

    [TestMethod]
    [DataRow(AcquisitionWorkflowKinds.TriggerRequested, "Acquire resources")]
    [DataRow(DownloadResultWorkflow.Trigger, "downloader result")]
    public async Task ManualEndpointRejectsManagedTriggersBeforeParsingInputOrWritingRun(
        string triggerKind, string sourceHint)
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Arbitrary user workflow name", TriggerKind = triggerKind
        });
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Controller.RunManually(
            definition.Id, new WorkflowManualRunInputModel {ArgsJson = "not even valid json"}, CancellationToken.None));
        StringAssert.Contains(error.Message, "source module");
        StringAssert.Contains(error.Message, sourceHint);
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    public void TriggerDescriptorsExposeManagedBoundaryWithoutOfferingPayloadFields()
    {
        var descriptors = Controller.GetTriggers().Data!.ToDictionary(t => t.Kind);
        foreach (var kind in new[] {AcquisitionWorkflowKinds.TriggerRequested, DownloadResultWorkflow.Trigger})
        {
            Assert.IsFalse(descriptors[kind].SupportsManualRun);
            Assert.AreEqual(0, descriptors[kind].PayloadFields.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(descriptors[kind].Description));
        }
        Assert.IsTrue(descriptors["postParser.manual"].SupportsManualRun);
        Assert.IsTrue(descriptors["downloader.completed"].SupportsManualRun);
    }

    [TestMethod]
    public async Task AcquisitionServiceStillStartsOwnedRunWithItsPreparedInput()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Acquisition from its source module", TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new WorkflowActivityInputModel {Kind = AcquisitionStepKinds.SelectLink}]
        });
        var resource = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Managed input");
        var task = await _sp.GetRequiredService<IAcquisitionService>().CreateAsync(resource.ResourceId,
            AcquisitionLeadKind.DirectUrl, "https://example.com/file.zip", recipeDefinitionId: definition.Id);
        Assert.IsNotNull(task.WorkflowRunId);
        var run = await Db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync();
        var payload = JsonSerializer.Deserialize<AcquisitionRequestedPayload>(run.PayloadJson!, WorkflowJson.Options)!;
        Assert.AreEqual(task.Id, payload.TaskId);
        Assert.AreEqual(resource.ResourceId, payload.ResourceId);
        Assert.AreEqual("https://example.com/file.zip", payload.LeadValue);
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload.WorkingDirectory));
        Assert.AreEqual(definition.Id, run.WorkflowDefinitionId);
    }

    [TestMethod]
    public async Task ManagedEntryRejectsWrongPayloadTypeAndStillValidatesActualInput()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Managed HTTP validation", TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new WorkflowActivityInputModel {Kind = AcquisitionStepKinds.FetchHttp}]
        });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Definitions.RunManagedAsync(definition.Id, new {resourceId = 12}));
        var error = await Assert.ThrowsExactlyAsync<WorkflowValidationException>(() =>
            Definitions.RunManagedAsync(definition.Id, new AcquisitionRequestedPayload
            {
                ResourceId = 12, LeadKind = AcquisitionLeadKind.DirectUrl, LeadValue = "not-http"
            }));
        StringAssert.Contains(error.Message, "HTTP");
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task TrustedEntryDoesNotBypassManualTriggerPayloadPreparation()
    {
        var definition = await Definitions.CreateAsync(new()
        {
            Name = "Manual scan", TriggerKind = "fs.manualScan"
        });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Definitions.RunManagedAsync(definition.Id, new object()));
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }
}
