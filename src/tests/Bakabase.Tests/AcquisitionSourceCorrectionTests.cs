using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, DownloadWorkset Workset)>
        SeedAcquisitionCorrectionAsync(bool withPriorFailure = true)
    {
        var source = Source("acquisition-correction", "baidu") with {AccessCode = "wrong", ArchivePassword = "archive-kept"};
        var target = Chosen(source, "account-a") with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var workset = Workset(source) with {Targets = [target]};
        if (withPriorFailure)
        {
            _baidu.TransferError = new CloudProviderException(target.Problem);
            await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target,
                "account-a", "/Bakabase", default));
            _baidu.TransferError = null;
        }
        var run = await Db.WorkflowRuns.SingleAsync(r => r.Id == 7201);
        run.Status = WorkflowRunStatus.Waiting;
        run.CurrentStepIndex = 0;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.cloudAttention";
        run.WaitPromptJson = JsonSerializer.Serialize(workset, WorkflowJson.Options);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Correction fixture", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "transfer", Kind = "baidu.transfer", ConfigJson = "{}"},
                new() {NodeId = "download", Kind = "download.enqueueSupported", ConfigJson = "{}"}]});
        var plan = new AcquisitionPlan {Title = "Correction fixture", OutputDirectory = workset.OutputDirectory,
            Parts = [new() {Id = target.Id, SelectedSourceId = source.Id,
                Sources = [new() {Id = source.Id, Url = source.Url, AccessCode = source.AccessCode, ArchivePassword = source.ArchivePassword}]}]};
        var task = new AcquisitionTaskDbModel {Title = plan.Title, BuilderId = "prepared", PlanRevision = 1,
            Status = AcquisitionStatus.Waiting, Phase = "attention", WorkflowRunId = run.Id,
            PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options), PlanHistoryJson = "[]",
            PurchaseRecordJson = "[{\"retained\":true}]", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        Db.AcquisitionTasks.Add(task);
        await Db.SaveChangesAsync();
        return (task, run, workset);
    }

    private static AcquisitionSourceCorrectionInput AccessCodeInput(AcquisitionSourceCorrections preview, string? code = "correct")
    {
        var source = preview.Sources.Single();
        return new(preview.ExpectedRevision, preview.ExpectedRunId!.Value, preview.CheckpointVersion!,
            Guid.NewGuid().ToString("N"), source.TargetId, source.SourceId, source.ExpectedSourceIdentity, code);
    }

    [TestMethod]
    public async Task Acquisition_access_code_preview_is_read_only_and_includes_the_actual_wrong_code()
    {
        var (task, run, _) = await SeedAcquisitionCorrectionAsync();
        var before = JsonSerializer.Serialize(await Db.WorkflowRuns.AsNoTracking().SingleAsync());
        var priorOperation = JsonSerializer.Serialize(await Db.CloudDownloadOperations.AsNoTracking().SingleAsync());
        var host = new CorrectionTestHost(Db);
        var preview = await new AcquisitionSourceCorrectionService(Db, Operations, host).GetAsync(task.Id);
        Assert.IsTrue(preview.CanCorrect);
        Assert.AreEqual("wrong", preview.Sources.Single().AccessCode);
        Assert.AreEqual("archive-kept", preview.Sources.Single().CurrentSource.ArchivePassword);
        Assert.AreEqual(0, preview.Sources.Single().History.Count);
        Assert.AreEqual(before, JsonSerializer.Serialize(await Db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual(priorOperation, JsonSerializer.Serialize(await Db.CloudDownloadOperations.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _baidu.ReadinessCalls); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Acquisition_access_code_preview_rejects_null_history_entries_without_throwing_or_mutating()
    {
        var (task, run, workset) = await SeedAcquisitionCorrectionAsync(false);
        var target = workset.Targets.Single();
        var malformedSource = target.SelectedSource! with {AccessCodeRevisions = [null!]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
            {Targets = [target with {Sources = [malformedSource]}]});
        await Db.SaveChangesAsync();
        var savedCheckpoint = run.CurrentItemJson;
        var host = new CorrectionTestHost(Db);
        var preview = await new AcquisitionSourceCorrectionService(Db, Operations, host).GetAsync(task.Id);
        Assert.IsFalse(preview.CanCorrect);
        Assert.AreEqual("SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED", preview.ReasonCode);
        Assert.AreEqual(0, preview.Sources.Count);
        Assert.AreEqual(savedCheckpoint, (await Db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentItemJson);
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(0, _baidu.ReadinessCalls);
        Assert.AreEqual(0, _baidu.TransferRequests.Count); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Acquisition_code_correction_continues_the_same_cursor_without_rewriting_plan_or_parser_evidence()
    {
        var (task, run, item) = await SeedAcquisitionCorrectionAsync();
        var oldPlan = task.PlanJson; var history = task.PlanHistoryJson; var purchases = task.PurchaseRecordJson;
        var host = new CorrectionTestHost(Db); var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var preview = await service.GetAsync(task.Id); var input = AccessCodeInput(preview);
        var accepted = await service.CorrectAsync(task.Id, input);
        Assert.IsTrue(accepted.Accepted); Assert.AreEqual(run.Id, accepted.WorkflowRunId);
        Assert.AreEqual(1, host.Calls); Assert.AreEqual(1, _baidu.TransferRequests.Count);
        var pending = await Db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Pending, pending.Status); Assert.AreEqual(0, pending.CurrentStepIndex);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(pending.PendingSignalJson!, WorkflowJson.Options)!;
        Assert.AreEqual(input.CorrectionId, signal.SourceCredentialCorrections.Single().CorrectionId);
        Assert.AreEqual("manual", signal.SourceCredentialCorrections.Single().Basis);
        Assert.AreEqual("correct", signal.SourceCredentialCorrections.Single().NewAccessCode);
        Assert.AreEqual(0, signal.SourceIds.Count); Assert.IsNull(signal.AccountId); Assert.IsNull(signal.OutputDirectory);
        var unchanged = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(oldPlan, unchanged.PlanJson); Assert.AreEqual(history, unchanged.PlanHistoryJson);
        Assert.AreEqual(purchases, unchanged.PurchaseRecordJson); Assert.AreEqual(1, unchanged.PlanRevision);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, input));
        Assert.AreEqual(1, host.Calls, "An accepted POST cannot restart the task if the response is lost.");
        var outcome = await new BaiduTransferActivity().ResumeAsync(Context(item), item, pending.PendingSignalJson!, default);
        var current = Item(outcome).Targets.Single().SelectedSource!;
        Assert.AreEqual("correct", current.AccessCode); Assert.AreEqual("archive-kept", current.ArchivePassword);
        Assert.AreEqual(1, current.AccessCodeRevisions!.Count);
        Assert.AreEqual(2, _baidu.TransferRequests.Count, "Only the known failed pre-write transfer is tried using the new credentials.");
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("revision")]
    [DataRow("checkpoint")]
    [DataRow("deleted")]
    public async Task Acquisition_code_correction_rechecks_snapshot_under_the_run_gate(string change)
    {
        var (task, run, _) = await SeedAcquisitionCorrectionAsync();
        var host = new CorrectionTestHost(Db) {BeforeValidation = async () =>
        {
            if (change == "revision") task.PlanRevision++;
            else if (change == "checkpoint") run.WaitPromptJson += " ";
            else task.DeletedAt = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }};
        var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var input = AccessCodeInput(await service.GetAsync(task.Id));
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, input));
        Assert.AreEqual("SOURCE_CORRECTION_TASK_CHANGED", error.Message);
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.IsNull((await Db.WorkflowRuns.AsNoTracking().SingleAsync()).PendingSignalJson);
    }

    [TestMethod]
    public async Task Acquisition_correction_rechecks_operation_outcome_after_preview_before_queueing()
    {
        var (task, _, _) = await SeedAcquisitionCorrectionAsync();
        var host = new CorrectionTestHost(Db) {BeforeValidation = async () =>
        {
            var op = await Db.CloudDownloadOperations.SingleAsync(); op.State = "unknown"; await Db.SaveChangesAsync();
        }};
        var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var input = AccessCodeInput(await service.GetAsync(task.Id));
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, input));
        Assert.AreEqual("SOURCE_CORRECTION_OPERATION_UNCERTAIN", error.Message);
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(1, _baidu.TransferRequests.Count);
    }

    [DataTestMethod]
    [DataRow("remote")]
    [DataRow("download")]
    [DataRow("artifact")]
    [DataRow("files")]
    public async Task Acquisition_correction_never_discards_already_acquired_content(string kind)
    {
        var (task, run, workset) = await SeedAcquisitionCorrectionAsync(false);
        var target = workset.Targets.Single();
        target = kind switch
        {
            "remote" => target with {RemoteEntry = new("baidu", "account-a", "/retained")},
            "download" => target with {DownloadTaskId = 123},
            "artifact" => target with {DownloadArtifactId = "retained"},
            _ => target with {Files = ["retained.rar"]}
        };
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]}); await Db.SaveChangesAsync();
        var host = new CorrectionTestHost(Db); var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var preview = await service.GetAsync(task.Id);
        Assert.IsFalse(preview.CanCorrect); Assert.AreEqual("SOURCE_CORRECTION_ALREADY_ACQUIRED", preview.ReasonCode);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, AccessCodeInput(preview)));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(0, _baidu.TransferRequests.Count);
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Running, "SOURCE_CORRECTION_TASK_ACTIVE")]
    [DataRow(WorkflowRunStatus.Pending, "SOURCE_CORRECTION_TASK_ACTIVE")]
    [DataRow(WorkflowRunStatus.Cancelled, "SOURCE_CORRECTION_TASK_CANCELLED")]
    [DataRow(WorkflowRunStatus.Success, "SOURCE_CORRECTION_ALREADY_COMPLETED")]
    public async Task Acquisition_correction_capability_keeps_readonly_source_values_for_ineligible_runs(WorkflowRunStatus status, string reason)
    {
        var (task, run, _) = await SeedAcquisitionCorrectionAsync(false);
        run.Status = status; await Db.SaveChangesAsync();
        var preview = await new AcquisitionSourceCorrectionService(Db, Operations, new CorrectionTestHost(Db)).GetAsync(task.Id);
        Assert.IsFalse(preview.CanCorrect); Assert.AreEqual(reason, preview.ReasonCode);
        Assert.AreEqual("wrong", preview.Sources.Single().CurrentSource.AccessCode);
    }

    [TestMethod]
    public async Task Acquisition_correction_unknown_response_cannot_submit_the_same_resume_twice()
    {
        var (task, _, _) = await SeedAcquisitionCorrectionAsync();
        var host = new CorrectionTestHost(Db) {ThrowAfterAcceptance = true};
        var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var input = AccessCodeInput(await service.GetAsync(task.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, input));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, input));
        Assert.AreEqual(1, host.Calls); Assert.AreEqual(1, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Acquisition_correction_remote_endpoint_checks_saved_paths_before_scheduling()
    {
        var (task, _, _) = await SeedAcquisitionCorrectionAsync();
        var host = new CorrectionTestHost(Db); var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var input = AccessCodeInput(await service.GetAsync(task.Id));
        var paths = new CorrectionDenyPaths();
        var controller = new AcquisitionSourceCorrectionsController(service, paths)
            {ControllerContext = new ControllerContext {HttpContext = new DefaultHttpContext()}};
        var response = await controller.Correct(task.Id, input);
        Assert.AreNotEqual(0, response.Code); Assert.IsTrue(paths.Checked.Count > 0); Assert.AreEqual(0, host.Calls);
    }

    [TestMethod]
    public async Task Completed_acquisition_keeps_corrected_source_readonly_after_checkpoint_is_cleared()
    {
        var (task, run, item) = await SeedAcquisitionCorrectionAsync(false);
        var target = item.Targets.Single();
        var first = AppendReadOnlyCorrection(target.SelectedSource!, "second-attempt");
        var latest = AppendReadOnlyCorrection(first, "correct-final");
        AddReadOnlyCorrectionBinding(run.Id, target.Id, first);
        AddReadOnlyCorrectionBinding(run.Id, target.Id, latest);
        AddReadOnlyCorrectionBinding(run.Id, target.Id, latest, "download");
        run.Status = WorkflowRunStatus.Success; run.CurrentItemJson = null; run.WaitPromptJson = null;
        await Db.SaveChangesAsync();
        var beforePlan = task.PlanJson;
        var beforeRequests = await Db.CloudDownloadOperations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.RequestJson).ToArrayAsync();
        var projected = await AcquisitionCompletedSourceProjection.ReadAsync(Db, [run.Id], default);
        Assert.AreEqual("correct-final", projected[run.Id].Targets.Single().SelectedSource!.AccessCode);
        var host = new CorrectionTestHost(Db); var service = new AcquisitionSourceCorrectionService(Db, Operations, host);
        var preview = await service.GetAsync(task.Id);
        Assert.IsFalse(preview.CanCorrect); Assert.AreEqual("SOURCE_CORRECTION_ALREADY_COMPLETED", preview.ReasonCode);
        Assert.AreEqual("correct-final", preview.Sources.Single().CurrentSource.AccessCode);
        Assert.AreEqual("archive-kept", preview.Sources.Single().CurrentSource.ArchivePassword);
        Assert.AreEqual(2, preview.Sources.Single().History.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CorrectAsync(task.Id, AccessCodeInput(preview, "another")));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(0, _baidu.ReadinessCalls); Assert.AreEqual(0, _baidu.TransferRequests.Count);
        Assert.AreEqual(beforePlan, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson);
        CollectionAssert.AreEqual(beforeRequests, await Db.CloudDownloadOperations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.RequestJson).ToArrayAsync());
        Assert.IsNull((await Db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentItemJson);
    }

    [DataTestMethod]
    [DataRow("branch")]
    [DataRow("binding")]
    [DataRow("requestIdentity")]
    [DataRow("history")]
    [DataRow("nullSource")]
    [DataRow("foreign")]
    [DataRow("unbound")]
    [DataRow("notCompleted")]
    public async Task Completed_source_projection_rejects_ambiguous_or_unbound_history(string problem)
    {
        var (_, run, item) = await SeedAcquisitionCorrectionAsync(false);
        var target = item.Targets.Single();
        var source = AppendReadOnlyCorrection(target.SelectedSource!, "correct");
        var (operation, binding) = AddReadOnlyCorrectionBinding(run.Id, target.Id, source);
        if (problem == "branch") AddReadOnlyCorrectionBinding(run.Id, target.Id, AppendReadOnlyCorrection(target.SelectedSource!, "different"));
        else if (problem == "binding") binding.Slot = target.Id + ":transfer:credential:" + Guid.NewGuid().ToString("N");
        else if (problem == "requestIdentity") operation.RequestJson = JsonSerializer.Serialize(new CloudTransferRequest("different", source, "account-a", "/saved"), WorkflowJson.Options);
        else if (problem == "history") operation.RequestJson = JsonSerializer.Serialize(new CloudTransferRequest(operation.Id,
            source with {AccessCodeRevisions = [source.AccessCodeRevisions![0] with {PreviousAccessCode = "not-the-original"}]}, "account-a", "/saved"), WorkflowJson.Options);
        else if (problem == "nullSource") operation.RequestJson = JsonSerializer.Serialize(new {operationId = operation.Id, source = (object?)null}, WorkflowJson.Options);
        else if (problem == "foreign") binding.OwnerId = "another-run";
        else if (problem == "unbound") Db.Remove(binding);
        run.Status = problem == "notCompleted" ? WorkflowRunStatus.Waiting : WorkflowRunStatus.Success;
        run.CurrentItemJson = null; run.WaitPromptJson = null;
        await Db.SaveChangesAsync();
        var projected = await AcquisitionCompletedSourceProjection.ReadAsync(Db, [run.Id], default);
        Assert.AreEqual(0, projected.Count);
        Assert.AreEqual(0, _baidu.ReadinessCalls); Assert.AreEqual(0, _baidu.TransferRequests.Count);
    }

    private static CloudSource AppendReadOnlyCorrection(CloudSource source, string code)
    {
        var revision = new CloudAccessCodeRevision {CorrectionId = Guid.NewGuid().ToString("N"),
            ExpectedSourceIdentity = CloudSourceIdentity.Key(source), PreviousAccessCode = source.AccessCode,
            NewAccessCode = code, Basis = "manual", AppliedAt = DateTime.UtcNow};
        return source with {AccessCode = code, AccessCodeRevisions = [..source.AccessCodeRevisions ?? [], revision]};
    }

    private (CloudOperationDbModel Operation, ArtifactOperationBindingDbModel Binding) AddReadOnlyCorrectionBinding(
        int runId, string targetId, CloudSource source, string kind = "transfer")
    {
        var operationId = Guid.NewGuid().ToString("N");
        var operation = new CloudOperationDbModel {Id = operationId, WorkflowRunId = runId, TargetId = targetId,
            Kind = kind, State = "succeeded", RequestJson = kind == "transfer"
                ? JsonSerializer.Serialize(new CloudTransferRequest(operationId, source, "account-a", "/saved"), WorkflowJson.Options)
                : JsonSerializer.Serialize(new CloudDownloadRequest(operationId, source, "account-a", null, "/saved"), WorkflowJson.Options)};
        var binding = new ArtifactOperationBindingDbModel {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow",
            OwnerId = runId.ToString(System.Globalization.CultureInfo.InvariantCulture), OperationId = operationId,
            Slot = targetId + ":" + kind + ":credential:" + source.AccessCodeRevisions![^1].CorrectionId};
        Db.CloudDownloadOperations.Add(operation); Db.Add(binding);
        return (operation, binding);
    }

    private sealed class CorrectionDenyPaths : IMediaPathGuard
    {
        public List<string?> Checked { get; } = [];
        public Task<bool> IsServableAsync(string? path, CancellationToken ct = default) {Checked.Add(path); return Task.FromResult(false);}
        public Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public void Invalidate() { }
    }
    private sealed class CorrectionTestHost(Bakabase.InsideWorld.Business.BakabaseDbContext db) : IWorkflowRunResumer
    {
        public int Calls; public Func<Task>? BeforeValidation; public bool ThrowAfterAcceptance;
        public Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default) => throw new AssertFailedException("Conditional continuation is required.");
        public Task RequeueAsync(int runId, CancellationToken ct = default) => throw new AssertFailedException("Conditional continuation is required.");
        public Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int runId, bool waiting, string signalJson, Func<CancellationToken, Task> validate, CancellationToken ct = default)
        {
            var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync(ct);
            try
            {
                if (BeforeValidation != null) await BeforeValidation();
                await validate(ct);
                var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId, ct);
                Assert.AreEqual(run.Status == WorkflowRunStatus.Waiting, waiting);
                run.Status = WorkflowRunStatus.Pending; run.PendingSignalJson = signalJson;
                await db.SaveChangesAsync(ct); Calls++;
                if (ThrowAfterAcceptance) throw new InvalidOperationException("Queue response was lost after durable acceptance.");
            }
            finally {gate.Release();}
        }
    }
}
