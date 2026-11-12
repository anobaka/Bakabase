using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed partial class ArchiveQuarantineServiceTests
{
    private string _root = null!;
    private Store _store = null!;
    private ArchiveQuarantineService Service => new(_store, _root, new Storage());
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "artifact-quarantine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root); _store = new Store();
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private async Task<ArtifactRecord> Artifact(string id, string name, string content, params string[] parents)
    {
        var root = Path.Combine(_root, id); Directory.CreateDirectory(root);
        var path = Path.Combine(root, name); await File.WriteAllTextAsync(path, content);
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, new[] {path});
        var artifact = new ArtifactRecord(id, id, "files", root, [path], manifest, manifest.Fingerprint,
            ArtifactLocationState.Available, true, parents, null);
        _store.Records[id] = artifact; return artifact;
    }

    [TestMethod]
    public async Task Exact_manifest_quarantine_preserves_siblings_and_restores_without_overwrite()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var unrelated = Path.Combine(source.RootDirectory, "unrelated.txt"); await File.WriteAllTextAsync(unrelated, "retain");
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual("Quarantined", result.Status);
        Assert.IsFalse(File.Exists(source.Files[0])); Assert.AreEqual("retain", await File.ReadAllTextAsync(unrelated));
        Assert.AreEqual("original", await File.ReadAllTextAsync(_store.Records[source.Id].Files[0]));
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        await File.WriteAllTextAsync(source.Files[0], "new-user-file");
        var blocked = await Service.RestoreAsync(result.ReceiptId!);
        Assert.IsFalse(blocked.Completed); Assert.AreEqual("new-user-file", await File.ReadAllTextAsync(source.Files[0]));
        File.Delete(source.Files[0]);
        Assert.IsTrue((await Service.RestoreAsync(result.ReceiptId!)).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
        Assert.AreEqual(ArtifactLocationState.Available, _store.Records[source.Id].State);
    }

    [TestMethod]
    public async Task Restore_and_scheduled_cleanup_prune_only_owned_empty_directories_and_keep_unknown_entries()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed);
        var container = Path.Combine(source.RootDirectory, ".bakabase-quarantine");
        var unknown = Path.Combine(container, ".DS_Store"); await File.WriteAllTextAsync(unknown, "retain");
        var unrelated = Path.Combine(container, "unknown-empty-directory"); Directory.CreateDirectory(unrelated);
        Assert.IsTrue((await Service.RestoreAsync(result.ReceiptId!)).Completed);
        Assert.IsFalse(Directory.Exists(Path.Combine(container, result.ReceiptId!)));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        Assert.IsTrue(Directory.Exists(unrelated));
        await Service.CleanupEmptyDirectoriesAsync();
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        Assert.IsTrue(Directory.Exists(unrelated));
    }

    [TestMethod]
    public async Task Active_consumers_and_modified_sources_are_retained()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Active = true;
        Assert.AreEqual("Blocked", (await Service.QuarantineAsync(source.Id, [output.Id])).Status);
        Assert.IsTrue(File.Exists(source.Files[0]));
        _store.Active = false;
        await File.WriteAllTextAsync(source.Files[0], "modified");
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("modified", await File.ReadAllTextAsync(source.Files[0]));
    }

    [TestMethod]
    public async Task Missing_modified_and_unrelated_sibling_outputs_cannot_prove_processing()
    {
        var parent = await Artifact("parent", "download.zip", "original");
        var source = await Artifact("source", "archive.zip", "original", parent.Id);
        var sibling = await Artifact("sibling", "sibling.txt", "success", parent.Id);
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [sibling.Id])).Completed);
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        await File.WriteAllTextAsync(output.Files[0], "tampered");
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, ["missing"])).Completed);
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
    }

    [TestMethod]
    public async Task Interrupted_publish_resumes_after_files_moved_without_repeating_or_losing_them()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.FailNextLocation = true;
        var pending = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsFalse(pending.Completed); Assert.IsNotNull(pending.ReceiptId); Assert.IsFalse(File.Exists(source.Files[0]));
        var done = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(done.Completed, done.Message); Assert.AreEqual(pending.ReceiptId, done.ReceiptId);
        Assert.IsTrue((await Service.RestoreAsync(done.ReceiptId!)).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
    }

    [TestMethod]
    public async Task Receipts_store_managed_paths_relative_to_appdata_and_follow_relocation()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var done = await Service.QuarantineAsync(source.Id, [output.Id]); Assert.IsTrue(done.Completed);
        var json = await File.ReadAllTextAsync(Path.Combine(_root, "file-processing", "quarantine-receipts", done.ReceiptId + ".json"));
        StringAssert.Contains(json, "appdata:source"); Assert.IsFalse(json.Contains(_root, StringComparison.Ordinal));
        var relocated = _root + "-moved"; Directory.Move(_root, relocated);
        foreach (var (id, artifact) in _store.Records.ToList())
            _store.Records[id] = artifact with {RootDirectory = artifact.RootDirectory.Replace(_root, relocated),
                Files = artifact.Files.Select(f => f.Replace(_root, relocated)).ToArray()};
        _root = relocated;
        Assert.IsTrue((await Service.RestoreAsync(done.ReceiptId!)).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(_root, "source", "archive.zip")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Quarantine_receipts_remain_in_appdata_and_restore_payloads_through_verified_aliases(bool absoluteEncoding)
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var done = await Service.QuarantineAsync(source.Id, [output.Id]); Assert.IsTrue(done.Completed);
        var receiptPath = Path.Combine(_root, "file-processing", "quarantine-receipts", done.ReceiptId + ".json");
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!;
        if (absoluteEncoding)
        {
            receipt["originalRoot"] = "absolute:" + source.RootDirectory;
            receipt["quarantineRoot"] = "absolute:" + _store.Records[source.Id].RootDirectory;
            await File.WriteAllTextAsync(receiptPath, receipt.ToJsonString());
        }
        var encodedOriginal = receipt["originalRoot"]!.GetValue<string>();
        var migrated = Path.Combine(_root, "nas", "source"); Directory.CreateDirectory(Path.GetDirectoryName(migrated)!);
        Directory.Move(source.RootDirectory, migrated);
        var current = _store.Records[source.Id];
        _store.Records[source.Id] = current with
        {
            RootDirectory = current.RootDirectory.Replace(source.RootDirectory, migrated),
            Files = current.Files.Select(p => p.Replace(source.RootDirectory, migrated)).ToArray()
        };
        var metadata = Path.Combine(_root, "artifact-workspaces"); Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "relocations.json"), JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(1, [new(source.RootDirectory, migrated)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.AreEqual(migrated, (await Service.GetReceiptsAsync()).Single().OriginalRoot);
        var restored = await Service.RestoreAsync(done.ReceiptId!);
        Assert.IsTrue(restored.Completed, restored.Message);
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(migrated, "archive.zip")));
        Assert.IsFalse(Directory.Exists(source.RootDirectory));
        Assert.IsTrue(File.Exists(receiptPath));
        Assert.AreEqual(encodedOriginal, JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!["originalRoot"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Symbolic_link_parent_is_rejected_before_any_file_move()
    {
        if (OperatingSystem.IsWindows()) return;
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var link = Path.Combine(_root, "linked"); Directory.CreateSymbolicLink(link, source.RootDirectory);
        _store.Records[source.Id] = source with {RootDirectory = link, Files = [Path.Combine(link, "archive.zip")]};
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
    }

    [TestMethod]
    public async Task Explicit_deletion_requires_published_delivery_and_preserves_unknown_files_and_history()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        Assert.IsFalse((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.IsTrue(File.Exists(source.Files[0]));
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        var quarantined = await Service.QuarantineAsync(source.Id, [output.Id]);
        var unknown = Path.Combine(_store.Records[source.Id].RootDirectory, "unknown.txt");
        await File.WriteAllTextAsync(unknown, "retain");
        var result = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("Purged", result.Status);
        Assert.AreEqual(ArtifactLocationState.Retired, _store.Records[source.Id].State);
        Assert.IsTrue(File.Exists(output.Files[0]));
        Assert.IsTrue(File.Exists(unknown));
        Assert.AreEqual("ReceiptPurged", (await Service.RestoreAsync(result.ReceiptId!)).Status);
        Assert.IsTrue((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("Purged", (await Service.GetReceiptsAsync()).Single().Status);
    }

    [TestMethod]
    public async Task Interrupted_permanent_cleanup_resumes_without_restoring_or_deleting_new_user_files()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        _store.FailNextLocation = true;
        var interrupted = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsFalse(interrupted.Completed);
        Assert.AreEqual(1, interrupted.RemovedFiles, "A failed metadata commit must still report the committed unlink.");
        Assert.AreEqual(source.Manifest.Entries.Single().Length, interrupted.RemovedBytes);
        Assert.AreEqual("Deleting", (await Service.GetReceiptsAsync()).Single().Status);
        Assert.AreEqual("ReceiptPurged", (await Service.RestoreAsync(interrupted.ReceiptId!)).Status);
        await File.WriteAllTextAsync(source.Files[0], "new user file");
        var finished = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsTrue(finished.Completed, finished.Message);
        Assert.AreEqual(0, finished.RemovedFiles, "Retry cannot report an earlier attempt's unlink a second time.");
        Assert.AreEqual("new user file", await File.ReadAllTextAsync(source.Files[0]));
        Assert.IsTrue(File.Exists(output.Files[0]));
    }

    [TestMethod]
    public async Task Active_references_modified_payloads_and_registered_aliases_block_permanent_cleanup()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        _store.Records["alias"] = source with {Id = "alias", OperationId = "alias", Kind = "publishedFiles"};
        Assert.IsFalse((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.IsTrue(File.Exists(source.Files[0]));
        _store.Records.Remove("alias");
        var quarantined = await Service.QuarantineAsync(source.Id, [output.Id]);
        _store.Active = true;
        Assert.IsFalse((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        _store.Active = false;
        await File.WriteAllTextAsync(_store.Records[source.Id].Files[0], "modified");
        Assert.IsFalse((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("modified", await File.ReadAllTextAsync(_store.Records[source.Id].Files[0]));
        Assert.IsTrue(File.Exists(output.Files[0]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Permanent_cleanup_rechecks_both_original_and_quarantine_aliases_inside_the_fence(bool originalLocation)
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        var quarantined = _store.Records[source.Id];
        var alias = originalLocation ? source : quarantined;
        _store.Records["alias"] = alias with {Id = "alias", OperationId = "alias", Kind = "publishedFiles"};
        var result = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("Blocked", result.Status);
        Assert.AreEqual("Quarantined", (await Service.GetReceiptsAsync()).Single().Status);
        Assert.IsTrue(File.Exists(quarantined.Files[0]));
        Assert.IsTrue(File.Exists(output.Files[0]));
        _store.Records.Remove("alias");
        Assert.IsTrue((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
    }

    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
    private sealed class Store : IArtifactOperationStore
    {
        public Dictionary<string, ArtifactRecord> Records { get; } = [];
        public Task<IAsyncDisposable> AcquireMaintenanceAsync(CancellationToken ct = default)
        {
            OnMaintenance?.Invoke();
            return Task.FromResult<IAsyncDisposable>(new Fence());
        }
        public Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> paths, string exclude, CancellationToken ct = default) =>
            Task.FromResult(Records.Values.Any(a => a.Id != exclude && a.Files.Intersect(paths, ArtifactManifestFiles.PathComparer).Any()));
        private sealed class Fence : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }

        public bool Active { get; set; }
        public bool PublicationOperations { get; set; }
        public bool FailNextLocation { get; set; }
        public int ResolveCount { get; private set; }
        public Action? OnMaintenance { get; set; }
        public Action? OnQuarantine { get; set; }
        public Action? OnGetOperation { get; set; }
        public Func<ArtifactRecord, ArtifactRecord>? ResolvedIdentity { get; set; }
        public Task<ArtifactRecord?> GetArtifactAsync(string id, CancellationToken ct = default) => Task.FromResult(Records.GetValueOrDefault(id));
        public async Task<ArtifactRecord?> ResolveByIdAsync(string id, CancellationToken ct = default)
        {
            ResolveCount++;
            var artifact = await GetArtifactAsync(id, ct);
            if (artifact?.State != ArtifactLocationState.Available ||
                !(await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)).Available) return null;
            return ResolvedIdentity?.Invoke(artifact) ?? artifact;
        }
        public Task<ArtifactOperationDbModel?> GetOperationAsync(string id, CancellationToken ct = default)
        {
            OnGetOperation?.Invoke();
            return Task.FromResult<ArtifactOperationDbModel?>(new() {Id = id, State = ArtifactOperationState.Completed,
                Kind = PublicationOperations && Records.GetValueOrDefault(id)?.Kind == "publishedFiles" ? "files.publish" : "files.process",
                OutputArtifactId = id});
        }
        public Task<bool> TryQuarantineAsync(string id, CancellationToken ct = default)
        {
            if (Active) return Task.FromResult(false);
            OnQuarantine?.Invoke();
            Records[id] = Records[id] with {State = ArtifactLocationState.Quarantined}; return Task.FromResult(true);
        }
        public async Task SetLocationAsync(string id, string root, ArtifactLocationState state, CancellationToken ct = default)
        {
            if (FailNextLocation) { FailNextLocation = false; throw new IOException("interrupted publication"); }
            var artifact = Records[id];
            if (state == ArtifactLocationState.Available && !(await ArtifactManifestFiles.ValidateAsync(root, artifact.Manifest, ct)).Available)
                throw new IOException("invalid restore");
            Records[id] = artifact with {RootDirectory = root, State = state,
                Files = artifact.Manifest.Entries.Select(e => Path.Combine(root, e.RelativePath)).ToArray()};
        }
        public Task<ArtifactRecord?> ResolveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactOperationClaim> ClaimAsync(ArtifactOperationRequest request, string owner, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ArtifactOperationLease lease, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactRecord> CompleteAsync(ArtifactOperationLease lease, ArtifactRegistration registration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> FailAsync(ArtifactOperationLease lease, string error, bool unknown = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactRecord> AdoptAsync(ArtifactOperationRequest request, ArtifactRegistration registration, string owner, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AttachConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => Task.FromResult(!Active);
        public Task<bool> RenewExistingConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> PinConsumerAsync(string id, string key, string kind, string owner, CancellationToken ct = default) => Task.FromResult(!Active);
        public Task ReleaseConsumerAsync(string id, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasActiveConsumersAsync(string id, CancellationToken ct = default) => Task.FromResult(Active);
    }
}
