using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Tests.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class RevisionCandidates : IAcquisitionSourceRevisionCandidates
    {
        public string EvidenceVersion = "first-evidence";
        public Task<IReadOnlyList<AcquisitionSourceRevisionCandidate>> ReadAsync(AcquisitionPlan plan, IReadOnlyList<WorkflowSourceRef> refs, CancellationToken ct)
        {
            var source = new AcquisitionPlanSource {Id = "new-source", Url = "https://example.org/new.zip", Kind = "http",
                ProviderId = "http", ArchivePassword = "new-password", Processing = new() {Requirement = "unknown"}};
            return Task.FromResult<IReadOnlyList<AcquisitionSourceRevisionCandidate>>([
                new("generic-mirror", plan.Parts.Single().Id, source, [new("custom", "entry", "fixture", "2")], "confirmed mirror", EvidenceVersion)]);
        }
    }

    private async Task<(AcquisitionSourceRevisionService Service, RevisionCandidates Candidates,
        int TaskId, int RunId, int DownloadId, RecoverableProvider Provider, CloudDownloadTarget Target)> RevisionFixture()
    {
        var (task, run, workset, download, _, provider, recovery) = await SeedRecoverableProvider();
        var source = workset.Targets.Single().SelectedSource!;
        var plan = new AcquisitionPlan {Title = "Source revision", OutputDirectory = _root, Parts = [new()
        {Id = "part", SelectedSourceId = source.Id, Sources = [new() {Id = source.Id, Url = source.Url, Kind = source.Kind,
            ProviderId = source.ProviderId}], Processing = new() {Requirement = "notRequired"}}]};
        task.PlanJson = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
        task.SourceRefsJson = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("custom", "entry", "fixture", "1")}, JsonSerializerOptions.Web);
        task.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, plan, [], DateTime.UtcNow)}, JsonSerializerOptions.Web);
        await _db.SaveChangesAsync();
        var intents = new DownloadIntentService(new([], [], null!, null!), null!, new UserStoragePolicy(false), _db,
            null!, null!, null!, null!, null!);
        var candidates = new RevisionCandidates();
        var safety = new AcquisitionSourceRevisionSafety(_db, recovery, [provider]);
        var service = new AcquisitionSourceRevisionService(_db, [candidates], safety, intents, new(),
            _services.GetRequiredService<DownloaderManager>());
        return (service, candidates, task.Id, run.Id, download.Id, provider, workset.Targets.Single());
    }

    [TestMethod]
    public async Task SourceRevision_new_plan_passes_execution_identity_guard_but_only_for_its_reviewed_source()
    {
        var f = await RevisionFixture(); var preview = await RevisionPreview(f.Service, f.TaskId);
        await f.Service.ApplyAsync(f.TaskId, new(preview.PreviewToken));
        var intents = new DownloadIntentService(new([], [], null!, null!), null!, new UserStoragePolicy(false), _db,
            null!, null!, null!, null!, null!);
        var plans = new AcquisitionPlanService(_db, [],
            _services.GetRequiredService<Bakabase.Modules.Acquisition.Abstractions.Services.IAcquisitionService>(),
            _services.GetRequiredService<Bakabase.Modules.Workflow.Abstractions.Services.IWorkflowDefinitionService>(), intents, new UserStoragePolicy(false));
        var prepared = await plans.PreflightAsync(f.TaskId, default);
        Assert.IsFalse(prepared.Issues.Any(i => i.Code is "PLAN_REPLAN_SOURCE_CHANGED" or "SOURCE_REVISION_EXECUTION_CHANGED"));
        Assert.IsNull(prepared.Task.WorkflowRunId);
        var row = await _db.AcquisitionTasks.SingleAsync();
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, JsonSerializerOptions.Web)!;
        var part = plan.Parts.Single();
        row.PlanJson = JsonSerializer.Serialize(plan with {Parts = [part with {Sources = part.Sources.Select(s => s.Id == part.SelectedSourceId
            ? s with {Url = "https://example.org/unreviewed.zip"} : s).ToList()}]}, JsonSerializerOptions.Web);
        await _db.SaveChangesAsync();
        var rejected = await plans.PreflightAsync(f.TaskId, default);
        Assert.IsTrue(rejected.Issues.Any(i => i.Code == "SOURCE_REVISION_EXECUTION_CHANGED"));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    private static async Task<AcquisitionSourceRevisionPreview> RevisionPreview(AcquisitionSourceRevisionService service, int taskId)
    {
        var options = await service.GetAsync(taskId);
        Assert.IsTrue(options.CanRevise, options.ReasonCode);
        var choice = options.Candidates.Single();
        return await service.PreviewAsync(taskId, new(options.ExpectedRevision, options.ExpectedRunId!.Value, choice.PartId, choice.Id));
    }

    [TestMethod]
    public async Task SourceRevision_preview_is_readonly_and_uses_the_new_mirrors_own_instructions()
    {
        var f = await RevisionFixture();
        var oldRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var oldTask = JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync());
        var preview = await RevisionPreview(f.Service, f.TaskId);
        Assert.IsTrue(preview.CanApply, preview.ReasonCode);
        Assert.IsTrue(preview.ProcessingChanged); Assert.AreEqual("unknown", preview.ProposedProcessing!.Requirement);
        Assert.AreEqual("new-password", preview.ProposedSource.ArchivePassword);
        Assert.IsNull(preview.CurrentSource.ArchivePassword);
        Assert.AreEqual(oldRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual(oldTask, JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, f.Provider.Checks, "No old provider network revalidation or writes during inspection.");
    }

    [TestMethod]
    public async Task SourceRevision_can_replace_a_nonretryable_source_without_authorizing_an_original_download_retry()
    {
        var f = await RevisionFixture();
        (await _db.DownloadTasks.SingleAsync()).Message = "PIKPAK_STORAGE_QUOTA_EXCEEDED";
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        var retry = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(retry.Row.CanContinue); Assert.AreEqual("RECOVERY_MANUAL_REVIEW", retry.Row.ReasonCode);
        var retainedDownload = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        var preview = await RevisionPreview(f.Service, f.TaskId);
        Assert.IsTrue(preview.CanApply, preview.ReasonCode);
        await f.Service.ApplyAsync(f.TaskId, new(preview.PreviewToken));
        Assert.AreEqual(0, f.Provider.Checks, "Switching does not probe or retry the rejected original source.");
        Assert.AreEqual(retainedDownload, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).Status);
        Assert.IsNull((await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).WorkflowRunId);
    }

    [TestMethod]
    public async Task SourceRevision_apply_is_durable_idempotent_and_retires_original_run_without_touching_download_or_history()
    {
        var f = await RevisionFixture(); var before = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var oldDownload = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        var oldOp = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync());
        var oldRun = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        var preview = await RevisionPreview(f.Service, f.TaskId);
        var result = await f.Service.ApplyAsync(f.TaskId, new(preview.PreviewToken));
        Assert.AreEqual(2, result.PlanRevision);
        Assert.AreEqual(result, await f.Service.ApplyAsync(f.TaskId, new(preview.PreviewToken)));
        var after = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.IsNull(after.WorkflowRunId); Assert.AreEqual(before.PurchaseRecordJson, after.PurchaseRecordJson);
        var history = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(after.PlanHistoryJson!, JsonSerializerOptions.Web)!;
        Assert.AreEqual(2, history.Count); Assert.AreEqual(before.PlanJson, JsonSerializer.Serialize(history[0].Plan, JsonSerializerOptions.Web));
        Assert.AreEqual(f.RunId, history[1].PreviousRunId); Assert.AreEqual(preview.PreviewToken, history[1].SourceRevision!.DecisionId);
        Assert.AreEqual("new-source", history[1].Plan.Parts.Single().SelectedSourceId);
        Assert.AreEqual(2, history[1].Plan.Parts.Single().Sources.Count, "Original frozen source remains available as evidence.");
        var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(WorkflowRunStatus.Cancelled, run.Status); Assert.AreEqual(oldRun.CurrentItemJson, run.CurrentItemJson);
        Assert.AreEqual(oldRun.PlanSnapshotJson, run.PlanSnapshotJson); Assert.AreEqual(oldRun.ErrorMessage, run.ErrorMessage);
        Assert.AreEqual(oldDownload, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(oldOp, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()));
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync()); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        var guard = new AcquisitionWorkflowRunMutationGuard(_db);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => guard.EnsureCanMutateAsync(f.RunId));
        StringAssert.StartsWith(error.Message, "ACQUISITION_PLAN_SUPERSEDED");
        var startGuard = new WorkflowOwnedDownloadStartGuard(_db, [guard]);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => {await using var lease = await startGuard.AcquireAsync(f.DownloadId);});
    }

    [DataTestMethod]
    [DataRow("run")]
    [DataRow("candidate")]
    [DataRow("file")]
    [DataRow("remote")]
    [DataRow("download")]
    [DataRow("revision")]
    public async Task SourceRevision_stale_preview_or_new_side_effect_cannot_change_the_task(string change)
    {
        var f = await RevisionFixture(); var preview = await RevisionPreview(f.Service, f.TaskId);
        if (change == "candidate") f.Candidates.EvidenceVersion = "changed";
        if (change == "remote") f.Provider.CanAbandon = false;
        if (change == "file")
        {
            var d = await _db.DownloadTasks.SingleAsync(); Directory.CreateDirectory(d.DownloadPath);
            await File.WriteAllTextAsync(Path.Combine(d.DownloadPath, ".partial"), "retained");
        }
        if (change == "download") (await _db.DownloadTasks.SingleAsync()).Status = DownloadTaskDbModelStatus.InProgress;
        if (change == "run") (await _db.WorkflowRuns.SingleAsync()).Status = WorkflowRunStatus.Pending;
        if (change == "revision") (await _db.AcquisitionTasks.SingleAsync()).PlanRevision++;
        await _db.SaveChangesAsync(); var before = JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.TaskId, new(preview.PreviewToken)));
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync()); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("completed")]
    [DataRow("receipt")]
    [DataRow("historicalTransfer")]
    [DataRow("shared")]
    [DataRow("remote")]
    [DataRow("unknownFailure")]
    public async Task SourceRevision_blocks_completed_partial_uncertain_and_shared_downloads(string change)
    {
        var f = await RevisionFixture();
        // A known but non-retryable failure must still pass every independent safety guard.
        (await _db.DownloadTasks.SingleAsync()).Message = "PIKPAK_STORAGE_QUOTA_EXCEEDED";
        if (change == "completed") (await _db.DownloadTasks.SingleAsync()).CompletedAt = DateTime.Now;
        if (change == "receipt") (await _db.CloudDownloadOperations.SingleAsync()).ReceiptJson = "{}";
        if (change == "shared") _db.ArtifactOperationBindings.Add(new() {Id = "other-owner", OwnerKind = "workflow", OwnerId = "999",
            Slot = "part:download", OperationId = (await _db.CloudDownloadOperations.SingleAsync()).Id});
        if (change == "historicalTransfer") _db.ArtifactOperationBindings.Add(new() {Id = "prior", OwnerKind = "workflow", OwnerId = f.RunId.ToString(),
            Slot = "part:transfer:credential:older", OperationId = "missing-operation"});
        if (change == "remote") f.Provider.CanAbandon = false;
        if (change == "unknownFailure") (await _db.DownloadTasks.SingleAsync()).Message = "unknown request failure";
        await _db.SaveChangesAsync(); var options = await f.Service.GetAsync(f.TaskId);
        Assert.IsFalse(options.CanRevise); Assert.IsNotNull(options.ReasonCode);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
    }

    [DataTestMethod]
    [DataRow("absent", true)]
    [DataRow("lock-only", true)]
    [DataRow("active", false)]
    [DataRow("partial", false)]
    [DataRow("registered", false)]
    [DataRow("manifest.json", false)]
    [DataRow("completed.json", false)]
    [DataRow("manifest.json.fixture.tmp", false)]
    public async Task SharePoint_source_revision_requires_a_stopped_download_without_retained_state(string scenario, bool expected)
    {
        var source = new CloudSource {Id = "source", ProviderId = "oneDrive", MethodId = "oneDrive.sharePoint",
            Url = "https://tenant.sharepoint.com/:f:/g/personal/user/fixture"};
        var (_, run, workset, download, operation, _, _) = await SeedRecoverableProvider(source, "PUBLIC_SHARE_NETWORK_ERROR");
        var appData = Directory.CreateDirectory(Path.Combine(_root, "app-data")).FullName;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Id))).ToLowerInvariant();
        var state = Path.Combine(appData, "cloud-downloads", "resolved", "oneDrive", key);
        string? retained = null;
        if (scenario != "absent")
        {
            Directory.CreateDirectory(state);
            await File.WriteAllBytesAsync(Path.Combine(state, "operation.lock"), []);
        }
        switch (scenario)
        {
            case "active": download.Status = DownloadTaskDbModelStatus.InProgress; break;
            case "registered": _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, FilesJson = "[]"}); break;
            case "partial":
                Directory.CreateDirectory(download.DownloadPath);
                retained = Path.Combine(download.DownloadPath, ".partial");
                break;
            case "manifest.json" or "completed.json" or "manifest.json.fixture.tmp":
                retained = Path.Combine(state, scenario);
                break;
        }
        if (retained != null) await File.WriteAllTextAsync(retained, "retained");
        await _db.SaveChangesAsync();
        using var handler = new PublicShareResolverTests.Reply(_ => throw new AssertFailedException("Source revision must not contact the original share."));
        var provider = new SharePointCloudProvider(appData, null!, new PublicShareResolverTests.Clients(handler));
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        var safety = new AcquisitionSourceRevisionSafety(_db, recovery, [provider]);

        var proof = await safety.CheckAsync(run.Id, workset.Targets.Single(), workset.OutputDirectory, default);

        Assert.AreEqual(expected, proof.Safe, proof.ReasonCode);
        Assert.AreEqual(scenario != "absent", Directory.Exists(state));
        if (Directory.Exists(state)) Assert.AreEqual(0, new FileInfo(Path.Combine(state, "operation.lock")).Length);
        if (retained != null) Assert.AreEqual("retained", await File.ReadAllTextAsync(retained));
    }
}
