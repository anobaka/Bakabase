using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Acquisition.Planning;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private string root = null!;
    private BakabaseDbContext db = null!;
    private ArtifactOperationStore store = null!;
    private PublishedArtifactAncestorCleanup cleanup = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize]
    public async Task Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "published-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "fixture.db")).Options);
        await db.Database.EnsureCreatedAsync();
        store = new(db, () => root);
        cleanup = new(db, store, new ArchiveQuarantineService(store, root, new Storage()));
        db.WorkflowRuns.AddRange(new WorkflowRunDbModel {Id = 1, Status = WorkflowRunStatus.Success},
            new WorkflowRunDbModel {Id = 2, Status = WorkflowRunStatus.Success},
            new WorkflowRunDbModel {Id = 3, Status = WorkflowRunStatus.Waiting});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    [TestCleanup]
    public async Task Cleanup() {await db.DisposeAsync(); Directory.Delete(root, true);}

    private async Task<ArtifactRecord> Artifact(string kind, ArtifactRecord? parent = null, bool? cleanupPolicy = true,
        string sourceCleanup = "keep", int owner = 1, string contents = "same bytes", string filename = "content.bin")
    {
        var id = Guid.NewGuid().ToString("N"); var dir = Path.Combine(root, id); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, filename); await File.WriteAllTextAsync(file, contents);
        var metadata = kind == "processedFiles" ? JsonSerializer.Serialize(new FileArtifactProcessingMetadata(parent?.Id,
            "{\"requirement\":\"notRequired\",\"steps\":[]}", null,
            StoragePolicy: cleanupPolicy == null ? null : new() {CleanupIntermediates = cleanupPolicy.Value, SourceCleanup = sourceCleanup}), Json) : null;
        var artifact = await store.AdoptAsync(new(id, id, kind == "publishedFiles" ? "files.publish" : kind == "download" ? "download" : "files.process", "fixture"),
            new(dir, [file], kind, kind != "publishedFiles", parent == null ? [] : [parent.Id], metadata), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", owner.ToString(), id, artifact.OperationId));
        return artifact;
    }
    private Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> Preview(ArtifactRecord output, params int[] owners) =>
        cleanup.PreviewAsync([output.Id], owners.Length == 0 ? [1] : owners, default);

    [TestMethod]
    public async Task Nested_registered_checkpoints_retire_after_delivery_while_formal_files_unknown_siblings_and_history_remain()
    {
        var raw = await Artifact("download"); var first = await Artifact("processedFiles", raw);
        var second = await Artifact("processedFiles", first); var final = await Artifact("publishedFiles", second);
        var unknown = Path.Combine(first.RootDirectory, "unknown.key"); await File.WriteAllTextAsync(unknown, "keep me");
        var state = Path.Combine(first.RootDirectory, "state.json"); await File.WriteAllTextAsync(state, "history");
        var rows = await Preview(final);
        CollectionAssert.AreEqual(new[] {second.Id, first.Id}, rows.Where(r => r.Eligible).Select(r => r.ArtifactId).ToArray());
        foreach (var row in rows.Where(r => r.Eligible)) Assert.IsTrue((await cleanup.ReclaimAsync(row, [1], default)).Completed);
        Assert.IsTrue(File.Exists(raw.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        Assert.IsFalse(File.Exists(first.Files[0])); Assert.IsFalse(File.Exists(second.Files[0]));
        Assert.AreEqual("keep me", await File.ReadAllTextAsync(unknown)); Assert.AreEqual("history", await File.ReadAllTextAsync(state));
        Assert.AreEqual(first.Manifest.Fingerprint, (await store.GetArtifactAsync(first.Id))!.Manifest.Fingerprint);
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(first.Id))!.State);
        Assert.AreEqual("AlreadyRetired", (await cleanup.ReclaimAsync(rows.First(r => r.ArtifactId == first.Id), [1], default)).Reason);
        Assert.AreEqual(2, Directory.GetFiles(Path.Combine(root, "file-processing", "quarantine-receipts"), "*.json").Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(null)]
    public async Task Keep_or_legacy_missing_policy_does_not_authorize_registered_snapshot_deletion(bool? keep)
    {
        var source = await Artifact("processedFiles", cleanupPolicy: keep); var final = await Artifact("publishedFiles", source);
        var row = (await Preview(final)).Single(); Assert.AreEqual("RetentionPolicyKeep", row.Reason); Assert.IsFalse(row.Eligible);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [TestMethod]
    public async Task Uncovered_key_and_changed_delivery_block_retirement()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source, contents: "other bytes");
        Assert.AreEqual(ArtifactFileCoverage.IncompleteCode, (await Preview(final)).Single().Reason);
        var anotherSource = await Artifact("processedFiles");
        var covered = await Artifact("publishedFiles", anotherSource); var row = (await Preview(covered)).Single();
        await File.WriteAllTextAsync(covered.Files[0], "modified");
        Assert.IsFalse((await cleanup.ReclaimAsync(row, [1], default)).Completed);
        Assert.IsTrue(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(anotherSource.Files[0]));
        Assert.AreEqual("modified", await File.ReadAllTextAsync(covered.Files[0]));
    }

    [TestMethod]
    public async Task Completed_shared_owner_and_waiting_owner_are_protected_even_without_consumer_leases()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", source.OperationId));
        Assert.AreEqual("SharedOwner", (await Preview(final)).Single().Reason);
        Assert.IsTrue((await Preview(final, 1, 2)).Single().Eligible);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "3", "borrowed", source.OperationId));
        Assert.AreEqual("ActiveOwner", (await Preview(final, 1, 2, 3)).Single().Reason);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [TestMethod]
    public async Task Shared_owner_added_after_preview_is_checked_again_before_deletion()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var row = (await Preview(final)).Single(); Assert.IsTrue(row.Eligible);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", source.OperationId));
        Assert.AreEqual("SharedOwner", (await cleanup.ReclaimAsync(row, [1], default)).Reason);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [TestMethod]
    public async Task Raw_downloads_require_explicit_delete_policy_and_preserved_publication()
    {
        var raw = await Artifact("download"); var step = await Artifact("processedFiles", raw, sourceCleanup: "delete");
        var final = await Artifact("publishedFiles", step); var rows = (await Preview(final)).Where(r => r.Eligible).ToArray();
        Assert.AreEqual(2, rows.Length);
        foreach (var row in rows) Assert.IsTrue((await cleanup.ReclaimAsync(row, [1], default)).Completed);
        Assert.IsFalse(File.Exists(raw.Files[0])); Assert.IsFalse(File.Exists(step.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        Assert.AreEqual(final.Id, (await store.ResolveByIdAsync(final.Id))!.Id);
    }

    [DataTestMethod]
    [DataRow("keep")]
    [DataRow("quarantine")]
    public async Task Explicit_nested_source_retention_overrides_root_delete_even_with_complete_file_coverage(string retention)
    {
        var raw = await Artifact("download"); var first = await Artifact("processedFiles", raw, sourceCleanup: "delete");
        var nested = await Artifact("processedFiles", first, sourceCleanup: "delete");
        var final = await Artifact("publishedFiles", nested);
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = [new()
            {Id = "keep-input", Op = "renameFile", TargetName = "content.bin", StoragePolicy = new() {SourceCleanup = retention}}]}, Json);
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(first.Id, plan, null,
            StoragePolicy: new() {CleanupIntermediates = true, SourceCleanup = "delete"}), Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == nested.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, metadata));
        var rows = await Preview(final);
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(store, raw, [final.Id])).Complete);
        Assert.IsTrue(rows.All(row => !row.Eligible && row.Reason == "RetentionPolicyKeep"));
        Assert.IsTrue(File.Exists(raw.Files[0])); Assert.IsTrue(File.Exists(first.Files[0]));
    }

    [TestMethod]
    public async Task Following_step_explicit_intermediate_keep_protects_its_registered_input()
    {
        var source = await Artifact("processedFiles"); var child = await Artifact("processedFiles", source);
        var final = await Artifact("publishedFiles", child);
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = [new()
            {Id = "retain", Op = "renameFile", TargetName = "content.bin", StoragePolicy = new() {CleanupIntermediates = false}}]}, Json);
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(source.Id, plan, null,
            StoragePolicy: new() {CleanupIntermediates = true}), Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == child.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, metadata));
        Assert.IsTrue((await Preview(final)).All(row => !row.Eligible && row.Reason == "RetentionPolicyKeep"));
        Assert.IsTrue(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(child.Files[0]));
    }

    [TestMethod]
    public async Task Publication_or_source_policy_changes_invalidate_the_fixed_preview()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var row = (await Preview(final)).Single();
        var changed = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null, "{\"steps\":[]}", null,
            StoragePolicy: new() {CleanupIntermediates = false}), Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, changed));
        Assert.AreEqual("SelectionChanged", (await cleanup.ReclaimAsync(row, [1], default)).Reason);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [DataTestMethod]
    [DataRow("plan")]
    [DataRow("builder")]
    [DataRow("prepared-key")]
    [DataRow("mark-config")]
    [DataRow("download-file")]
    [DataRow("download-result")]
    [DataRow("download-task")]
    public async Task Historical_alias_references_in_other_owners_and_json_keys_are_retained(string kind)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var legacy = Path.Combine(root, "old-nas-path");
        var metadata = Path.Combine(root, "artifact-workspaces"); Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "relocations.json"), JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(1, [new(legacy, source.RootDirectory)]), Json));
        cleanup = new(db, store, new(store, root, new Storage()), workspaces: new(root, new Storage()));
        var oldFile = Path.Combine(legacy, "content.bin");
        var json = JsonSerializer.Serialize(new {path = oldFile});
        if (kind is "plan" or "builder" or "prepared-key")
            db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Id = 99, WorkflowRunId = 2, Status = AcquisitionStatus.Completed,
                PlanJson = kind == "plan" ? json : null, BuilderStateJson = kind == "builder" ? json : null,
                PreparedContextJson = kind == "prepared-key" ? JsonSerializer.Serialize(new Dictionary<string, bool> {{oldFile, true}}) : null});
        if (kind == "mark-config") db.PathMarks.Add(new PathMarkDbModel {Id = 99, Path = Path.Combine(root, "other"), ConfigJson = json});
        if (kind == "download-file") {db.DownloadTasks.Add(new() {Id = 99, DownloadPath = Path.Combine(root, "other")}); db.DownloadTaskFiles.Add(new() {DownloadTaskId = 99, Path = oldFile, Size = 10});}
        if (kind == "download-result") db.DownloadResults.Add(new() {Id = 99, DownloadTaskId = 99, Path = legacy, FilesJson = JsonSerializer.Serialize(new[] {oldFile})});
        if (kind == "download-task") db.DownloadTasks.Add(new() {Id = 99, DownloadPath = legacy});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [TestMethod]
    public async Task Shared_configuration_roots_are_not_inputs_but_actual_ancestor_input_paths_are_protected()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Id = 99, WorkflowRunId = 2, Status = AcquisitionStatus.Completed,
            TargetDirectory = root, BuilderStateJson = JsonSerializer.Serialize(new {
                storagePolicy = new {processingWorkspaceDirectory = root, rootDirectory = root},
                deliverySettings = new {rootDirectory = root}})});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsTrue((await Preview(final)).Single().Eligible);
        await db.AcquisitionTasks.Where(t => t.Id == 99).ExecuteUpdateAsync(set => set.SetProperty(t => t.PreparedContextJson,
            JsonSerializer.Serialize(new {localInput = new {rootDirectory = root}}, Json)));
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason);
        Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [TestMethod]
    public async Task Only_current_completed_acquisition_authorizes_its_explicit_historical_run_owners()
    {
        var historical = await Artifact("processedFiles", owner: 2);
        var final = await Artifact("publishedFiles", historical);
        db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Id = 99, WorkflowRunId = 1, Status = AcquisitionStatus.Completed});
        db.Set<WorkflowRunSourceDbModel>().AddRange(
            new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task", EntityId = "99"},
            new() {WorkflowRunId = 2, Module = "acquisition", EntityKind = "task", EntityId = "99"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        cleanup = new(db, store, new(store, root, new Storage()), [new AcquisitionArtifactCleanupOwnerScope(db)]);
        var owners = await cleanup.PublicationOwnersAsync(final.Id, default);
        CollectionAssert.AreEquivalent(new[] {1, 2}, owners.ToArray());
        Assert.IsTrue((await cleanup.PreviewAsync([final.Id], owners, default)).Single().Eligible);
        await db.AcquisitionTasks.Where(t => t.Id == 99).ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, AcquisitionStatus.Waiting));
        Assert.AreEqual(0, (await cleanup.PublicationOwnersAsync(final.Id, default)).Count);
        Assert.IsTrue(File.Exists(historical.Files[0]));
    }

    [DataTestMethod]
    [DataRow("options")]
    [DataRow("operation")]
    public async Task Valid_cloud_transport_workspace_is_allocation_but_output_and_nested_paths_remain_references(string kind)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var destination = Path.Combine(root, "unrelated-cloud-output");
        var request = new CloudDownloadRequest("other-cloud", new() {Id = "share", Url = "https://example.test/share", ProviderId = "baidu"},
            "account", null, destination) {WorkspaceDirectory = root};
        if (kind == "options") db.DownloadTasks.Add(new() {Id = 99, ThirdPartyId = CloudSites.Site("baidu"), DownloadPath = destination,
            Options = JsonSerializer.Serialize(request, Json)});
        else db.CloudDownloadOperations.Add(new() {Id = request.OperationId, WorkflowRunId = 2, Kind = "download", State = "completed",
            RequestJson = JsonSerializer.Serialize(request, Json)});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        async Task SetRequest(string json)
        {
            if (kind == "options") await db.DownloadTasks.Where(d => d.Id == 99).ExecuteUpdateAsync(set => set.SetProperty(d => d.Options, json));
            else await db.CloudDownloadOperations.Where(o => o.Id == request.OperationId).ExecuteUpdateAsync(set => set.SetProperty(o => o.RequestJson, json));
        }
        Assert.IsTrue((await Preview(final)).Single().Eligible, "The unrelated transport only shares its allocation root.");
        await SetRequest(JsonSerializer.Serialize(request with {WorkspaceDirectory = source.RootDirectory}, Json));
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "A workspace inside the candidate remains protected.");
        await SetRequest(JsonSerializer.Serialize(request with {RemoteEntry = new("baidu", "account", root)}, Json));
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "A nested actual entry path is never an allocation-root exemption.");
        await SetRequest(JsonSerializer.Serialize(request with {OutputDirectory = root}, Json));
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "A concrete transport output ancestor remains protected.");
        await SetRequest(JsonSerializer.Serialize(new {workspaceDirectory = root}, Json));
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "The field name alone does not establish a cloud request contract.");
        Assert.IsTrue(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
    }

    [TestMethod]
    public async Task Unbound_sources_and_retained_sibling_branches_do_not_become_cleanup_candidates()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        await db.Set<ArtifactOperationBindingDbModel>().Where(b => b.OperationId == source.OperationId).ExecuteDeleteAsync();
        Assert.AreEqual("SharedOwner", (await Preview(final)).Single().Reason);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "rebound", source.OperationId));
        var sibling = await Artifact("processedFiles", source, cleanupPolicy: false);
        Assert.AreEqual("ReferencedDescendant", (await Preview(final)).Single().Reason);
        Assert.IsTrue(File.Exists(sibling.Files[0])); Assert.IsTrue(File.Exists(source.Files[0]));
    }

    [DataTestMethod]
    [DataRow("started")]
    [DataRow("completed")]
    public async Task Completed_transport_receipt_is_exempt_only_for_its_exact_owned_download(string dispatchState)
    {
        var raw = await Artifact("download"); var step = await Artifact("processedFiles", raw, sourceCleanup: "delete");
        var final = await Artifact("publishedFiles", step);
        var metadata = JsonSerializer.Serialize(new DownloadArtifactMetadata(99, null), Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == raw.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, metadata));
        db.DownloadTasks.Add(new() {Id = 99, DownloadPath = raw.RootDirectory,
            Status = Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants.DownloadTaskDbModelStatus.Complete});
        db.DownloadTaskFiles.Add(new() {DownloadTaskId = 99, Path = raw.Files[0], Size = raw.Manifest.Entries[0].Length});
        db.DownloadResults.Add(new() {Id = 99, DownloadTaskId = 99, Path = raw.RootDirectory, FilesJson = JsonSerializer.Serialize(raw.Files)});
        db.CloudDownloadOperations.Add(new CloudOperationDbModel {Id = "own-download", Kind = "download", State = dispatchState, WorkflowRunId = 1, DownloadTaskId = 99});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var rows = (await Preview(final)).Where(r => r.Eligible).ToArray(); Assert.AreEqual(2, rows.Length);
        foreach (var row in rows) Assert.IsTrue((await cleanup.ReclaimAsync(row, [1], default)).Completed);
        Assert.IsFalse(File.Exists(raw.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        Assert.IsNotNull(await db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(r => r.Id == 99));
        Assert.AreEqual(dispatchState, (await db.CloudDownloadOperations.AsNoTracking().SingleAsync()).State);
        Assert.AreEqual(Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants.DownloadTaskDbModelStatus.Complete,
            (await db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    public async Task Completion_intent_commits_with_success_and_is_processed_without_waiting_for_old_history_sweep()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var observer = new PublishedArtifactCleanupCompletionObserver(db);
        await observer.StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Waiting}, new object(), default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        await observer.StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        // No implicit SaveChanges: uncommitted follow-up cannot run.
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        CollectionAssert.AreEqual(new[] {final.Id}, (await cleanup.GetRequestedIdsAsync(default)).ToArray());
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.IsFalse(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        Assert.AreEqual(1, (await cleanup.GetRequestedIdsAsync(default)).Count,
            "Actual retirement schedules one follow-up for ancestors released by this pass.");
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        var metadata = JsonNode.Parse((await store.GetArtifactAsync(final.Id))!.MetadataJson!)!;
        Assert.AreEqual("completed", metadata["publicationCleanup"]!["status"]!.GetValue<string>());
        Assert.AreEqual(1, metadata["publicationCleanup"]!["removedFiles"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task Restarted_owner_preserves_inputs_and_does_not_starve_the_completion_queue()
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        await new PublishedArtifactCleanupCompletionObserver(db).StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, WorkflowRunStatus.Waiting));
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        Assert.IsTrue(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        var metadata = JsonNode.Parse((await store.GetArtifactAsync(final.Id))!.MetadataJson!)!;
        Assert.AreEqual("ownerNotCompleted", metadata["publicationCleanup"]!["status"]!.GetValue<string>());
    }

    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
}
