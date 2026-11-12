using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactWorkflowDefinitionGuardTests
{
    [TestMethod]
    public async Task Local_artifact_chain_cannot_change_while_waiting_but_can_change_after_success()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        try
        {
            await using var scope = services.CreateAsyncScope();
            var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var activity = scope.ServiceProvider.GetRequiredService<IWorkflowActivityRegistry>().Get("fs.processArtifacts");
            Assert.IsInstanceOfType<IWorkflowCheckpointedActivity>(activity);
            var definition = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
            {
                Name = "Local artifact checkpoint guard", TriggerKind = FileProcessingPlanTrigger.TriggerKind,
                Enabled = false, Activities = [new() {Kind = "fs.processArtifacts", ConfigJson = "{}"}]
            });
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Waiting,
                CurrentStepIndex = 0, WaitReason = "artifacts.fileProcessing", StartedAt = DateTime.UtcNow
            };
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            const string revisedConfig = "{\"recursive\":{\"enabled\":true,\"maxDepth\":4}}";
            var revision = new WorkflowDefinitionUpdateInputModel
            {
                Activities = [new() {Kind = "fs.processArtifacts", ConfigJson = revisedConfig}]
            };

            var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => definitions.UpdateAsync(definition.Id, revision));
            StringAssert.Contains(error.Message, "durable operations");
            Assert.AreEqual("{}", (await definitions.GetAsync(definition.Id))!.Activities.Single().ConfigJson);

            await db.WorkflowRuns.Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
            var updated = await definitions.UpdateAsync(definition.Id, revision);
            Assert.AreEqual(revisedConfig, updated.Activities.Single().ConfigJson);
            Assert.AreEqual(WorkflowRunStatus.Success,
                (await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status);
        }
        finally
        {
            if (services is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (services is IDisposable disposable) disposable.Dispose();
        }
    }
}
