using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class ArtifactWorkspaceMaintenanceTests
{
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private ArtifactOperationStore _store = null!;
    private ArtifactWorkspaceMaintenance _maintenance = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "workspace-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _store = new(_db, () => _root);
        var archives = new NoExtraction();
        _maintenance = new(_db, _store, new(archives), new(archives), _root);
    }
    [TestCleanup]
    public async Task Cleanup() { await _db.DisposeAsync(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private async Task<(ArtifactRecord Artifact, string Original, string Intermediate, string State)> Fixture()
    {
        var checkpoint = Path.Combine(_root, "operation", Guid.NewGuid().ToString("N"));
        var original = Path.Combine(_root, "original.zip"); await File.WriteAllTextAsync(original, "original");
        // The lifecycle tests exercise real per-file receipts, not a fabricated Completed flag.
        // No extraction is needed: the private copy is retained byte-for-byte in the final output.
        var result = await new FileProcessingPlanExecutor(new NoExtraction()).ExecuteAsync(
            new() {Requirement = "notRequired"}, _root, [original], checkpoint);
        Assert.IsTrue(result.Completed, result.Message);
        var stateFile = Path.Combine(Path.GetDirectoryName(result.OutputDirectory!)!, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        var staged = state.Outputs["download"].Single().Path;
        var artifact = await _store.AdoptAsync(new(Guid.NewGuid().ToString("N"), "finished", "files.process", "input"),
            new(result.OutputDirectory!, result.Files, "processedFiles", true), "fixture");
        return (artifact, original, staged, stateFile);
    }

    [TestMethod]
    public async Task Preview_fingerprint_authorizes_only_its_exact_unchanged_private_selection()
    {
        var fixture = await Fixture();
        var preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        Assert.IsNotNull(preview.SelectionFingerprint);
        Assert.AreEqual(64, preview.SelectionFingerprint.Length);
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint}})).Single();
        Assert.IsTrue(result.Completed, result.Reason);
        Assert.IsFalse(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.State));
    }

    [TestMethod]
    public async Task Equal_count_and_size_cannot_authorize_a_different_private_file_set()
    {
        var fixture = await Fixture();
        var preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        var changed = Path.Combine(Path.GetDirectoryName(fixture.Intermediate)!, "changed.zip");
        File.Move(fixture.Intermediate, changed);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(fixture.State), Json)!;
        state.Outputs["download"] = [state.Outputs["download"].Single() with {Path = changed}];
        state.Active = state.Active.Select(a => a.Path == fixture.Intermediate ? a with {Path = changed} : a).ToList();
        var checkpoint = Path.GetDirectoryName(fixture.State)!;
        var beforeKey = Path.GetRelativePath(checkpoint, fixture.Intermediate).Replace('\\', '/');
        var afterKey = Path.GetRelativePath(checkpoint, changed).Replace('\\', '/');
        var contents = state.FileContents[beforeKey];
        state.FileContents.Remove(beforeKey);
        state.FileContents[afterKey] = contents;
        await File.WriteAllTextAsync(fixture.State, JsonSerializer.Serialize(state, Json));
        var next = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        Assert.AreEqual(preview.FileCount, next.FileCount);
        Assert.AreEqual(preview.Bytes, next.Bytes);
        Assert.AreNotEqual(preview.SelectionFingerprint, next.SelectionFingerprint);
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint!}})).Single();
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("SelectionChanged", result.Reason);
        Assert.IsTrue(File.Exists(changed));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "file-processing", "workspace-cleanup-receipts")));
    }

    [TestMethod]
    public async Task Same_size_candidate_with_new_timestamp_requires_a_new_preview()
    {
        var fixture = await Fixture();
        var preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        File.SetLastWriteTimeUtc(fixture.Intermediate, File.GetLastWriteTimeUtc(fixture.Intermediate).AddHours(-1));
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint!}})).Single();
        Assert.AreEqual("SelectionChanged", result.Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "file-processing", "workspace-cleanup-receipts")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string>()));
    }

    [TestMethod]
    public async Task Scoped_preview_and_execution_respect_the_artifacts_frozen_keep_policy()
    {
        var kept = await Fixture(); var eligible = await Fixture();
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null,
            JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "notRequired"}, Json), null,
            StoragePolicy: new() {CleanupIntermediates = false}), Json);
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == kept.Artifact.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, metadata));
        var preview = await _maintenance.PreviewAsync([kept.Artifact.Id], default);
        Assert.AreEqual(1, preview.Count);
        Assert.AreEqual("RetentionPolicyKeep", preview[0].Reason);
        Assert.IsFalse(preview[0].Eligible);
        var results = await _maintenance.ReclaimAsync([kept.Artifact.Id, eligible.Artifact.Id], default);
        Assert.AreEqual("RetentionPolicyKeep", results[0].Reason);
        Assert.IsTrue(results[1].Completed);
        Assert.IsTrue(File.Exists(kept.Intermediate));
        Assert.IsFalse(File.Exists(eligible.Intermediate));
        Assert.IsTrue(File.Exists(kept.Artifact.Files.Single()));
        Assert.IsTrue(File.Exists(eligible.Artifact.Files.Single()));
    }

    [TestMethod]
    public async Task Verified_completed_private_snapshots_are_reclaimed_without_losing_result_history_or_reuse()
    {
        var fixture = await Fixture();
        var unknown = Path.Combine(Path.GetDirectoryName(fixture.Intermediate)!, "unregistered-user-file.txt");
        await File.WriteAllTextAsync(unknown, "keep");
        var preview = (await _maintenance.PreviewAsync(default)).Single();
        Assert.IsTrue(preview.Eligible); Assert.AreEqual(1, preview.FileCount); Assert.AreEqual(8, preview.Bytes);
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single();
        Assert.IsTrue(result.Completed); Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsFalse(File.Exists(fixture.Intermediate));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(unknown));
        Assert.IsTrue(File.Exists(fixture.Original)); Assert.IsTrue(File.Exists(fixture.State));
        Assert.AreEqual(fixture.Artifact.Id, (await _store.ResolveAsync("finished"))!.Id);
        Assert.AreEqual("NothingToReclaim", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
    }

    [TestMethod]
    public async Task Legacy_completed_snapshots_without_file_receipts_are_retained()
    {
        var fixture = await Fixture();
        var historical = new FileProcessingPlanExecutor.State
        {
            Completed = true, FinalFiles = fixture.Artifact.Files.ToList(),
            Outputs = new() {["download"] = [new(fixture.Intermediate, "original.zip")]}
        };
        await File.WriteAllTextAsync(fixture.State, JsonSerializer.Serialize(historical, Json));
        var preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        Assert.IsFalse(preview.Eligible);
        Assert.AreEqual(0, preview.FileCount);
        Assert.AreEqual("NothingToReclaim", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.State));
    }

    [TestMethod]
    public async Task Active_consumers_and_waiting_states_keep_their_snapshots()
    {
        var fixture = await Fixture();
        await _store.AttachConsumerAsync(fixture.Artifact.OperationId, "active-workflow", TimeSpan.FromMinutes(5));
        var blocked = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single();
        Assert.AreEqual("ActiveConsumer", blocked.Reason); Assert.IsTrue(File.Exists(fixture.Intermediate));
        await _store.ReleaseConsumerAsync(fixture.Artifact.OperationId, "active-workflow");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(fixture.State), Json)!;
        state.Completed = false;
        await File.WriteAllTextAsync(fixture.State, JsonSerializer.Serialize(state, Json));
        Assert.AreEqual("NothingToReclaim", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
    }

    [TestMethod]
    public async Task A_modified_result_or_independently_registered_snapshot_cannot_authorize_cleanup()
    {
        var fixture = await Fixture();
        var retained = await _store.AdoptAsync(new("snapshot", "snapshot", "manual", "input"),
            new(Path.GetDirectoryName(fixture.Intermediate)!, [fixture.Intermediate]), "fixture");
        Assert.AreEqual("ReferencedArtifact", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        // A separate fixture proves verification happens again at execute time, after preview.
        var changed = await Fixture();
        Assert.IsTrue((await _maintenance.PreviewAsync(default)).Single(x => x.ArtifactId == changed.Artifact.Id).Eligible);
        await File.WriteAllTextAsync(changed.Artifact.Files.Single(), "changed");
        Assert.AreEqual("ResultNotVerified", (await _maintenance.ReclaimAsync([changed.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(changed.Intermediate));
    }
    [TestMethod]
    public async Task A_quarantine_restore_keeps_its_original_paths_fenced_until_registration_commits()
    {
        var fixture = await Fixture();
        var originalRoot = Path.GetDirectoryName(fixture.Intermediate)!;
        var snapshot = await _store.AdoptAsync(new("snapshot-restore", "snapshot-restore", "manual", "input"),
            new(originalRoot, [fixture.Intermediate], Managed: true), "fixture");
        Assert.IsTrue(await _store.TryQuarantineAsync(snapshot.Id));
        var quarantine = Path.Combine(_root, "quarantine"); Directory.CreateDirectory(quarantine);
        var quarantined = Path.Combine(quarantine, Path.GetFileName(fixture.Intermediate));
        File.Move(fixture.Intermediate, quarantined);
        await _store.SetLocationAsync(snapshot.Id, quarantine, ArtifactLocationState.Quarantined);
        // Restoration has copied bytes back, but not yet committed Available at the original root.
        File.Copy(quarantined, fixture.Intermediate);
        Assert.AreEqual("ReferencedArtifact", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
    }

    [TestMethod]
    public async Task Maintenance_fence_blocks_new_adoptions_claims_and_consumer_attachment_without_deadlocking()
    {
        var fixture = await Fixture();
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options;
        await using var adoptionDb = new BakabaseDbContext(options);
        await using var consumerDb = new BakabaseDbContext(options);
        await using var claimDb = new BakabaseDbContext(options);
        var adoptionStore = new ArtifactOperationStore(adoptionDb, () => _root);
        var consumerStore = new ArtifactOperationStore(consumerDb, () => _root);
        var claimStore = new ArtifactOperationStore(claimDb, () => _root);
        var fence = await _store.AcquireMaintenanceAsync();
        var adoption = adoptionStore.AdoptAsync(new("new-registration", "new-registration", "manual", "input"),
            new(Path.GetDirectoryName(fixture.Intermediate)!, [fixture.Intermediate]), "new-owner");
        var consumer = consumerStore.AttachConsumerAsync(fixture.Artifact.OperationId, "new-consumer", TimeSpan.FromMinutes(2));
        var claim = claimStore.ClaimAsync(new("new-operation", "new-operation", "manual", "input"), "claim-owner", TimeSpan.FromMinutes(2));
        Assert.IsFalse(adoption.IsCompleted); Assert.IsFalse(consumer.IsCompleted); Assert.IsFalse(claim.IsCompleted);
        await fence.DisposeAsync();
        Assert.IsNotNull(await adoption.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(await consumer.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(ArtifactClaimStatus.Acquired, (await claim.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.AreEqual("ActiveConsumer", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        await consumerStore.ReleaseConsumerAsync(fixture.Artifact.OperationId, "new-consumer");
        Assert.AreEqual("ReferencedArtifact", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
    }

    [TestMethod]
    public async Task Pending_maintenance_waits_for_mutations_and_cancellation_does_not_poison_the_fence()
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_root).EnterMutationAsync(default);
        using var cancellation = new CancellationTokenSource();
        var maintenance = _store.AcquireMaintenanceAsync(cancellation.Token);
        Assert.IsFalse(maintenance.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => maintenance);
        // A cancelled writer releases the admission gate; another mutation can still run.
        await using var another = await ArtifactMaintenanceGate.For(_root).EnterMutationAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Noop_cleanup_does_not_queue_behind_unrelated_exclusive_maintenance(bool activeConsumer)
    {
        var fixture = await Fixture();
        if (activeConsumer)
            Assert.IsTrue(await _store.AttachConsumerAsync(fixture.Artifact.OperationId, "active-reader", TimeSpan.FromMinutes(2)));
        else
            Assert.IsTrue((await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Completed);
        await using var fence = await _store.AcquireMaintenanceAsync();
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)
            .WaitAsync(TimeSpan.FromSeconds(3))).Single();
        Assert.AreEqual(activeConsumer ? "ActiveConsumer" : "NothingToReclaim", result.Reason);
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        Assert.AreEqual(activeConsumer, File.Exists(fixture.Intermediate));
    }

    [TestMethod]
    public async Task Reclamation_has_an_exact_private_receipt_and_repeated_reuse_does_not_create_another()
    {
        var fixture = await Fixture();
        var first = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single();
        var directory = Path.Combine(_root, "file-processing", "workspace-cleanup-receipts");
        var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(Directory.GetFiles(directory).Single()), Json)!;
        Assert.AreEqual("Completed", receipt.Status);
        Assert.AreEqual(fixture.Artifact.Id, receipt.ArtifactId);
        Assert.AreEqual(fixture.Artifact.Fingerprint, receipt.ResultFingerprint);
        Assert.AreEqual(1, receipt.RemovedFiles);
        Assert.AreEqual(first.RemovedBytes, receipt.RemovedBytes);
        Assert.IsTrue(receipt.Files.Single().Path.StartsWith("appdata:"));
        Assert.IsNotNull(receipt.FinishedAt);
        await _maintenance.ReclaimAsync([fixture.Artifact.Id], default);
        Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        Assert.AreEqual(fixture.Artifact.Id, (await _store.ResolveByIdAsync(fixture.Artifact.Id))!.Id);
    }

    [TestMethod]
    public async Task An_unwritable_receipt_directory_prevents_payload_deletion()
    {
        var fixture = await Fixture();
        var directory = Path.Combine(_root, "file-processing"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "workspace-cleanup-receipts"), "blocked");
        Assert.IsFalse((await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Completed);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
    }

    private ServiceProvider LifecycleServices(Func<IArtifactOperationStore, IArtifactOperationStore>? decorate = null) => new ServiceCollection()
        .AddScoped(_ => new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options))
        .AddScoped<IArtifactOperationStore>(sp =>
        {
            var store = new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => _root);
            return decorate?.Invoke(store) ?? store;
        })
        .AddScoped(sp => new ArtifactWorkspaceMaintenance(sp.GetRequiredService<BakabaseDbContext>(),
            sp.GetRequiredService<IArtifactOperationStore>(), new(new NoExtraction()), new(new NoExtraction()), _root))
        .BuildServiceProvider();

    [TestMethod]
    public async Task Idle_compensation_retries_after_last_consumer_release_and_survives_a_restart()
    {
        var fixture = await Fixture();
        await _store.AttachConsumerAsync(fixture.Artifact.OperationId, "reader", TimeSpan.FromMinutes(5));
        await using var services = LifecycleServices();
        var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance);
        await cleanup.RunIdlePassAsync(default);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        await _store.ReleaseConsumerAsync(fixture.Artifact.OperationId, "reader");
        // No completion callback fires again; a fresh worker discovers the durable registration.
        cleanup = new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance);
        await cleanup.RunIdlePassAsync(default);
        Assert.IsFalse(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsNotNull(await _store.ResolveByIdAsync(fixture.Artifact.Id));
    }

    [TestMethod]
    public async Task Waiting_workflow_is_protected_until_terminal_status_without_any_node_specific_release()
    {
        var fixture = await Fixture();
        _db.Add(new WorkflowRunDbModel {Id = 72, WorkflowDefinitionId = 1, Status = WorkflowRunStatus.Waiting});
        await _db.SaveChangesAsync();
        Assert.IsTrue(await _store.PinConsumerAsync(fixture.Artifact.OperationId, "workflow-reader", "workflow", "72"));
        await _db.Set<ArtifactConsumerDbModel>().ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddDays(-1)));
        await using var services = LifecycleServices();
        var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance);
        await cleanup.RunIdlePassAsync(default);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        await _db.Set<WorkflowRunDbModel>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        await cleanup.RunIdlePassAsync(default); // End of cursor cycle.
        await cleanup.RunIdlePassAsync(default);
        Assert.IsFalse(File.Exists(fixture.Intermediate));
    }

    [TestMethod]
    public async Task Background_compensation_reclaims_unconsumed_artifacts_while_unrelated_downloads_and_workflows_run()
    {
        var fixture = await Fixture();
        _db.Add(new DownloadTaskDbModel {Id = 15, Key = "fixture", DownloadPath = Path.Combine(_root, "unrelated-download"), Status = DownloadTaskDbModelStatus.InProgress});
        _db.Add(new WorkflowRunDbModel {Id = 81, WorkflowDefinitionId = 1, Status = WorkflowRunStatus.Running});
        await _db.SaveChangesAsync();
        await using var services = LifecycleServices();
        var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance);
        await cleanup.RunIdlePassAsync(default);
        Assert.IsFalse(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsNotNull(await _store.ResolveByIdAsync(fixture.Artifact.Id));
    }

    [TestMethod]
    public async Task Candidate_keyset_visits_later_artifacts_without_repeating_the_first_page()
    {
        var fixtures = new[] {await Fixture(), await Fixture(), await Fixture()};
        var first = await _maintenance.GetCandidateIdsAsync(null, 2, default);
        var next = await _maintenance.GetCandidateIdsAsync(first[^1], 2, default);
        Assert.AreEqual(2, first.Count); Assert.AreEqual(1, next.Count);
        CollectionAssert.AreEquivalent(fixtures.Select(f => f.Artifact.Id).ToArray(), first.Concat(next).ToArray());
        Assert.AreEqual(0, (await _maintenance.GetCandidateIdsAsync(next[^1], 2, default)).Count);
    }

    [TestMethod]
    public async Task One_idle_pass_reclaims_at_most_two_operations()
    {
        var fixtures = new[] {await Fixture(), await Fixture(), await Fixture()};
        await using var services = LifecycleServices();
        var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance);
        await cleanup.RunIdlePassAsync(default);
        Assert.AreEqual(1, fixtures.Count(f => File.Exists(f.Intermediate)));
        await cleanup.RunIdlePassAsync(default);
        Assert.IsTrue(fixtures.All(f => !File.Exists(f.Intermediate)));
    }

    [TestMethod]
    public async Task Private_ledger_file_referenced_by_another_completed_task_is_preserved()
    {
        var fixture = await Fixture();
        _db.AcquisitionTasks.Add(new() {Id = 99,
            Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Completed,
            PreparedContextJson = JsonSerializer.Serialize(new {localInput = new {path = fixture.Intermediate}}, Json)});
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        Assert.AreEqual("ReferencedPath", (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.AreEqual("ReferencedPath", (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        Assert.IsNotNull(await _store.ResolveByIdAsync(fixture.Artifact.Id));
    }

    [TestMethod]
    public async Task Registered_boundary_reclaims_only_proven_private_inputs_after_every_owner_stops()
    {
        var original = Path.Combine(_root, "boundary.bin"); await File.WriteAllTextAsync(original, "payload");
        var checkpoint = Path.Combine(_root, "boundary-checkpoint");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameFile", TargetName = "first.bin"},
            new() {Id = "second", Input = "download", Op = "renameFile", TargetName = "second.bin"}]};
        var executor = new FileProcessingPlanExecutor(new NoExtraction());
        var boundary = await executor.ExecuteAsync(plan, _root, [original], checkpoint, stopAfterStepId: "first",
            storagePolicy: new() {Materialization = "copy", CleanupIntermediates = true});
        Assert.IsTrue(boundary.Completed, boundary.Message);
        var stateFile = Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single();
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        var staged = state.Outputs["download"].Single().Path;
        var unknown = Path.Combine(Path.GetDirectoryName(staged)!, "user.key"); await File.WriteAllTextAsync(unknown, "retain");
        var artifact = await _store.AdoptAsync(new("boundary-owned", "boundary-owned", "files.process", "input"),
            new(boundary.OutputDirectory!, boundary.Files, "processedFiles", true), "fixture");
        _db.WorkflowRuns.Add(new() {Id = 70, Status = WorkflowRunStatus.Waiting}); await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        Assert.IsTrue(await new ArtifactOperationBindingService(_db).BindAsync("workflow", "70", "boundary", artifact.OperationId));
        Assert.AreEqual("ActiveConsumer", (await _maintenance.ReclaimAsync([artifact.Id], default)).Single().Reason);
        Assert.IsTrue(File.Exists(staged));
        await _db.WorkflowRuns.Where(r => r.Id == 70).ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        var preview = (await _maintenance.PreviewAsync([artifact.Id], default)).Single(); Assert.IsTrue(preview.Eligible, preview.Reason);
        var outcome = (await _maintenance.ReclaimAsync([artifact.Id], default)).Single(); Assert.IsTrue(outcome.Completed, outcome.Reason);
        Assert.IsFalse(File.Exists(staged)); Assert.IsTrue(File.Exists(original)); Assert.IsTrue(File.Exists(boundary.Files.Single()));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        var retained = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        Assert.IsFalse(retained.Completed); Assert.IsNull(retained.FinalManifest); Assert.IsNotNull(retained.IntermediatesReclaimedAt);
        Assert.IsTrue(retained.BoundaryResults.ContainsKey("first")); Assert.AreEqual(state.FileTransitions.Count, retained.FileTransitions.Count);
    }

    [TestMethod]
    public async Task Canonical_paths_outside_private_roots_and_result_step_checkpoints_remain()
    {
        var fixture = await Fixture();
        var checkpoint = Path.GetDirectoryName(fixture.State)!;
        var retained = Path.Combine(checkpoint, "retained.txt"); await File.WriteAllTextAsync(retained, "keep");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(fixture.State), Json)!;
        state.Outputs["escaped"] = [new(Path.Combine(checkpoint, "inputs", "..", "retained.txt"), "retained.txt")];
        await File.WriteAllTextAsync(fixture.State, JsonSerializer.Serialize(state, Json));
        var stepRoot = Path.Combine(checkpoint, "result-step-TEST"); Directory.CreateDirectory(stepRoot);
        var stepFile = Path.Combine(stepRoot, "history.txt"); await File.WriteAllTextAsync(stepFile, "history");
        var step = await _store.AdoptAsync(new("intermediate", "intermediate", "files.process", "input"),
            new(stepRoot, [stepFile], "processedFiles", true), "fixture");
        Assert.AreEqual("IntermediateResultRetained", (await _maintenance.ReclaimAsync([step.Id], default)).Single().Reason);
        Assert.AreEqual(1, (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default)).Single().RemovedFiles);
        Assert.IsTrue(File.Exists(retained)); Assert.IsTrue(File.Exists(stepFile));
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task Recursive_preview_requires_every_private_file_to_have_evidence_and_no_other_consumer(
        bool independentlyRegistered, bool omitOutputEvidence)
    {
        var directory = Path.Combine(_root, "recursive");
        var archive = Path.Combine(_root, "two-files.zip");
        await File.WriteAllTextAsync(archive, "archive");
        var result = await new RecursiveArchiveExecutor(new TwoFileExtraction()).ExecuteAsync(
            _root, [archive], directory, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed, result.Message);
        var stateFile = Path.Combine(directory, "recursive-state.json");
        var recursive = JsonSerializer.Deserialize<RecursiveArchiveExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        var node = recursive.Nodes.Single();
        var nodeRoot = Path.Combine(directory, node.OutputRoot!);
        var nested = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(Path.GetDirectoryName(nodeRoot)!, "state.json")), Json)!;
        var nodeFiles = nested.FinalFiles;
        Assert.AreEqual(2, nodeFiles.Count);
        if (omitOutputEvidence)
        {
            // A successful tool exit and partial lineage cannot account for an omitted sibling.
            node.Outputs = node.Outputs.Take(1).ToList();
            await File.WriteAllTextAsync(stateFile, JsonSerializer.Serialize(recursive, Json));
        }
        var artifact = await _store.AdoptAsync(new("recursive", "recursive", "files.process", "input"),
            new(result.OutputDirectory!, result.Files, "processedFiles", true), "fixture");
        var preview = (await _maintenance.PreviewAsync([artifact.Id], default)).Single();
        // One private input, two extraction outputs and two private node-result files.
        Assert.AreEqual(omitOutputEvidence ? 0 : 5, preview.FileCount);
        if (independentlyRegistered)
            await _store.AdoptAsync(new("independent", "independent", "manual", "input"), new(nodeRoot, [nodeFiles[1]]), "fixture");
        var cleanup = (await _maintenance.ReclaimAsync([artifact.Id], default)).Single();
        if (omitOutputEvidence) Assert.AreEqual("NothingToReclaim", cleanup.Reason);
        else if (independentlyRegistered) Assert.AreEqual("ReferencedArtifact", cleanup.Reason);
        else { Assert.IsTrue(cleanup.Completed); Assert.AreEqual(5, cleanup.RemovedFiles); }
        Assert.IsTrue(nodeFiles.All(f => File.Exists(f) == (independentlyRegistered || omitOutputEvidence)));
        Assert.IsTrue(result.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(archive));
    }

    [TestMethod]
    public async Task Executor_cannot_expand_an_audited_deletion_set()
    {
        var fixture = await Fixture();
        var plans = new FileProcessingPlanExecutor(new NoExtraction());
        await plans.ReclaimCompletedIntermediatesAsync(fixture.Artifact.RootDirectory, fixture.Artifact.Manifest,
            new HashSet<string>(FileProcessingFiles.PathComparer));
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
    }

    private sealed class TwoFileExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "first.txt"), "first", ct);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "second.txt"), "second", ct);
            return new(true, request.Directory, "");
        }
    }

    private sealed class NoExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => throw new AssertFailedException("Maintenance must not extract.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct) => throw new AssertFailedException("Maintenance must not extract.");
    }
}
