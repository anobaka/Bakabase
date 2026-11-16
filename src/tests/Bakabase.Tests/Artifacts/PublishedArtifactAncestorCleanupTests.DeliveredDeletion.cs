using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Old, ArtifactRecord Latest,
        AcquisitionTaskDeletionService Service)> DeliveryVersionsFixture(bool samePath = false)
    {
        var f = await DeletionFixture();
        ArtifactRecord latest;
        if (samePath)
        {
            latest = await store.AdoptAsync(new("new-publication", "new-publication", "files.publish", f.Source.Fingerprint),
                new(f.Final.RootDirectory, f.Final.Files, "publishedFiles", false, [f.Source.Id]), "fixture");
        }
        else latest = await Artifact("publishedFiles", f.Source, owner: 2);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "part:artifact.publish", latest.OperationId));
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.WorkflowRunId, (int?)2).SetProperty(t => t.PlanRevision, 2));
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, null);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage());
        return (task, f.Source, f.Final, latest, new(db, new FinishedDeletionActivity([1, 2]), new(), files));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_final_deletion_includes_owned_historical_versions_and_releases_their_input_reference(bool retainedRetry)
    {
        var f = await DeliveryVersionsFixture();
        if (retainedRetry) await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, (DateTime?)DateTime.UtcNow));
        var before = await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true,
            RetryRetainedFiles: retainedRetry, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.AreEqual(2, groups.Count(g => g.Kind == "publishedFiles"));
        Assert.IsTrue(groups.All(g => g.Eligible), string.Join(",", groups.Select(g => g.ReasonCode)));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: retainedRetry));
        Assert.AreEqual(3, result.RemovedFiles);
        Assert.IsFalse(File.Exists(f.Source.Files.Single())); Assert.IsFalse(File.Exists(f.Old.Files.Single()));
        Assert.IsFalse(File.Exists(f.Latest.Files.Single()));
        if (retainedRetry) Assert.AreEqual(before, await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        Assert.IsNotNull(await store.GetArtifactAsync(f.Old.Id));
    }

    [TestMethod]
    public async Task Historical_delivery_not_selected_for_final_deletion_remains_protected()
    {
        var f = await DeliveryVersionsFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Kind == "publishedFiles"));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.ReasonCode == "ReferencedDescendant"));
        Assert.IsTrue(File.Exists(f.Old.Files.Single())); Assert.IsTrue(File.Exists(f.Latest.Files.Single()));
    }

    [TestMethod]
    public async Task Historical_publications_at_same_path_are_fixed_once_and_deleted_idempotently()
    {
        var f = await DeliveryVersionsFixture(samePath: true);
        var unknown = Path.Combine(f.Old.RootDirectory, "manual.key"); await File.WriteAllTextAsync(unknown, "preserve");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        Assert.AreEqual(2, preview.Rows.Single().Cleanup!.Groups.Count);
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.All(g => g.Eligible));
        Assert.AreEqual(1, preview.CleanupFileCount);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.RemovedFiles); Assert.AreEqual(0, result.CleanupResults!.Single().ReasonCodes.Count);
        Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Same_path_publication_with_foreign_owner_protects_every_selected_version()
    {
        var f = await DeliveryVersionsFixture(samePath: true);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "3", "borrowed-history", f.Old.OperationId));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        Assert.AreEqual(2, preview.Rows.Single().Cleanup!.Groups.Count);
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.All(g => !g.Eligible));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Old.Files.Single())); Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    [TestMethod]
    [DataRow("foreign-owner")]
    [DataRow("foreign-descendant")]
    [DataRow("active-reader")]
    public async Task Historical_publication_release_never_bypasses_foreign_or_active_references(string scenario)
    {
        var f = await DeliveryVersionsFixture();
        if (scenario == "foreign-owner") Assert.IsTrue(await new ArtifactOperationBindingService(db)
            .BindAsync("workflow", "3", "borrowed-history", f.Old.OperationId));
        if (scenario == "foreign-descendant") await Artifact("processedFiles", f.Old, owner: 3);
        if (scenario == "active-reader") Assert.IsTrue(await store.AttachConsumerAsync(f.Old.OperationId, "external-reader", TimeSpan.FromHours(1)));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId == f.Source.Id).Eligible);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.IsTrue(File.Exists(f.Source.Files.Single())); Assert.IsTrue(File.Exists(f.Old.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Final_deletion_keeps_publication_root_and_unknown_directories(bool unknown)
    {
        var f = await DeletionFixture();
        if (unknown) Directory.CreateDirectory(Path.Combine(f.Final.RootDirectory, "manual-empty-folder"));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory)); Assert.IsTrue(Directory.Exists(root));
        if (unknown) Assert.IsTrue(Directory.Exists(Path.Combine(f.Final.RootDirectory, "manual-empty-folder")));
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Final_deletion_prunes_only_registered_empty_parent_chain_inside_publication_root()
    {
        var f = await DeletionFixture();
        var nested = Path.Combine(f.Final.RootDirectory, "nested", "inner"); Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "content.bin"); File.Move(f.Final.Files.Single(), file);
        var publication = await store.AdoptAsync(new("nested-publication", "nested-publication", "files.publish", f.Source.Fingerprint),
            new(f.Final.RootDirectory, [file], "publishedFiles", false, [f.Source.Id]), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested-output:artifact.publish", publication.OperationId));
        var unknown = Path.Combine(f.Final.RootDirectory, "manual-empty-folder"); Directory.CreateDirectory(unknown);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory)); Assert.IsTrue(Directory.Exists(unknown));
        Assert.IsFalse(Directory.Exists(Path.Combine(f.Final.RootDirectory, "nested")));
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    private sealed class CleanupDiagnosticLogger : ILogger<AcquisitionTaskDeletionCleanupService>
    {
        internal readonly List<Exception> Errors = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception != null) Errors.Add(exception); }
    }

    [TestMethod]
    [DataRow("plan-valid")]
    [DataRow("builder-valid")]
    [DataRow("plan-unknown")]
    [DataRow("builder-unknown")]
    [DataRow("plan-concrete")]
    [DataRow("builder-concrete")]
    [DataRow("plan-wrong-task")]
    [DataRow("builder-wrong-task")]
    [DataRow("plan-input")]
    [DataRow("builder-input")]
    public async Task Final_deletion_distinguishes_shared_delivery_allocation_from_concrete_foreign_paths(string scenario)
    {
        var f = await DeletionFixture();
        var other = new AcquisitionTaskDbModel {Title = "Other task", Status = AcquisitionStatus.Waiting};
        db.AcquisitionTasks.Add(other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var plan = AllocationPlan();
        var layout = new AcquisitionDeliveryDirectoryResolver(new Storage()).Resolve(other.Id, "Other task", root,
            plan.Parts, new() {RootDirectory = root});
        if (scenario.EndsWith("concrete")) layout = layout with {TaskDirectory = f.Final.RootDirectory,
            PartDirectories = new() {["part"] = f.Final.RootDirectory}};
        if (scenario.EndsWith("wrong-task")) layout = layout with {TaskId = other.Id + 1};
        var rawLayout = JsonSerializer.SerializeToNode(layout, Json)!;
        if (scenario.EndsWith("unknown")) rawLayout["unknown"] = root;
        var planNode = JsonSerializer.SerializeToNode(plan, Json)!;
        var builder = new System.Text.Json.Nodes.JsonObject();
        var target = scenario.StartsWith("builder") ? builder : planNode;
        target["deliveryLayout"] = rawLayout;
        if (scenario.EndsWith("input")) target["inputDirectory"] = root;
        var planJson = planNode.ToJsonString(); var builderJson = builder.ToJsonString();
        await db.AcquisitionTasks.Where(t => t.Id == other.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.PlanJson, planJson).SetProperty(t => t.BuilderStateJson, builderJson));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        var selected = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual(scenario.EndsWith("valid"), selected.Eligible, selected.ReasonCode);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Unreadable_private_workspace_does_not_discard_independent_final_deletion_and_logs_the_cause()
    {
        var f = await DeletionFixture();
        var brokenRoot = Path.Combine(root, "broken-checkpoint", "result"); Directory.CreateDirectory(brokenRoot);
        var retained = Path.Combine(brokenRoot, "retained.bin"); await File.WriteAllTextAsync(retained, "preserve");
        var broken = await store.AdoptAsync(new("broken-plan", "broken-plan", "files.process", "fixture"),
            new(brokenRoot, [retained], "processedFiles", true), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "broken:artifact.process", broken.OperationId));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(brokenRoot)!, "state.json"), "not-json");
        var logger = new CleanupDiagnosticLogger(); var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, null);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(), logger: logger);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(), files);
        var preview = await service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Single(g => g.Kind == "publishedFiles").Eligible);
        var blocked = groups.Where(g => g.ArtifactId == broken.Id).ToArray();
        CollectionAssert.AreEquivalent(new[] {"intermediate", "processedFiles"}, blocked.Select(g => g.Kind).ToArray());
        Assert.IsTrue(blocked.All(g => !g.Eligible && g.SizeUnknown));
        Assert.IsTrue(blocked.All(g => g.ReasonCode == "WorkspaceUnavailable"));
        Assert.IsTrue(logger.Errors.Any(e => e is JsonException), "Retain the diagnostic exception, not only a generic failure code.");
        var result = await service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.IsFalse(File.Exists(f.Final.Files.Single())); Assert.IsTrue(File.Exists(retained));
        Assert.IsTrue(result.CleanupResults!.Single().ReasonCodes.Contains("WorkspaceUnavailable"));
    }

    [TestMethod]
    public async Task Final_delivery_deletion_defaults_off_even_when_intermediate_checks_are_skipped()
    {
        var f = await DeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        Assert.IsFalse(preview.DeleteDeliveredFiles);
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Kind == "publishedFiles"));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_final_deletion_is_independent_and_preserves_unregistered_files_and_history(bool intermediates)
    {
        var f = await DeletionFixture();
        var unknown = Path.Combine(f.Final.RootDirectory, "manual.key");
        await File.WriteAllTextAsync(unknown, "keep user addition");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], intermediates, true, true, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.DeleteDeliveredFiles);
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Kind == "publishedFiles");
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        Assert.AreEqual(f.Final.Files.Single(), group.Files.Single().Path);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(intermediates ? 2 : 1, result.RemovedFiles);
        Assert.AreEqual(1, result.DeletedTaskIds.Count);
        Assert.AreEqual(!intermediates, File.Exists(f.Source.Files.Single()));
        Assert.IsFalse(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(unknown));
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory));
        Assert.AreEqual(f.Final.Fingerprint, (await store.GetArtifactAsync(f.Final.Id))!.Fingerprint);
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json")
            .Select(path => JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(File.ReadAllText(path), Json)!).ToArray();
        Assert.IsTrue(receipts.Any(r => r.ArtifactId == f.Final.Id && r.Consent?.DeleteDeliveredFiles == true && r.RemovedFiles == 1));
        Assert.AreEqual(result, await f.Service.DeleteAsync(new(preview.PreviewId)));
    }

    [TestMethod]
    public async Task Final_deletion_requires_explicit_skip_acceptance_or_a_matching_detailed_preview()
    {
        var f = await DeletionFixture();
        var taskOnly = await f.Service.PreviewAsync(new([f.Task.Id]));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(new(taskOnly.PreviewId, DeleteDeliveredFiles: true)));
        var intermediatesOnly = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(new(intermediatesOnly.PreviewId, DeleteDeliveredFiles: true)));
        var explicitFinal = await f.Service.PreviewAsync(new([f.Task.Id], DeleteDeliveredFiles: true));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(new(explicitFinal.PreviewId, DeleteDeliveredFiles: false)));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(new(explicitFinal.PreviewId, CleanupFiles: true)));
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Skipped_final_deletion_can_confirm_task_scope_without_a_second_file_inventory(bool intermediates)
    {
        var f = await DeletionFixture();
        var unknown = Path.Combine(f.Final.RootDirectory, "manual.key");
        await File.WriteAllTextAsync(unknown, "keep unregistered user content");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id]));
        Assert.IsNull(preview.Rows.Single().Cleanup);
        Assert.IsFalse(preview.DeleteDeliveredFiles);
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, CleanupFiles: intermediates,
            AcceptDeliveredFiles: true, SkipFileVerification: true, DeleteDeliveredFiles: true);
        var result = await f.Service.DeleteAsync(input);
        Assert.AreEqual(intermediates ? 2 : 1, result.RemovedFiles);
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id));
        Assert.AreEqual(!intermediates, File.Exists(f.Source.Files.Single()));
        Assert.IsFalse(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(unknown));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        Assert.AreEqual(result, await f.Service.DeleteAsync(input));
    }

    [TestMethod]
    [DataRow("registered-file")]
    [DataRow("resource")]
    [DataRow("active-consumer")]
    public async Task Skipped_task_scope_still_checks_new_references_before_removing_final_files(string scenario)
    {
        var f = await DeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id]));
        if (scenario == "registered-file")
            await store.AdoptAsync(new("foreign-file", "foreign-file", "files.process", "fixture"),
                new(f.Final.RootDirectory, f.Final.Files, "processedFiles", true), "foreign-owner");
        if (scenario == "resource")
        {
            db.ResourcesV2.Add(new() {Path = f.Final.Files.Single(), IsFile = true});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (scenario == "active-consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(f.Final.OperationId, "another-reader", TimeSpan.FromHours(1)));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, AcceptDeliveredFiles: true,
            SkipFileVerification: true, DeleteDeliveredFiles: true));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow("before-preview")]
    [DataRow("after-preview")]
    [DataRow("missing-root")]
    public async Task Explicit_final_deletion_is_idempotent_for_confirmed_missing_files(string scenario)
    {
        var f = await DeletionFixture();
        if (scenario == "before-preview") File.Delete(f.Final.Files.Single());
        if (scenario == "missing-root") Directory.Delete(f.Final.RootDirectory, true);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], DeleteDeliveredFiles: true));
        if (scenario == "after-preview") File.Delete(f.Final.Files.Single());
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count, string.Join(",", result.CleanupResults!.SelectMany(r => r.ReasonCodes)));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Final_files_replaced_after_preview_are_protected_even_with_skipped_content_checks(bool skip)
    {
        var f = await DeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, skip, skip, DeleteDeliveredFiles: true));
        await File.WriteAllTextAsync(f.Final.Files.Single(), "replacement");
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual("replacement", await File.ReadAllTextAsync(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Final_content_checks_detect_preexisting_same_length_edits_without_hashing_at_preview(bool skip)
    {
        var f = await DeletionFixture();
        await File.WriteAllTextAsync(f.Final.Files.Single(), "edit bytes"); // same length as "same bytes"
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], AcceptDeliveredFiles: skip, SkipFileVerification: skip, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single().Eligible);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(skip ? 1 : 0, result.RemovedFiles);
        if (!skip) Assert.AreEqual("edit bytes", await File.ReadAllTextAsync(f.Final.Files.Single()));
        else Assert.IsFalse(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow("binding")]
    [DataRow("registered-file")]
    [DataRow("task-path")]
    [DataRow("resource-path")]
    [DataRow("active-consumer")]
    [DataRow("task-running")]
    public async Task Explicit_final_deletion_still_protects_foreign_references_and_active_work(string scenario)
    {
        var f = await DeletionFixture();
        if (scenario == "binding") Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", f.Final.OperationId));
        if (scenario == "registered-file")
            await store.AdoptAsync(new("foreign-file", "foreign-file", "files.process", "fixture"),
                new(f.Final.RootDirectory, f.Final.Files, "processedFiles", true), "foreign-owner");
        if (scenario == "task-path")
        {
            db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Title = "another task", Status = AcquisitionStatus.Waiting,
                PlanJson = JsonSerializer.Serialize(new {inputDirectory = f.Final.RootDirectory}, Json)});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (scenario == "resource-path")
        {
            db.ResourcesV2.Add(new() {Path = f.Final.Files.Single(), IsFile = true});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (scenario == "active-consumer") Assert.IsTrue(await store.AttachConsumerAsync(f.Final.OperationId, "other-reader", TimeSpan.FromHours(1)));
        if (scenario == "task-running") await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AcquisitionStatus.Running));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], DeleteDeliveredFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup?.Groups.Any(g => g.Eligible) == true);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles); Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Final_deletion_rechecks_references_added_after_preview()
    {
        var f = await DeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], DeleteDeliveredFiles: true));
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "late-borrower", f.Final.OperationId));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles); Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Final_deletion_releases_only_its_own_proven_current_directory_views(bool foreignBinding)
    {
        var f = await CreateCurrentFilesDeletionFixture();
        var view = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, store, new Storage(), null,
            f.Task.Id, 2, 1, "part", f.Final, null, default);
        if (foreignBinding) Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed-final-view", view.OperationId));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], DeleteDeliveredFiles: true));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(foreignBinding ? 0 : 1, result.RemovedFiles,
            string.Join(",", result.CleanupResults!.SelectMany(r => r.ReasonCodes)));
        Assert.AreEqual(foreignBinding, File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(f.Raw.Files.Single())); Assert.IsTrue(File.Exists(f.Processed.Files.Single()));
        Assert.IsNotNull(await store.GetArtifactAsync(view.Id));
    }
}
