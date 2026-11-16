using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(true, false, 50L)]
    [DataRow(true, true, 1L)]
    [DataRow(true, null, 1L)]
    [DataRow(false, false, 50L)]
    [DataRow(false, true, 1L)]
    [DataRow(false, null, 50L)]
    public async Task Replanned_run_choice_overrides_global_reserve_before_its_first_write(
        bool globalEmergency, bool? selectedEmergency, long expectedGiB)
    {
        const long gib = 1024L * 1024 * 1024;
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        options.Value.MinimumFreeSpaceBytes = 50 * gib;
        options.Value.UseEmergencySpaceReserve = globalEmergency;
        var (task, previous, download) = await Failed();
        var processed = await ProcessedInput(previous.Id, download);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, previous.Id, processed.Id), default);
        var savedHistory = (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanHistoryJson;
        var policy = _services.GetRequiredService<IStorageWritePolicyProvider>();
        
        var newRuns = 0;
        var workflow = new ReserveRunHost(async (WorkflowRunPlan plan, object payload, IReadOnlyList<WorkflowSourceRef>? sources, CancellationToken ct) =>
            {
                var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Pending,
                    PlanSnapshotJson = WorkflowRunPlans.Serialize(plan),
                    Sources = (sources ?? []).Select(s => new WorkflowRunSourceDbModel {Module = s.Module,
                        EntityKind = s.EntityKind, EntityId = s.EntityId, Revision = s.Revision ?? "", ResultKey = s.ResultKey ?? ""}).ToList()};
                Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync(ct); newRuns++;
                Assert.AreEqual(50 * gib, await policy.GetMinimumFreeBytesAsync(new("workflow", run.Id.ToString()), ct),
                    "A scheduled run without its atomic task association must remain conservative.");
                return new WorkflowRun {Id = run.Id, Status = run.Status};
            });
        var plans = new AcquisitionPlanService(Db, [], _services.GetRequiredService<IAcquisitionService>(),
            workflow, _services.GetRequiredService<DownloadIntentService>(),
            _services.GetRequiredService<IUserStoragePolicy>(), artifacts: Artifacts);
        var started = await plans.RunAsync(task.Id, revised.Task.PlanRevision, default, selectedEmergency);
        Assert.IsNotNull(started.Task.WorkflowRunId); Assert.AreEqual(1, newRuns);
        var current = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        var grant = AcquisitionStorageOverrideGrant.Read(current);
        if (selectedEmergency == null) Assert.IsNull(grant);
        else
        {
            Assert.IsNotNull(grant); Assert.AreEqual(current.WorkflowRunId, grant.WorkflowRunId);
            Assert.AreEqual(revised.Task.PlanRevision, grant.PlanRevision);
            Assert.AreEqual(selectedEmergency.Value ? "emergency" : "configured", grant.ReserveMode);
        }
        var context = new StorageWriteContext("workflow", current.WorkflowRunId!.Value.ToString());
        Assert.AreEqual(expectedGiB * gib, await policy.GetMinimumFreeBytesAsync(context));
        var guard = new StorageWriteGuard(policy, _ => (_root, 64 * gib), TimeSpan.FromMilliseconds(500));
        if (expectedGiB == 50)
        {
            var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() =>
                guard.CheckAllocationAsync(_root, 31 * gib, context));
            Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        }
        else await guard.CheckAllocationAsync(_root, 31 * gib, context);
        Assert.AreEqual(globalEmergency, options.Value.UseEmergencySpaceReserve, "A run choice must not edit the global option.");
        Assert.AreEqual(savedHistory, current.PlanHistoryJson);
        Assert.IsTrue(File.Exists(download.Files.Single())); Assert.IsTrue(File.Exists(processed.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        if (selectedEmergency == false)
        {
            var tracked = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
            tracked.BuilderStateJson = AcquisitionStorageOverrideGrant.WithState(tracked.BuilderStateJson,
                grant! with {ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)});
            await Db.SaveChangesAsync(); AcquisitionStorageWritePolicyProvider.Invalidate();
            Assert.AreEqual(50 * gib, await policy.GetMinimumFreeBytesAsync(context),
                "Expiration must not lower a run's explicit configured reserve to the global emergency reserve.");
            tracked.BuilderStateJson = AcquisitionStorageOverrideGrant.WithState(tracked.BuilderStateJson, grant!);
            Db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = current.WorkflowRunId.Value,
                Status = "failed", CompletedAt = DateTime.UtcNow});
            await Db.SaveChangesAsync(); AcquisitionStorageWritePolicyProvider.Invalidate();
            Assert.AreEqual(50 * gib, await policy.GetMinimumFreeBytesAsync(context),
                "A failed step must not loosen its same-run configured reserve.");
        }
    }

    [TestMethod]
    public void Run_input_preserves_explicit_false_and_omission_as_distinct_choices()
    {
        var explicitFalse = JsonSerializer.Deserialize<AcquisitionPlanRunInput>(
            "{\"expectedRevision\":2,\"useEmergencySpaceReserve\":false}", WorkflowJson.Options)!;
        var omitted = JsonSerializer.Deserialize<AcquisitionPlanRunInput>("{\"expectedRevision\":2}", WorkflowJson.Options)!;
        Assert.AreEqual(false, explicitFalse.UseEmergencySpaceReserve); Assert.IsNull(omitted.UseEmergencySpaceReserve);
    }
    private sealed class ReserveRunHost(Func<WorkflowRunPlan, object, IReadOnlyList<WorkflowSourceRef>?, CancellationToken, Task<WorkflowRun>> start)
        : IWorkflowDefinitionService
    {
        public Task<WorkflowRun> RunPlanAsync(WorkflowRunPlan plan, object payload, IReadOnlyList<WorkflowSourceRef>? sources = null,
            CancellationToken ct = default) => start(plan, payload, sources, ct);
        public Task<WorkflowDefinition> CreateAsync(WorkflowDefinitionCreationInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition> UpdateAsync(int id, WorkflowDefinitionUpdateInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> GetAsync(int id) => throw new NotSupportedException();
        public Task<List<WorkflowDefinition>> SearchAsync(WorkflowDefinitionSearchInputModel input) => throw new NotSupportedException();
        public Task<SearchResponse<WorkflowRun>> SearchRunsAsync(WorkflowRunSearchInputModel input) => throw new NotSupportedException();
        public Task<WorkflowRun> RunManuallyAsync(int id, string? argsJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowRun> RunManagedAsync(int id, object payload, CancellationToken ct = default) => throw new NotSupportedException();
    }

}
