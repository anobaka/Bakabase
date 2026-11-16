using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private async Task<(int TaskId, int RunId, AcquisitionPlanService Plans, string File)> RegisteredDownloadFixture(bool ready = false,
        string? wrapper = null)
    {
        var f = await LocalFilesFixture();
        var run = await _db.WorkflowRuns.SingleAsync();
        var sourceRun = _db.Set<WorkflowRunSourceDbModel>().Where(s => s.WorkflowRunId == run.Id);
        await sourceRun.ExecuteDeleteAsync();
        var item = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        var target = item.Targets.Single();
        var result = await _db.DownloadResults.SingleAsync();
        var files = JsonSerializer.Deserialize<List<string>>(result.FilesJson)!;
        if (wrapper != null)
        {
            var parent = Directory.CreateDirectory(Path.Combine(result.DownloadDirectory, wrapper, "author")).FullName;
            var nested = Path.Combine(parent, Path.GetFileName(files.Single()));
            File.Move(files.Single(), nested);
            files = [nested];
            result.FilesJson = JsonSerializer.Serialize(files);
            result.Fingerprint = (await ArtifactManifestFiles.CaptureAsync(result.DownloadDirectory, files)).Fingerprint;
            await _db.SaveChangesAsync();
        }
        var reference = new WorkflowSourceRef("catalog", "entry", "resource-a", "v1", target.SelectedSourceId);
        var source = target.SelectedSource!;
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        target = await new DownloadArtifactCoordinator(_db, store, new UserStoragePolicy(false),
            _services.GetRequiredService<DownloadResultService>()).RecordAsync(new() {RunId = run.Id, WorkflowDefinitionId = 0,
                TriggerKind = CloudWorkflow.Trigger, Payload = item, ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance},
            target with {DownloadDirectory = result.DownloadDirectory, Files = files}, item.OutputDirectory, default);
        run.PayloadJson = JsonSerializer.Serialize(item with {SourceRefs = [reference]}, WorkflowJson.Options);
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(item with {SourceRefs = [reference], Targets = [target]});
        run.Sources = [new() {Module = reference.Module, EntityKind = reference.EntityKind, EntityId = reference.EntityId,
            Revision = reference.Revision!, ResultKey = reference.ResultKey!}];
        var original = await _db.AcquisitionTasks.SingleAsync();
        original.WorkflowRunId = null;
        var borrower = new AcquisitionTaskDbModel {Title = "Source-linked acquisition", BuilderId = ready ? "prepared" : "draft",
            PlanRevision = 1, Status = AcquisitionStatus.Waiting, Phase = ready ? "ready" : "attention",
            SourceRefsJson = JsonSerializer.Serialize(new[] {reference}, WorkflowJson.Options), PlanJson = ready ? original.PlanJson : null,
            TargetDirectory = _root, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            BuilderStateJson = JsonSerializer.Serialize(new AcquisitionPlanBuilderState {DraftSources = [
                new() {Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId},
                new() {Id = "another-source", Url = "https://example.org/another"}]}, WorkflowJson.Options)};
        _db.AcquisitionTasks.Add(borrower);
        await _db.SaveChangesAsync();
        borrower.SourceRefsJson = JsonSerializer.Serialize(new[] {reference,
            new WorkflowSourceRef("acquisition", "task", borrower.Id.ToString())}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (borrower.Id, run.Id, f.Plans, files.Single());
    }

    [TestMethod]
    public async Task Registered_download_can_borrow_a_completed_producers_receipt_after_its_cursor_was_cleared()
    {
        var f = await RegisteredDownloadFixture();
        await _db.WorkflowRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success)
            .SetProperty(r => r.CurrentItemJson, (string?)null));
        var candidate = (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates.Single();
        var ready = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, candidate.Token), default);
        var part = ready.Plan!.Parts.Single();
        Assert.IsNotNull(await AcquisitionPlanArtifactInputGuard.RequireAsync(_db, _services.GetRequiredService<IArtifactOperationStore>(),
            f.TaskId, part.Id, part.ContinueFrom!, default));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.IsTrue(File.Exists(f.File));
    }

    [TestMethod]
    public async Task Registered_download_single_selection_never_drops_another_required_group()
    {
        var f = await RegisteredDownloadFixture(ready: true);
        var row = await _db.AcquisitionTasks.SingleAsync(t => t.Id == f.TaskId);
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        row.PlanJson = JsonSerializer.Serialize(plan with {Parts = [plan.Parts.Single(), plan.Parts.Single() with {Id = "second"}]}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        Assert.AreEqual(0, (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates.Count);
        Assert.IsTrue(File.Exists(f.File));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Registered_download_shared_transport_requires_a_stopped_producer_from_the_same_origin(bool foreign)
    {
        var f = await RegisteredDownloadFixture();
        var run = await _db.WorkflowRuns.Include(r => r.Sources).SingleAsync();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        var target = workset.Targets.Single() with {Id = "same-source-reused"};
        var origin = run.Sources.Single();
        var reused = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = DateTime.UtcNow,
            CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]}),
            Sources = [new() {Module = origin.Module, EntityKind = origin.EntityKind, EntityId = foreign ? "other-entry" : origin.EntityId,
                Revision = origin.Revision, ResultKey = origin.ResultKey}]};
        _db.WorkflowRuns.Add(reused); await _db.SaveChangesAsync();
        var bindings = new ArtifactOperationBindingService(_db);
        await bindings.BindAsync("workflow", reused.Id.ToString(), target.Id + ":artifact.download", target.DownloadOperationId!);
        await bindings.BindAsync("workflow", reused.Id.ToString(), CloudOperationService.OperationSlot(target, "download"),
            (await _db.CloudDownloadOperations.SingleAsync()).Id);
        var choices = (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates;
        if (foreign) { Assert.AreEqual(0, choices.Count); return; }
        var second = choices.Single(c => c.RunId == reused.Id);
        var prepared = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, second.Token), default);
        Assert.AreEqual(reused.Id, prepared.Plan!.Parts.Single().ContinueFrom!.PreviousRunId);
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("delivery")]
    [DataRow("processing")]
    [DataRow("unrelated-parent")]
    [DataRow("multi-parent")]
    [DataRow("publication-changed")]
    [DataRow("keep-existing-publication")]
    [DataRow("overlapping-publication")]
    [DataRow("stale-publication-on-retry")]
    [DataRow("missing-processed")]
    [DataRow("unbound-output")]
    [DataRow("other-writer")]
    [DataRow("input-fingerprint")]
    public async Task Registered_download_borrows_only_exact_stage_outputs_descending_from_its_download(string mode)
    {
        var f = await RegisteredDownloadFixture();
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var run = await _db.WorkflowRuns.SingleAsync();
        var item = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        var target = item.Targets.Single();
        var raw = (await store.GetArtifactAsync(target.DownloadArtifactId!))!;
        var processing = Directory.CreateDirectory(Path.Combine(_root, "old-processing")).FullName;
        var processedFile = Path.Combine(processing, "unpacked.txt");
        await File.WriteAllTextAsync(processedFile, "already unpacked");
        var converted = await store.AdoptAsync(new("processing-real", "processed", "files.process", raw.Fingerprint),
            new(processing, [processedFile], "processedFiles", true, [raw.Id]), "test");
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "old-delivery")).FullName;
        var finalFile = Path.Combine(delivery, "ready.txt");
        await File.WriteAllTextAsync(finalFile, "already delivered");
        var foreign = await store.AdoptAsync(new("other-download", "other", "download", "another-source"),
            new(processing, [processedFile], "download"), "test");
        var parents = mode switch {"unrelated-parent" => new[] {foreign.Id}, "multi-parent" => [converted.Id, foreign.Id], _ => [converted.Id]};
        var published = await store.AdoptAsync(new("publish-real", "published", "files.publish",
                mode == "input-fingerprint" ? raw.Fingerprint : converted.Fingerprint),
            new(delivery, [finalFile], "publishedFiles", false, parents), "test");
        var bindings = new ArtifactOperationBindingService(_db);
        await bindings.BindAsync("workflow", f.RunId.ToString(), target.Id + ":artifact.process", converted.OperationId);
        if (mode != "unbound-output")
            await bindings.BindAsync("workflow", f.RunId.ToString(), target.Id + ":artifact.publish", published.OperationId);
        var final = target with {ProcessedArtifactId = converted.Id, ProcessingOperationId = converted.OperationId, ProcessedDirectory = processing,
            PublishedArtifactId = published.Id, PublicationOperationId = published.OperationId, PublishedDirectory = delivery, PublicationStatus = "delivered"};
        run.Status = WorkflowRunStatus.Success; run.CurrentItemJson = null;
        run.PayloadJson = JsonSerializer.Serialize(item with {Targets = [target with {SelectedSourceId = null, AccountId = null}]}, WorkflowJson.Options);
        run.OutputItemsJson = JsonSerializer.Serialize(new[] {item with {Targets = [final]}}, WorkflowJson.Options);
        if (mode == "other-writer") _db.DownloadTasks.Add(new() {DownloadPath = delivery, Status = DownloadTaskDbModelStatus.InProgress});
        await _db.SaveChangesAsync();
        if (mode == "missing-processed") File.Delete(processedFile);
        var choices = (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates;
        Assert.IsTrue(choices.Any(c => c.Stage == "download"));
        Assert.AreEqual(mode != "missing-processed", choices.Any(c => c.Stage == "processing"));
        if (mode is "other-writer" or "unbound-output" or "multi-parent" or "unrelated-parent" or "input-fingerprint")
        {
            Assert.IsFalse(choices.Any(c => c.Stage == "delivery")); return;
        }
        Assert.AreEqual("delivery", choices[0].Stage);
        var chosen = choices.Single(c => c.Stage == (mode == "processing" ? "processing" : "delivery"));
        if (mode == "publication-changed")
        {
            await File.WriteAllTextAsync(finalFile, new string('X', (int)new FileInfo(finalFile).Length));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, chosen.Token), default));
            return;
        }
        var result = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, chosen.Token), default);
        var part = result.Plan!.Parts.Single();
        var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(_db, store, f.TaskId, part.Id, part.ContinueFrom!, default);
        Assert.AreEqual(mode == "processing" ? converted.Id : published.Id, input.Id);
        Assert.AreEqual(mode == "processing" ? processedFile : finalFile, input.Files.Single());
        if (mode is "keep-existing-publication" or "overlapping-publication" or "stale-publication-on-retry")
            await VerifyPublishedHandoff(f.Plans, f.TaskId, f.RunId, result.Task.PlanRevision, input, mode);

        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    private async Task VerifyPublishedHandoff(AcquisitionPlanService plans, int taskId, int producerId, int revision,
        ArtifactRecord publication, string mode)
    {
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var kept = await plans.KeepPreparedFilesAsync(taskId, new(revision), default);
        var started = await plans.RunAsync(taskId, kept.Task.PlanRevision, default);
        var run = await _db.WorkflowRuns.SingleAsync(r => r.Id == started.Task.WorkflowRunId);
        var frozen = WorkflowRunPlans.Read(run.PlanSnapshotJson)!;
        var item = JsonSerializer.Deserialize<DownloadWorkset>(run.PayloadJson!, WorkflowJson.Options)!;
        WorkflowExecutionContext Context(int step) => new()
        {
            RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = item,
            Services = _services, Logger = NullLogger.Instance, ActivityConfigJson = frozen.Nodes[step].ConfigJson,
            FollowingActivities = frozen.Nodes.Skip(step + 1).Select(n => new WorkflowPlannedActivity(n.Kind, n.ConfigJson)).ToArray()
        };
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), item, default);
        item = (DownloadWorkset)attached.Replacement!;
        var current = item.Targets.Single();
        var directories = current.StageDirectories!;
        var stage = directories.Processing!.Directory;
        var destination = directories.Delivery!.Directory;
        Directory.CreateDirectory(stage);
        var marker = Path.Combine(stage, "owned-by-another-task.txt");
        await File.WriteAllTextAsync(marker, "leave unchanged");
        _db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Waiting,
            CurrentItemJson = JsonSerializer.Serialize(new {input = stage})});
        var consumer = AcquisitionPlanArtifactInputGuard.Consumer(run.Id, current.Id);
        await store.ReleaseConsumerAsync(publication.OperationId, consumer);
        run.Status = WorkflowRunStatus.Failed;
        run.ErrorMessage = "STAGE_COMMIT_REFERENCED"; run.CurrentStepIndex = 1;
        if (mode == "overlapping-publication")
        {
            var binding = directories.Delivery with {Directory = publication.RootDirectory};
            var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(frozen.Nodes[2].ConfigJson, WorkflowJson.Options)!;
            var node = frozen.Nodes[2] with {ConfigJson = JsonSerializer.Serialize(config with
                {TargetDirectories = new() {[current.Id] = publication.RootDirectory}}, WorkflowJson.Options)};
            frozen = frozen with {Nodes = [.. frozen.Nodes.Take(2), node]};
            run.PlanSnapshotJson = WorkflowRunPlans.Serialize(frozen);
            item = item with {Targets = [current with {StageDirectories = directories with {Delivery = binding}}]};
        }
        if (mode == "stale-publication-on-retry") await File.WriteAllTextAsync(publication.Files.Single(), "altered delivery");
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(item);
        await _db.SaveChangesAsync();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false), store,
            workspaces: _services.GetRequiredService<FileProcessingWorkspaceResolver>());
        var row = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var recovery = await diagnostics.DiagnoseAsync(row, run, default);
        if (mode != "keep-existing-publication")
        {
            Assert.IsFalse(recovery.Row.CanContinue);
            Assert.AreEqual(mode == "overlapping-publication" ? "RECOVERY_PROCESSING_FILES_REFERENCED" : "RECOVERY_FILES_UNVERIFIED", recovery.Row.ReasonCode);
            return;
        }
        Assert.AreEqual("RECOVERY_STAGE_INPUT_READY", recovery.Row.ReasonCode); Assert.AreEqual("retry", recovery.Row.Action);
        run.Status = WorkflowRunStatus.Running; await _db.SaveChangesAsync();
        var forwarded = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), item, default);
        Assert.IsNull(forwarded.Suspension);
        var processed = ((DownloadWorkset)forwarded.Replacement!).Targets.Single();
        Assert.AreEqual(publication.Id, processed.ProcessedArtifactId);
        Assert.AreEqual(publication.RootDirectory, processed.ProcessedDirectory);
        Assert.IsFalse(await _db.ArtifactOperationBindings.AnyAsync(b => b.OwnerId == run.Id.ToString() &&
            b.OperationId == publication.OperationId && b.Slot.Contains(":process:")));
        Assert.IsTrue(await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().AnyAsync(c => c.OperationId == publication.OperationId &&
            c.ConsumerKey == consumer && c.ReleasedAt == null));
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(2), forwarded.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var output = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.AreEqual(destination, output.PublishedDirectory);
        Assert.AreEqual("already delivered", await File.ReadAllTextAsync(output.PublishedFiles!.Single()));
        Assert.AreEqual("already delivered", await File.ReadAllTextAsync(publication.Files.Single()));
        CollectionAssert.AreEqual(new[] {marker}, Directory.GetFiles(stage));
        Assert.IsTrue(await _db.ArtifactOperationBindings.AnyAsync(b => b.OwnerId == producerId.ToString() &&
            b.OperationId == publication.OperationId && b.Slot.EndsWith(":artifact.publish")));
        Assert.IsFalse((await AcquisitionTaskCleanupViews.OwnedClosuresAsync(_db, [publication.Id], [run.Id.ToString()], default)).Contains(publication.Id));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Registered_download_explicitly_prepares_existing_input_without_another_transfer_or_source_ownership(bool ready)
    {
        var f = await RegisteredDownloadFixture(ready);
        var before = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        var preview = await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default);
        var selected = preview.Candidates.Single();
        Assert.AreEqual(1, selected.FileCount);
        Assert.AreEqual(new FileInfo(f.File).Length, selected.TotalBytes);
        var result = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, selected.Token), default);
        var replay = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, selected.Token), default);
        Assert.AreEqual(result.Task.PlanRevision, replay.Task.PlanRevision);
        Assert.AreEqual("ready", result.Task.Phase);
        Assert.IsNull(result.Task.WorkflowRunId);
        var part = result.Plan!.Parts.Single();
        Assert.IsTrue(AcquisitionLocalFilesRecoveryService.LocalOnly(result.Plan));
        Assert.IsTrue(await f.Plans.CanAnalyzePreparedFilesAsync(await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId), default));
        Assert.IsFalse(await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(_db, f.RunId, f.TaskId, default));
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(_db, store, f.TaskId, part.Id, part.ContinueFrom!, default);
        CollectionAssert.AreEqual(new[] {f.File}, input.Files.ToArray());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AcquisitionPlanArtifactInputGuard.RequireAsync(_db, store,
            f.TaskId, "other-part", part.ContinueFrom!, default));
        Assert.AreEqual(before.CurrentItemJson, (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentItemJson);
        var started = await f.Plans.RunAsync(f.TaskId, result.Task.PlanRevision, default);
        Assert.IsNotNull(started.Task.WorkflowRunId);
        var borrowing = await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == started.Task.WorkflowRunId);
        var frozen = WorkflowRunPlans.Read(borrowing.PlanSnapshotJson)!;
        Assert.IsFalse(frozen.Nodes.Any(n => n.Kind.StartsWith("download.") || n.Kind.EndsWith(".transfer")));
        var workset = JsonSerializer.Deserialize<DownloadWorkset>(borrowing.PayloadJson!, WorkflowJson.Options)!;
        await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(new() {RunId = borrowing.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = frozen.Nodes[0].ConfigJson,
            Services = _services, Logger = NullLogger.Instance}, workset, default);
        Assert.IsTrue(await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(_db, borrowing.Id, part.Id, input.OperationId, default));
        // Even after both readers stop and their leases expire, the original owner's binding
        // prevents deletion of its download by the acquiring task.
        await _db.WorkflowRuns.ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        Assert.IsFalse((await AcquisitionTaskCleanupViews.OwnedClosuresAsync(_db, [input.Id], [borrowing.Id.ToString()], default)).Contains(input.Id));
        // Losing the producer's binding must never turn the borrower's read receipt into ownership.
        await _db.ArtifactOperationBindings.Where(b => b.OwnerId == f.RunId.ToString() && b.OperationId == input.OperationId).ExecuteDeleteAsync();
        var storage = new UserStoragePolicy(false);
        var extraction = _services.GetRequiredService<IArchiveExtractionService>();
        var workspace = new FileProcessingWorkspaceResolver(_root, storage);
        var cleanup = new AcquisitionTaskDeletionCleanupService(_db, store, new(_db, store, new(extraction), new(extraction), _root, workspace),
            new(_db, store, new ArchiveQuarantineService(store, _root, storage)), storage, workspaces: workspace);
        var borrower = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId);
        var files = await cleanup.PreviewAsync(borrower, [borrowing.Id], true, true, default, deleteDeliveredFiles: true);
        Assert.IsNotNull(files.Consent);
        Assert.IsFalse(files.Summary.Groups.Any(g => g.ArtifactId == "task-artifact:" + input.Id ||
            g.Files.Any(file => file.Path == f.File)));
        await cleanup.ApplyAsync(borrower, files, default);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual("verified existing bytes", await File.ReadAllTextAsync(f.File));
    }

    [DataTestMethod]
    [DataRow("changed-bytes")]
    [DataRow("run-resumed")]
    [DataRow("different-result")]
    [DataRow("another-acquisition")]
    [DataRow("different-origin")]
    [DataRow("download-running")]
    [DataRow("another-writer")]
    [DataRow("transport-changed")]
    public async Task Registered_download_refuses_stale_or_foreign_inputs_and_leaves_the_draft_intact(string change)
    {
        var f = await RegisteredDownloadFixture();
        var candidate = (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates.Single();
        switch (change)
        {
            case "changed-bytes": await File.WriteAllTextAsync(f.File, new string('x', (int)new FileInfo(f.File).Length)); break;
            case "run-resumed": await _db.WorkflowRuns.ExecuteUpdateAsync(set => set.SetProperty(r => r.PendingSignalJson, "{}")); break;
            case "different-result": await _db.DownloadResults.ExecuteUpdateAsync(set => set.SetProperty(r => r.Fingerprint, "changed")); break;
            case "another-acquisition":
                _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = f.RunId, Module = "acquisition", EntityKind = "task", EntityId = "99999"});
                await _db.SaveChangesAsync(); break;
            case "different-origin": await _db.Set<WorkflowRunSourceDbModel>().ExecuteUpdateAsync(set => set.SetProperty(s => s.Revision, "new")); break;
            case "download-running": await _db.DownloadTasks.ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, DownloadTaskDbModelStatus.InProgress)); break;
            case "another-writer":
                _db.DownloadTasks.Add(new() {DownloadPath = Path.GetDirectoryName(f.File)!, Status = DownloadTaskDbModelStatus.InProgress});
                await _db.SaveChangesAsync(); break;
            case "transport-changed": await _db.CloudDownloadOperations.ExecuteUpdateAsync(set => set.SetProperty(o => o.RequestJson, "{}")); break;
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, candidate.Token), default));
        var row = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId);
        Assert.IsNull(row.PlanJson); Assert.IsNull(row.WorkflowRunId); Assert.AreEqual(1, row.PlanRevision); Assert.IsTrue(File.Exists(f.File));
    }
}
