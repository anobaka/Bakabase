using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private AcquisitionPlanService CurrentStoragePlans(string work, string delivery)
    {
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
        {
            ProcessingWorkspaceDirectory = work, DeliveryRootDirectory = delivery,
            StoragePolicy = new() {ProcessingWorkspaceDirectory = work, SourceCleanup = "delete"}
        });
        var storage = _services.GetRequiredService<IUserStoragePolicy>();
        return new(Db, [], _services.GetRequiredService<IAcquisitionService>(),
            _services.GetRequiredService<IWorkflowDefinitionService>(), _services.GetRequiredService<DownloadIntentService>(),
            storage, artifacts: Artifacts, deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
    }

    [TestMethod]
    public async Task Completed_published_files_can_be_borrowed_with_explicit_current_storage_without_altering_history()
    {
        var (task, run, input, published, _) = await CompletedInput(true);
        Assert.IsTrue(task.ReplanArtifacts.Any(a => a.ArtifactId == published.Id && a.Kind == "publishedFiles"));
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        var oldHistory = row!.PlanHistoryJson; var oldRunPlan = run.PlanSnapshotJson;
        var work = Directory.CreateDirectory(Path.Combine(_root, "new-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "new-delivery")).FullName;
        var plans = CurrentStoragePlans(work, delivery);
        var guarded = new List<string>();
        var request = ContinueInput(task, run.Id, published.Id) with {UseCurrentStorageDefaults = true};
        var revised = await plans.ReplanAsync(task.Id, request, default, path => {guarded.Add(path!); return Task.CompletedTask;});
        Assert.AreEqual(2, revised.Task.PlanRevision);
        Assert.AreEqual(published.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(published.Fingerprint, revised.PlanHistory.Last().ArtifactEvidence!.Single().Fingerprint);
        Assert.AreEqual(work, revised.Plan.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.AreEqual("keep", revised.Plan.StoragePolicy.SourceCleanup);
        Assert.AreEqual(task.Plan!.Parts.Single().StoragePolicy?.SourceCleanup, revised.Plan.Parts.Single().StoragePolicy?.SourceCleanup);
        Assert.AreEqual(delivery, revised.Plan.DeliveryLayout!.RootDirectory);
        StringAssert.EndsWith(revised.Plan.DeliveryLayout.TaskDirectory, " [R2]");
        Assert.IsTrue(guarded.Contains(work)); Assert.IsTrue(guarded.Contains(revised.Plan.DeliveryLayout.TaskDirectory));
        Assert.IsFalse(Directory.Exists(revised.Plan.DeliveryLayout.TaskDirectory));
        Assert.AreEqual(oldRunPlan, run.PlanSnapshotJson);
        var priorHistory = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(oldHistory!, WorkflowJson.Options)!;
        Assert.AreEqual(JsonSerializer.Serialize(priorHistory[0]), JsonSerializer.Serialize(revised.PlanHistory[0]));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(input.Files.Single()));
        var nodes = AcquisitionPlanCompiler.Compile(revised.Plan).Nodes;
        Assert.IsFalse(nodes.Any(n => n.Kind.StartsWith("download.") || n.Kind.StartsWith("baidu.")));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        var prepared = await plans.PreflightAsync(task.Id, default);
        Assert.AreEqual(JsonSerializer.Serialize(revised.Plan.DeliveryLayout), JsonSerializer.Serialize(prepared.Plan!.DeliveryLayout));
    }

    [TestMethod]
    public async Task Current_storage_changes_only_placement_and_retains_saved_cleanup_and_materialization_preferences()
    {
        var (task, run, _, published, _) = await CompletedInput(true);
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        var oldPolicy = new ArtifactStoragePolicy {ProcessingWorkspaceDirectory = _root,
            CleanupIntermediates = false, Materialization = "copy", SourceCleanup = "quarantine"};
        var partOverride = new ArtifactStoragePolicyOverride {ProcessingWorkspaceDirectory = _root,
            ProcessingDirectoryTemplate = "old-{OperationId}/{Attempt}", CleanupIntermediates = false,
            Materialization = "copy", SourceCleanup = "delete"};
        var stepOverride = partOverride with {ProcessingWorkspaceDirectory = null, SourceCleanup = null};
        var part = task.Plan!.Parts.Single() with {StoragePolicy = partOverride,
            Processing = new() {Requirement = "required", Steps =
                [new() {Id = "s2", Op = "extractArchive", Selector = "*.r", StoragePolicy = stepOverride}]}};
        row!.PlanJson = JsonSerializer.Serialize(task.Plan with {StoragePolicy = oldPolicy, Parts = [part]}, WorkflowJson.Options);
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = oldPolicy}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var work = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName;
        var revised = await CurrentStoragePlans(work, delivery).ReplanAsync(task.Id,
            ContinueInput(task, run.Id, published.Id) with {UseCurrentStorageDefaults = true}, default);
        Assert.AreEqual(work, revised.Plan!.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.IsFalse(revised.Plan.StoragePolicy.CleanupIntermediates);
        Assert.AreEqual("copy", revised.Plan.StoragePolicy.Materialization);
        Assert.AreEqual("quarantine", revised.Plan.StoragePolicy.SourceCleanup);
        foreach (var saved in new[] {revised.Plan.Parts.Single().StoragePolicy!,
                     revised.Plan.Parts.Single().Processing!.Steps.Single().StoragePolicy!})
        {
            Assert.IsNull(saved.ProcessingWorkspaceDirectory); Assert.IsNull(saved.ProcessingDirectoryTemplate);
            Assert.IsNull(saved.DownloadDirectoryTemplate); Assert.AreEqual(false, saved.CleanupIntermediates);
            Assert.AreEqual("copy", saved.Materialization);
        }
        Assert.AreEqual("delete", revised.Plan.Parts.Single().StoragePolicy!.SourceCleanup);
        Assert.IsNull(revised.Plan.Parts.Single().Processing!.Steps.Single().StoragePolicy!.SourceCleanup);
        Assert.AreEqual(delivery, revised.Plan.DeliveryLayout!.RootDirectory);
    }

    [TestMethod]
    public async Task Published_continuation_without_opt_in_keeps_frozen_delivery_defaults()
    {
        var (task, run, _, published, _) = await CompletedInput(true);
        var old = task.Plan!.DeliveryLayout!;
        var revised = await CurrentStoragePlans(Path.Combine(_root, "new-work"), Path.Combine(_root, "new-delivery"))
            .ReplanAsync(task.Id, ContinueInput(task, run.Id, published.Id), default);
        Assert.AreEqual(old.RootDirectory, revised.Plan!.DeliveryLayout!.RootDirectory);
        Assert.AreEqual(old.TaskDirectory + " [R2]", revised.Plan.DeliveryLayout.TaskDirectory);
        Assert.AreEqual(task.Plan!.Parts.Single().StoragePolicy?.SourceCleanup, revised.Plan.Parts.Single().StoragePolicy?.SourceCleanup);
    }

    [DataTestMethod]
    [DataRow("modified")]
    [DataRow("wrong-operation")]
    [DataRow("denied-directory")]
    [DataRow("collision")]
    public async Task Published_continuation_checks_bytes_operation_and_new_directory_before_mutation(string fault)
    {
        var (task, run, _, published, _) = await CompletedInput(true);
        var work = Directory.CreateDirectory(Path.Combine(_root, "new-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "new-delivery")).FullName;
        var plans = CurrentStoragePlans(work, delivery);
        if (fault == "modified") await File.WriteAllTextAsync(published.Files.Single(), "changed");
        if (fault == "wrong-operation") await Db.Set<ArtifactOperationDbModel>().Where(o => o.Id == published.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Kind, "files.process"));
        if (fault == "collision")
        {
            var layout = new AcquisitionDeliveryDirectoryResolver(_services.GetRequiredService<IUserStoragePolicy>())
                .Resolve(task.Id, task.Plan!.Title, task.Plan.OutputDirectory, task.Plan.Parts, new() {RootDirectory = delivery});
            Directory.CreateDirectory(layout.TaskDirectory + " [R2]");
        }
        var request = ContinueInput(task, run.Id, published.Id) with {UseCurrentStorageDefaults = true};
        Exception? failure = null;
        try {await plans.ReplanAsync(task.Id, request, default, _ => fault == "denied-directory"
            ? throw new UnauthorizedAccessException("not allowed") : Task.CompletedTask);}
        catch (Exception error) {failure = error;}
        Assert.IsTrue(failure is ArgumentException or InvalidOperationException or UnauthorizedAccessException,
            "The invalid continuation must fail before mutation: " + failure);
        var current = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(AcquisitionStatus.Completed, current.Status); Assert.AreEqual(1, current.PlanRevision);
        Assert.AreEqual(run.Id, current.WorkflowRunId); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Failed_download_replan_cannot_use_current_storage_to_change_started_download_locations()
    {
        var (task, run, download) = await Failed();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id,
            Input(task, run, download.Id) with {UseCurrentStorageDefaults = true}, default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }
}
