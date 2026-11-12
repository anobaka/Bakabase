using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionHistoricalContinuationGuardTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Later_revision_cannot_make_superseded_original_run_runnable_again(bool currentPlanCleared)
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var owner = new AcquisitionTaskDbModel {Title = "Retained local continuation", Status = AcquisitionStatus.Waiting};
        db.AcquisitionTasks.Add(owner); await db.SaveChangesAsync();
        WorkflowRunDbModel Run() => new() {Status = WorkflowRunStatus.Waiting, WaitReason = "cloud.cloudAttention",
            CurrentItemJson = "retained checkpoint", Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = owner.Id.ToString()}]};
        var original = Run(); var intermediate = Run(); var current = Run();
        db.WorkflowRuns.AddRange(original, intermediate, current); await db.SaveChangesAsync();
        AcquisitionPlan Plan(int parent) => new() {Title = owner.Title, Parts = [new() {Id = "main",
            ContinueFrom = new(owner.Id, parent, "artifact", "operation", new string('a', 64))}]};
        owner.WorkflowRunId = current.Id;
        owner.PlanRevision = 3;
        owner.PlanJson = currentPlanCleared ? null : JsonSerializer.Serialize(Plan(intermediate.Id), JsonSerializerOptions.Web);
        owner.PlanHistoryJson = JsonSerializer.Serialize(new[] {
            new AcquisitionPlanRevision(2, Plan(original.Id), [], DateTime.UtcNow, "Continue verified download", original.Id),
            new AcquisitionPlanRevision(3, Plan(intermediate.Id), [], DateTime.UtcNow, "Continue retained local output", intermediate.Id)
        }, JsonSerializerOptions.Web);
        await db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(await db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync());
        var resumer = scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>();
        foreach (var historical in new[] {original, intermediate})
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => resumer.ResumeAsync(historical.Id, "{}"));
            StringAssert.StartsWith(error.Message, "ACQUISITION_PLAN_SUPERSEDED");
        }
        await new AcquisitionWorkflowRunMutationGuard(db).EnsureCanMutateAsync(current.Id);
        Assert.AreEqual(before, JsonSerializer.Serialize(await db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync()));
        Assert.AreEqual(0, await db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, await db.WorkflowNodeExecutions.CountAsync());
    }

    [TestMethod]
    public async Task Another_tasks_history_cannot_block_an_unrelated_run()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var unrelated = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting};
        db.WorkflowRuns.Add(unrelated); await db.SaveChangesAsync();
        var owner = new AcquisitionTaskDbModel {Title = "Different task", Status = AcquisitionStatus.Waiting};
        db.AcquisitionTasks.Add(owner); await db.SaveChangesAsync();
        var plan = new AcquisitionPlan {Parts = [new() {Id = "main", ContinueFrom =
            new(owner.Id, unrelated.Id, "artifact", "operation", new string('a', 64))}]};
        owner.PlanHistoryJson = JsonSerializer.Serialize(new[] {
            new AcquisitionPlanRevision(2, plan, [], DateTime.UtcNow, "An unrelated historical reference", unrelated.Id)
        }, JsonSerializerOptions.Web);
        await db.SaveChangesAsync();
        await new AcquisitionWorkflowRunMutationGuard(db).EnsureCanMutateAsync(unrelated.Id);
    }
}
