using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class AcquisitionReplanTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private AcquisitionPlanService Plans => _services.GetRequiredService<AcquisitionPlanService>();
    private IArtifactOperationStore Artifacts => _services.GetRequiredService<IArtifactOperationStore>();

    [TestInitialize]
    public async Task Setup()
    {
        _root = Directory.CreateTempSubdirectory("acquisition-replan-").FullName;
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<DownloadMethodRegistry>();
            services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
            // These tiny replan fixtures exercise the real reserve policy and write guard,
            // independently of the developer machine's current free space. Capacity-failure
            // tests supply their own measurements in StorageWriteGuardTests and related suites.
            services.RemoveAll<IStorageWriteGuard>();
            services.AddSingleton<IStorageWriteGuard>(sp => new StorageWriteGuard(
                sp.GetRequiredService<IStorageWritePolicyProvider>(), _ => (_root, 1L << 40), TimeSpan.FromMilliseconds(500)));
            services.RemoveAll<Bakabase.InsideWorld.Business.Components.Compression.IArchiveExtractionService>();
            services.AddSingleton<Bakabase.InsideWorld.Business.Components.Compression.IArchiveExtractionService>(_continuationArchives);
            // AppService's test default is shared across providers. Pinned workspace identities
            // must stay inside this fixture, including when another run used identical file bytes.
            var appData = Path.Combine(_root, "appdata");
            services.AddScoped(_ => new FileProcessingWorkspaceResolver(appData));
            services.AddScoped(sp => new ArtifactWorkspaceMaintenance(sp.GetRequiredService<BakabaseDbContext>(), sp.GetRequiredService<IArtifactOperationStore>(),
                sp.GetRequiredService<FileProcessingPlanExecutor>(), sp.GetRequiredService<RecursiveArchiveExecutor>(), appData));
            services.AddScoped(sp => new FileArtifactProcessor(sp.GetRequiredService<IArtifactOperationStore>(),
                sp.GetRequiredService<FileProcessingPlanExecutor>(), sp.GetRequiredService<RecursiveArchiveExecutor>(),
                () => appData, sp.GetRequiredService<IServiceScopeFactory>(), maintenance: sp.GetRequiredService<ArtifactWorkspaceMaintenance>()));
        });
    }
    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Artifact)> Failed(bool implicitAccount = false)
    {
        var source = new WorkflowSourceRef("test", "item", "original", "2", "0");
        var plan = new AcquisitionPlan {Title = "Saved source", OutputDirectory = _root, Parts = [new()
        {
            Id = "main", SelectedSourceId = "source", ProcessingSource = source, AppliedProcessingRevisionId = "source-revision",
            Sources = [new() {Id = "source", Url = "https://example.test/files", Kind = "http", AccountId = implicitAccount ? null : "11111111111111111111111111111111", AccessCode = "saved-code"}],
            Processing = new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]}
        }]};
        var prepared = await Plans.CreatePreparedPlanAsync(plan, [source]);
        // Freeze the server-resolved directory scopes alongside the executable plan.
        plan = prepared.Plan!;
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Failed,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(AcquisitionPlanCompiler.Compile(plan)),
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, ErrorMessage = "Missing volume", CurrentStepIndex = 3,
            CurrentItemJson = "saved-checkpoint", OutputItemsJson = "saved-output",
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = prepared.Id.ToString(), Revision = "1"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var row = await Db.AcquisitionTasks.FindAsync(prepared.Id);
        row!.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Failed; row.Phase = "attention";
        row.Error = run.ErrorMessage; row.CompletedAt = run.CompletedAt; await Db.SaveChangesAsync();
        var file = Path.Combine(_root, "original.bin"); await File.WriteAllTextAsync(file, "original bytes");
        var target = AcquisitionPlanCompiler.Workset(prepared.Id, 1, plan, [source]).Targets.Single();
        if (implicitAccount) target = target with {AccountId = "22222222222222222222222222222222"};
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        var artifact = await Artifacts.AdoptAsync(new("download-op", intent, "download", "input"),
            new(_root, [file], "download", true), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:artifact.download", artifact.OperationId);
        return ((await Plans.GetAsync(prepared.Id, default))!, run, artifact);
    }

    private static AcquisitionPlanReplanInput Input(AcquisitionPlanTaskDetail task, WorkflowRunDbModel run, string artifactId) =>
        new(task.Task.PlanRevision, run.Id, "Observed wrapper and sibling volumes in the validated download; task-local adjustment.",
            [new("main", new() {Requirement = "required", Steps =
            [new() {Id = "unwrap", Op = "extractArchive", Selector = "wrapper.rar"},
             new() {Id = "place", Op = "moveFile", Input = "unwrap", TargetDirectory = "parts"},
             new() {Id = "extract", Op = "extractArchive", Inputs = ["download", "place"], Selector = "parts/*"}]}, [artifactId])]);

    [TestMethod]
    public async Task Replan_appends_task_revision_and_keeps_original_run_files_source_and_download_identity()
    {
        var (task, run, artifact) = await Failed(); var frozen = run.PlanSnapshotJson;
        Assert.AreEqual(artifact.Id, task.ReplanArtifacts.Single().ArtifactId);
        var input = Input(task, run, artifact.Id);
        var revised = await Plans.ReplanAsync(task.Id, input, default);
        Assert.AreEqual(2, revised.Task.PlanRevision); Assert.AreEqual("ready", revised.Task.Phase);
        Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status); Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.IsNull(revised.Task.Error); Assert.IsNull(revised.Task.CompletedAt);
        Assert.AreEqual(2, revised.PlanHistory.Count); Assert.AreEqual(1, revised.PlanHistory[0].Plan.Parts[0].Processing!.Steps.Count);
        var history = revised.PlanHistory[1]; Assert.AreEqual(run.Id, history.PreviousRunId); Assert.AreEqual(input.Reason, history.Reason);
        Assert.AreEqual(DateTimeKind.Utc, history.CreatedAt.Kind);
        Assert.AreEqual(artifact.Fingerprint, history.ArtifactEvidence!.Single().Fingerprint);
        Assert.AreEqual(JsonSerializer.Serialize(artifact.Manifest), JsonSerializer.Serialize(history.ArtifactEvidence.Single().Manifest));
        Assert.AreEqual(task.Plan!.OutputDirectory, revised.Plan!.OutputDirectory);
        Assert.AreEqual(task.Plan.Parts[0].Sources[0], revised.Plan.Parts[0].Sources[0]);
        Assert.AreEqual(task.Plan.Parts[0].ProcessingSource, revised.Plan.Parts[0].ProcessingSource);
        Assert.IsNull(revised.Plan.Parts[0].AppliedProcessingRevisionId, "Task-local observations must not impersonate the source revision.");
        var originalTarget = AcquisitionPlanCompiler.Workset(task.Id, 1, task.Plan, task.SourceRefs).Targets.Single();
        var revisedTarget = AcquisitionPlanCompiler.Workset(task.Id, 2, revised.Plan, revised.SourceRefs).Targets.Single();
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(originalTarget, _root), DownloadArtifactCoordinator.DownloadIntent(revisedTarget, _root));
        var stored = await Db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(frozen, stored.PlanSnapshotJson); Assert.AreEqual(WorkflowRunStatus.Failed, stored.Status);
        Assert.AreEqual("saved-checkpoint", stored.CurrentItemJson); Assert.AreEqual("saved-output", stored.OutputItemsJson);
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(artifact.Files.Single()));
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync(), "Preparing a revision must not start a run.");
        CollectionAssert.AreEqual(new[] {"download", "place"}, revised.Steps.Last(s => s.Operation != null).Inputs!.ToArray());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, input, default));
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Pending)]
    [DataRow(WorkflowRunStatus.Running)]
    [DataRow(WorkflowRunStatus.Waiting)]
    [DataRow(WorkflowRunStatus.Success)]
    public async Task Actual_run_status_fences_stale_failed_task_status(WorkflowRunStatus status)
    {
        var (task, run, artifact) = await Failed();
        run.Status = status; await Db.SaveChangesAsync();
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, Input(task, run, artifact.Id), default));
        Assert.AreEqual("PLAN_REPLAN_NOT_STOPPED", error.Message);
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [TestMethod]
    public async Task Replan_checks_revision_and_current_run_before_replacing_anything()
    {
        var (task, run, artifact) = await Failed(); var input = Input(task, run, artifact.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, input with {ExpectedRevision = 0}, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, input with {ExpectedRunId = run.Id + 1}, default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [DataTestMethod]
    [DataRow("other-part", false)]
    [DataRow("main", true)]
    public async Task Evidence_must_be_bound_to_this_run_and_exact_part(string partId, bool anotherRun)
    {
        var (task, run, artifact) = await Failed();
        await Db.Set<ArtifactOperationBindingDbModel>().ExecuteDeleteAsync();
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", (anotherRun ? run.Id + 1 : run.Id).ToString(), partId + ":artifact.download", artifact.OperationId);
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, Input(task, run, artifact.Id), default));
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_INVALID", error.Message);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_or_modified_files_cannot_back_a_revision(bool remove)
    {
        var (task, run, artifact) = await Failed();
        if (remove) File.Delete(artifact.Files.Single()); else await File.WriteAllTextAsync(artifact.Files.Single(), "modified bytes");
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, Input(task, run, artifact.Id), default));
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", error.Message);
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [TestMethod]
    public async Task Replanned_run_uses_actual_download_identity_even_when_original_account_was_automatic()
    {
        var (task, run, artifact) = await Failed(implicitAccount: true);
        Assert.IsNull(task.Plan!.Parts[0].Sources[0].AccountId);
        var revised = await Plans.ReplanAsync(task.Id, Input(task, run, artifact.Id), default);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, task.SourceRefs);
        var resolved = workset.Targets.Single() with {AccountId = "22222222222222222222222222222222"};
        workset = workset with {Targets = [resolved]};
        Assert.IsTrue(await Plans.HasUnchangedDownloadIdentityAsync(run.Id, workset, default));
        Assert.IsFalse(await Plans.HasUnchangedDownloadIdentityAsync(run.Id,
            workset with {Targets = [resolved with {AccountId = "33333333333333333333333333333333"}]}, default));
        Assert.IsFalse(await Plans.HasUnchangedDownloadIdentityAsync(run.Id,
            workset with {Targets = [resolved with {Sources = [resolved.Sources[0] with {MethodScope = "different-engine"}]}]}, default));
        Assert.IsTrue(await Plans.HasUnchangedDownloadIdentityAsync(run.Id,
            workset with {OutputDirectory = Path.Combine(_root, "other")}, default),
            "An allocated task directory remains the actual download location when only the global root differs.");
        Assert.IsFalse(await Plans.HasUnchangedDownloadIdentityAsync(run.Id,
            workset with {Targets = [resolved with {DownloadDirectoryScope = resolved.DownloadDirectoryScope! with
                {Directory = Path.Combine(_root, "another-task")}}]}, default),
            "Changing the concrete task download directory changes its semantic identity.");
        Assert.IsFalse(await Plans.HasUnchangedDownloadIdentityAsync(run.Id + 1, workset, default));
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
        Assert.IsNull((await Plans.GetAsync(task.Id, default))!.Task.WorkflowRunId);
    }

    [TestMethod]
    public async Task Retry_gate_prevents_replan_from_using_the_status_seen_before_retry()
    {
        var (task, run, artifact) = await Failed(); var gate = AcquisitionTaskGate.For(task.Id);
        await gate.WaitAsync();
        Task<AcquisitionPlanTaskDetail> pending;
        try
        {
            pending = Plans.ReplanAsync(task.Id, Input(task, run, artifact.Id), default);
            Assert.IsFalse(pending.IsCompleted);
            run.Status = WorkflowRunStatus.Pending; await Db.SaveChangesAsync();
        }
        finally {gate.Release();}
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pending);
    }
}
