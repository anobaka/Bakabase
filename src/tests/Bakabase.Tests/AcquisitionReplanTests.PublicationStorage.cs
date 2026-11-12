using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Processed, ArtifactRecord Download, string Historical)>
        PublicationStorageInput()
    {
        var (task, run, download) = await Failed();
        var processed = await ProcessedInput(run.Id, download);
        var historical = Path.Combine(_root, "retired-volume", "Downloads");
        var plan = task.Plan! with {OutputDirectory = historical, DeliveryLayout = null};
        var workset = AcquisitionPlanCompiler.Workset(task.Id, 1, plan, task.SourceRefs);
        var target = workset.Targets.Single() with
        {
            ProcessedArtifactId = processed.Id, ProcessingOperationId = processed.OperationId,
            ProcessingIntentKey = (await Artifacts.GetOperationAsync(processed.OperationId))!.IntentKey,
            ProcessedDirectory = processed.RootDirectory, ProcessedFiles = processed.Files.ToList(),
            PublicationWaitReason = "StorageUnavailable", Problem = StorageWriteGuard.ReserveLowCode
        };
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(compiled);
        run.CurrentStepIndex = compiled.Nodes.Select((n, i) => (n, i)).Single(x => x.n.Kind == "fs.publishArtifacts").i;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        run.Status = WorkflowRunStatus.Waiting; run.CompletedAt = null; run.ErrorMessage = null;
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "main", reason = "StorageUnavailable", message = StorageWriteGuard.ReserveLowCode});
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.Status = AcquisitionStatus.Waiting; row.CompletedAt = null; row.Error = null;
        row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        Db.DownloadTasks.Add(new() {Id = 50, Key = "existing-completed-download", Name = "Unchanged original download",
            DownloadPath = historical, Status = DownloadTaskDbModelStatus.Complete, Progress = 100, Checkpoint = "retained receipt"});
        await Db.SaveChangesAsync();
        return ((await Plans.GetAsync(task.Id, default))!, run, processed, download, historical);
    }

    private AcquisitionPlanService PublicationStoragePlans(string work, string delivery, IUserStoragePolicy storage)
    {
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
        {
            ProcessingWorkspaceDirectory = work, DeliveryRootDirectory = delivery,
            StoragePolicy = new() {ProcessingWorkspaceDirectory = work, SourceCleanup = "delete"}
        });
        return new(Db, [], _services.GetRequiredService<IAcquisitionService>(),
            _services.GetRequiredService<IWorkflowDefinitionService>(), _services.GetRequiredService<DownloadIntentService>(),
            storage, artifacts: Artifacts, deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
    }

    private static AcquisitionPlanReplanInput ContinuePublication(AcquisitionPlanTaskDetail task, int run, string artifact) =>
        new(task.Task.PlanRevision, run, "Continue the verified processed files using current local storage.",
            [new("main", new() {Requirement = "notRequired", Steps = []}, [artifact], artifact)], UseCurrentStorageDefaults: true);

    [TestMethod]
    public async Task Publication_storage_wait_replans_only_verified_processed_files_to_current_storage_and_preserves_history()
    {
        var f = await PublicationStorageInput();
        Assert.IsTrue(AcquisitionPublicationStorageWait.Matches(f.Run));
        CollectionAssert.AreEqual(new[] {f.Processed.Id}, f.Task.ReplanArtifacts.Select(a => a.ArtifactId).ToArray());
        var beforeRun = JsonSerializer.Serialize(await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.Run.Id));
        var beforeDownloads = JsonSerializer.Serialize(await Db.DownloadTasks.AsNoTracking().ToListAsync());
        var row = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        var beforeHistory = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(row.PlanHistoryJson!, WorkflowJson.Options)!;
        var work = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName;
        var policy = new PublicationStoragePolicy(f.Historical);
        var plans = PublicationStoragePlans(work, delivery, policy);
        var request = ContinuePublication(f.Task, f.Run.Id, f.Processed.Id);
        var revised = await plans.ReplanAsync(f.Task.Id, request, default);
        Assert.AreEqual(2, revised.Task.PlanRevision); Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.AreEqual("ready", revised.Task.Phase); Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status);
        Assert.AreEqual(f.Processed.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(f.Run.Id, revised.Plan.Parts.Single().ContinueFrom!.PreviousRunId);
        Assert.AreEqual(work, revised.Plan.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.AreEqual("keep", revised.Plan.StoragePolicy.SourceCleanup);
        Assert.AreEqual(delivery, revised.Plan.DeliveryLayout!.RootDirectory);
        Assert.IsTrue(revised.Plan.DeliveryLayout.Settings.ContentLayout!.RemoveGeneratedDirectories);
        Assert.IsFalse(Directory.Exists(revised.Plan.DeliveryLayout.TaskDirectory));
        var nodes = AcquisitionPlanCompiler.Compile(revised.Plan).Nodes;
        Assert.IsTrue(nodes.All(n => n.Kind is AcquisitionPlanContinueFilesActivity.ActivityKind or
            AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts"));
        Assert.IsTrue(nodes.Any(n => n.Kind == AcquisitionPlanContinueFilesActivity.ActivityKind));
        Assert.IsTrue(policy.Checked.All(p => !p.StartsWith(f.Historical, StringComparison.Ordinal)));
        Assert.AreEqual(beforeRun, JsonSerializer.Serialize(await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.Run.Id)));
        Assert.AreEqual(JsonSerializer.Serialize(beforeHistory), JsonSerializer.Serialize(revised.PlanHistory.Take(beforeHistory.Count)));
        Assert.AreEqual(beforeDownloads, JsonSerializer.Serialize(await Db.DownloadTasks.AsNoTracking().ToListAsync()));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(f.Download.Files.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(f.Processed.Files.Single()));
        var borrowed = await AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts, f.Task.Id, "main",
            revised.Plan.Parts.Single().ContinueFrom!, default);
        Assert.AreEqual(f.Processed.Id, borrowed.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => plans.ReplanAsync(f.Task.Id, request, default));
        Assert.AreEqual(2, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id)).PlanRevision);
    }

    [DataTestMethod]
    [DataRow("pending-answer")]
    [DataRow("running")]
    [DataRow("password")]
    [DataRow("different-wait")]
    [DataRow("wrong-node")]
    [DataRow("future-download")]
    [DataRow("future-transfer")]
    [DataRow("unknown-future-node")]
    [DataRow("download-publication")]
    [DataRow("forced-publication")]
    [DataRow("malformed-prompt")]
    [DataRow("missing-target")]
    [DataRow("wrong-target")]
    [DataRow("missing-target-wait")]
    public async Task Publication_storage_replan_does_not_open_unrelated_waits_or_network_work(string fault)
    {
        var f = await PublicationStorageInput();
        var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        switch (fault)
        {
            case "pending-answer": f.Run.PendingSignalJson = "{}"; break;
            case "running": f.Run.Status = WorkflowRunStatus.Running; break;
            case "password": f.Run.WaitPromptJson = "{\"targetId\":\"main\",\"reason\":\"PasswordRequired\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}"; break;
            case "different-wait": f.Run.WaitReason = "cloud.fileProcessing"; break;
            case "wrong-node": f.Run.CurrentStepIndex = 0; break;
            case "future-download": plan = plan with {Nodes = [..plan.Nodes, new() {NodeId = "later", Kind = "download.awaitCompletion"}]}; break;
            case "future-transfer": plan = plan with {Nodes = [..plan.Nodes, new() {NodeId = "later", Kind = "baidu.transfer"}]}; break;
            case "unknown-future-node": plan = plan with {Nodes = [..plan.Nodes, new() {NodeId = "later", Kind = "plugin.custom"}]}; break;
            case "download-publication":
            case "forced-publication":
                plan = plan with {Nodes = plan.Nodes.Select(n => n.Kind == "fs.publishArtifacts" ? n with
                {ConfigJson = JsonSerializer.Serialize(new FileArtifactPublicationConfig
                    {Source = fault == "download-publication" ? "download" : "processed", ReusePolicy = fault == "forced-publication" ? "force" : "reuseVerified"}, WorkflowJson.Options)} : n).ToArray()}; break;
            case "malformed-prompt": f.Run.WaitPromptJson = "[]"; break;
            case "missing-target": f.Run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\"}"; break;
            case "wrong-target": f.Run.WaitPromptJson = "{\"targetId\":\"other\",\"reason\":\"StorageUnavailable\"}"; break;
            case "missing-target-wait": var checkpoint = JsonNode.Parse(f.Run.CurrentItemJson!)!;
                checkpoint["item"]!["targets"]![0]!["publicationWaitReason"] = null;
                f.Run.CurrentItemJson = checkpoint.ToJsonString(); break;
        }
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan); await Db.SaveChangesAsync();
        Assert.AreEqual(0, (await Plans.GetAsync(f.Task.Id, default))!.ReplanArtifacts.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentStoragePlans(
            Directory.CreateDirectory(Path.Combine(_root, "work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName).ReplanAsync(f.Task.Id,
                ContinuePublication(f.Task, f.Run.Id, f.Processed.Id), default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id)).PlanRevision);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("missing-binding")]
    [DataRow("other-owner")]
    [DataRow("shared-owner")]
    [DataRow("unfinished-operation")]
    [DataRow("different-artifact")]
    [DataRow("stale-intent")]
    public async Task Publication_storage_replan_candidates_require_exact_current_processed_evidence(string fault)
    {
        var f = await PublicationStorageInput();
        switch (fault)
        {
            case "missing-binding": Db.ArtifactOperationBindings.RemoveRange(Db.ArtifactOperationBindings.Where(b => b.OperationId == f.Processed.OperationId)); break;
            case "other-owner": (await Db.Set<WorkflowRunSourceDbModel>().SingleAsync(s => s.WorkflowRunId == f.Run.Id)).EntityId = "999"; break;
            case "shared-owner": Db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = f.Run.Id, Module = "acquisition", EntityKind = "task", EntityId = "999"}); break;
            case "unfinished-operation": (await Db.ArtifactOperations.SingleAsync(o => o.Id == f.Processed.OperationId)).State = ArtifactOperationState.Running; break;
            case "different-artifact":
            case "stale-intent":
                var snapshot = JsonNode.Parse(f.Run.CurrentItemJson!)!;
                snapshot["item"]!["targets"]![0]![fault == "different-artifact" ? "processedArtifactId" : "processingIntentKey"] = "different";
                f.Run.CurrentItemJson = snapshot.ToJsonString(); break;
        }
        await Db.SaveChangesAsync();
        Assert.AreEqual(0, (await Plans.GetAsync(f.Task.Id, default))!.ReplanArtifacts.Count);
        Exception? failure = null;
        try {await CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName).ReplanAsync(f.Task.Id,
                ContinuePublication(f.Task, f.Run.Id, f.Processed.Id), default);}
        catch (Exception error) {failure = error;}
        Assert.IsTrue(failure is ArgumentException or InvalidOperationException, failure?.ToString() ?? "Replan unexpectedly succeeded");
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id)).PlanRevision);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("without-current-defaults")]
    [DataRow("download-input")]
    [DataRow("same-length-content-change")]
    public async Task Publication_storage_replan_requires_explicit_local_defaults_and_full_input_verification(string fault)
    {
        var f = await PublicationStorageInput();
        var request = ContinuePublication(f.Task, f.Run.Id, fault == "download-input" ? f.Download.Id : f.Processed.Id);
        if (fault == "without-current-defaults") request = request with {UseCurrentStorageDefaults = false};
        if (fault == "same-length-content-change")
            await File.WriteAllTextAsync(f.Processed.Files.Single(), new string('x', (int)new FileInfo(f.Processed.Files.Single()).Length));
        Exception? failure = null;
        try {await CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName).ReplanAsync(f.Task.Id, request, default);}
        catch (Exception error) {failure = error;}
        Assert.IsTrue(failure is ArgumentException or InvalidOperationException, failure?.ToString() ?? "Replan unexpectedly succeeded");
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id)).PlanRevision);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.Run.Id)).Status);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

    private sealed class PublicationStoragePolicy(string historical) : IUserStoragePolicy
    {
        internal List<string> Checked { get; } = [];
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => !path.StartsWith(historical, StringComparison.Ordinal);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {Checked.Add(path); if (!IsPathAllowed(path, purpose)) throw new UserStoragePathException("Historical NAS is not mounted.");}
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }
}
