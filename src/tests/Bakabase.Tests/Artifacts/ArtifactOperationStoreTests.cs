using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class ArtifactOperationStoreTests
{
    private string _root = null!;
    private string _appData = null!;
    private BakabaseDbContext _db = null!;
    private ArtifactOperationStore _store = null!;
    private static readonly TimeSpan LeaseTime = TimeSpan.FromMinutes(5);

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "artifact-store-" + Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_appData);
        _db = OpenDb();
        await _db.Database.EnsureCreatedAsync();
        _store = Store(_db);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private BakabaseDbContext OpenDb() => new(new DbContextOptionsBuilder<BakabaseDbContext>()
        .UseSqlite("Data Source=" + Path.Combine(_root, "artifacts.db")).Options);
    private ArtifactOperationStore Store(BakabaseDbContext db) => new(db, () => _appData);
    private static ArtifactOperationRequest Request(string id, bool restartable = true) =>
        new(id, "intent-" + id, "test.files.v1", "content-" + id, restartable);
    private async Task<ArtifactRegistration> Files(string name, bool managed = true, string? root = null)
    {
        root ??= Path.Combine(_appData, name);
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        var file = Path.Combine(root, "nested", name + ".txt");
        await File.WriteAllTextAsync(file, "content-" + name);
        return new(root, new[] {file}, Managed: managed);
    }
    private Task<ArtifactRecord> Adopt(string id, ArtifactRegistration registration) =>
        _store.AdoptAsync(Request(id), registration, "test-owner");

    [TestMethod]
    public async Task Changed_output_cannot_commit_a_proof_for_an_earlier_manifest()
    {
        var files = await Files("proof-bound");
        var manifest = await ArtifactManifestFiles.CaptureAsync(files.RootDirectory, files.Files.ToArray());
        var claim = await _store.ClaimAsync(Request("proof-bound"), "owner", LeaseTime);
        var bound = files with {ExpectedManifestFingerprint = manifest.Fingerprint};
        await File.WriteAllTextAsync(files.Files[0], "changed after proof");

        await Assert.ThrowsExactlyAsync<IOException>(() => _store.CompleteAsync(claim.Lease!, bound));
        var pending = await _store.GetOperationAsync(claim.OperationId);
        Assert.AreEqual(ArtifactOperationState.Running, pending!.State);
        Assert.IsNull(pending.OutputArtifactId);
        Assert.AreEqual("changed after proof", await File.ReadAllTextAsync(files.Files[0]));

        await File.WriteAllTextAsync(files.Files[0], "content-proof-bound");
        var completed = await _store.CompleteAsync(claim.Lease!, bound);
        Assert.AreEqual(manifest.Fingerprint, completed.Fingerprint);
    }

    [TestMethod]
    public async Task First_attempt_only_claim_never_restarts_a_failed_or_missing_completed_generation()
    {
        var request = Request("first-only") with {AllowRetry = false};
        var first = await _store.ClaimAsync(request, "owner", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Acquired, first.Status);
        await _store.FailAsync(first.Lease!, "retained failure");
        var retry = await _store.ClaimAsync(request, "owner", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation, retry.Status);
        Assert.AreEqual(1, (await _store.GetOperationAsync(request.OperationKey))!.Attempt);

        var complete = await Adopt("complete-only", await Files("complete-only"));
        var reusable = Request("complete-only") with {AllowRetry = false};
        Assert.AreEqual(ArtifactClaimStatus.Completed, (await _store.ClaimAsync(reusable, "new-consumer", LeaseTime)).Status);
        File.Delete(complete.Files[0]);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation, (await _store.ClaimAsync(reusable, "new-consumer", LeaseTime)).Status);
        Assert.AreEqual(1, (await _store.GetOperationAsync("complete-only"))!.Attempt);
    }

    [TestMethod]
    public async Task Concurrent_first_attempt_only_claims_do_not_upgrade_to_a_new_generation()
    {
        await using var otherDb = OpenDb(); var other = Store(otherDb);
        var request = Request("first-race") with {AllowRetry = false};
        var claims = await Task.WhenAll(_store.ClaimAsync(request, "one", LeaseTime), other.ClaimAsync(request, "two", LeaseTime));
        Assert.AreEqual(1, claims.Count(c => c.Status == ArtifactClaimStatus.Acquired));
        Assert.AreEqual(1, claims.Count(c => c.Status == ArtifactClaimStatus.NeedsReconciliation));
        Assert.AreEqual(1, (await _store.GetOperationAsync("first-race"))!.Attempt);
    }

    [TestMethod]
    public async Task Maintenance_keeps_live_operation_and_consumer_heartbeats_running_without_reviving_inactive_readers()
    {
        var source = await Adopt("heartbeat-source", await Files("heartbeat-source"));
        var claim = await _store.ClaimAsync(Request("publishing"), "publisher", LeaseTime);
        Assert.IsTrue(await _store.AttachConsumerAsync(source.OperationId, "live", LeaseTime));
        Assert.IsTrue(await _store.AttachConsumerAsync(source.OperationId, "expired", LeaseTime));
        Assert.IsTrue(await _store.AttachConsumerAsync(source.OperationId, "released", LeaseTime));
        await _store.ReleaseConsumerAsync(source.OperationId, "released");
        await _db.Set<ArtifactConsumerDbModel>().Where(c => c.ConsumerKey == "expired")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var originalLease = (await _store.GetOperationAsync(claim.OperationId))!.LeaseUntil;
        var originalConsumer = await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync(c => c.ConsumerKey == "live");
        await using var fence = await _store.AcquireMaintenanceAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var longer = LeaseTime + LeaseTime;
        Assert.IsTrue(await _store.RenewAsync(claim.Lease!, longer, deadline.Token),
            "Maintenance hashing must not pause the producer's operation heartbeat.");
        Assert.IsTrue(await _store.RenewExistingConsumerAsync(source.OperationId, "live", longer, deadline.Token),
            "An already-protected reader must keep its lease while new attachments are fenced.");
        Assert.IsFalse(await _store.RenewExistingConsumerAsync(source.OperationId, "missing", longer, deadline.Token));
        Assert.IsFalse(await _store.RenewExistingConsumerAsync(source.OperationId, "expired", longer, deadline.Token));
        Assert.IsFalse(await _store.RenewExistingConsumerAsync(source.OperationId, "released", longer, deadline.Token));
        Assert.AreEqual(3, await _db.Set<ArtifactConsumerDbModel>().CountAsync());
        Assert.IsTrue((await _store.GetOperationAsync(claim.OperationId))!.LeaseUntil > originalLease);
        var renewed = await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync(c => c.ConsumerKey == "live");
        Assert.IsTrue(renewed.LeaseUntil > originalConsumer.LeaseUntil);
        Assert.IsNull(renewed.ReleasedAt);
        // Renewal may extend but never shorten a lease used by another active reader.
        Assert.IsTrue(await _store.RenewExistingConsumerAsync(source.OperationId, "live", LeaseTime, deadline.Token));
        Assert.AreEqual(renewed.LeaseUntil,
            (await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync(c => c.ConsumerKey == "live")).LeaseUntil);
    }

    [TestMethod]
    public async Task Completed_output_is_reused_across_consumers_and_survives_source_removal()
    {
        var raw = await Adopt("raw", await Files("raw"));
        var finalFiles = await Files("final");
        var final = await Adopt("extract", finalFiles with {ParentArtifactIds = new[] {raw.Id}, MetadataJson = "{\"plan\":1}"});
        File.Delete(raw.Files[0]);
        Assert.IsNull(await _store.ResolveByIdAsync(raw.Id));
        var reused = await _store.ResolveAsync("intent-extract");
        Assert.IsNotNull(reused);
        Assert.AreEqual(final.Id, reused.Id);
        Assert.AreEqual("{\"plan\":1}", reused.MetadataJson);
        CollectionAssert.AreEqual(new[] {raw.Id}, reused.ParentArtifactIds.ToArray());
        Assert.IsTrue(await _store.AttachConsumerAsync(final.OperationId, "run-a", LeaseTime));
        Assert.IsTrue(await _store.AttachConsumerAsync(final.OperationId, "run-b", LeaseTime));
        await _store.ReleaseConsumerAsync(final.OperationId, "run-a");
        Assert.IsTrue(await _store.HasActiveConsumersAsync(final.OperationId));
        var claim = await _store.ClaimAsync(Request("extract"), "new-run", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Completed, claim.Status);
        Assert.AreEqual(final.Id, claim.Artifact!.Id);
        Assert.AreEqual(1, (await _store.GetOperationAsync(final.OperationId))!.Attempt);
    }

    [TestMethod]
    public async Task Disconnected_completed_storage_blocks_new_generations_and_reuses_files_after_reconnect()
    {
        var artifact = await Adopt("disconnected", await Files("disconnected"));
        var detached = artifact.RootDirectory + "-detached";
        Directory.Move(artifact.RootDirectory, detached);
        Assert.IsNull(await _store.ResolveByIdAsync(artifact.Id));
        Assert.AreEqual(ArtifactLocationState.StorageUnavailable, (await _store.GetArtifactAsync(artifact.Id))!.State);
        var blocked = await _store.ClaimAsync(Request("disconnected"), "retry", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.StorageUnavailable, blocked.Status);
        Assert.IsNull(blocked.Lease);
        Assert.AreEqual(1, (await _store.GetOperationAsync(artifact.OperationId))!.Attempt);
        Directory.Move(detached, artifact.RootDirectory);
        var restored = await _store.ClaimAsync(Request("disconnected"), "retry", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Completed, restored.Status);
        Assert.AreEqual(artifact.Id, restored.Artifact!.Id);
        Assert.AreEqual(1, (await _store.GetOperationAsync(artifact.OperationId))!.Attempt);
    }

    [TestMethod]
    public async Task Concurrent_claims_have_one_owner_and_stale_tokens_cannot_publish()
    {
        await using var secondDb = OpenDb();
        var second = Store(secondDb);
        var claims = await Task.WhenAll(_store.ClaimAsync(Request("shared"), "one", LeaseTime),
            second.ClaimAsync(Request("shared"), "two", LeaseTime));
        Assert.AreEqual(1, claims.Count(c => c.Status == ArtifactClaimStatus.Acquired));
        Assert.AreEqual(1, claims.Count(c => c.Status == ArtifactClaimStatus.Busy));
        var original = claims.Single(c => c.Lease != null).Lease!;
        await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == "shared")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var recovered = await second.ClaimAsync(Request("shared"), "recovery", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Acquired, recovered.Status);
        Assert.AreNotEqual(original.Token, recovered.Lease!.Token);
        Assert.AreEqual(1, (await second.GetOperationAsync("shared"))!.Attempt, "Lease recovery retains its checkpoint generation.");
        Assert.IsFalse(await _store.RenewAsync(original, LeaseTime));
        Assert.IsFalse(await _store.FailAsync(original, "stale failure"));
        var files = await Files("shared");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _store.CompleteAsync(original, files));
        var published = await second.CompleteAsync(recovered.Lease!, files);
        Assert.AreEqual(published.Id, (await _store.ResolveAsync("intent-shared"))!.Id);
    }

    [TestMethod]
    public async Task Execution_slot_binding_freezes_semantic_operation_but_allows_sharing_across_runs()
    {
        await using var secondDb = OpenDb();
        var first = new ArtifactOperationBindingService(_db);
        var second = new ArtifactOperationBindingService(secondDb);
        var bindings = await Task.WhenAll(first.BindAsync("workflow", "1", "target:transfer", "account-a"),
            second.BindAsync("workflow", "1", "target:transfer", "account-b"));
        Assert.AreEqual(1, bindings.Count(bound => bound));
        var bound = await _db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().SingleAsync();
        Assert.IsTrue(await first.BindAsync("workflow", "1", "target:transfer", bound.OperationId));
        Assert.IsFalse(await first.BindAsync("workflow", "1", "target:transfer", "new-configuration"));
        Assert.IsTrue(await second.BindAsync("workflow", "2", "target:transfer", bound.OperationId));
        Assert.IsTrue(await first.BindAsync("workflow", "1", "target:download", "independent-download"));
    }

    [TestMethod]
    public async Task Same_owner_resumes_same_lease_but_unknown_side_effects_require_reconciliation()
    {
        var first = await _store.ClaimAsync(Request("resume"), "owner", LeaseTime);
        var resumed = await _store.ClaimAsync(Request("resume"), "owner", LeaseTime);
        Assert.AreEqual(first.Lease, resumed.Lease);
        Assert.AreEqual(1, (await _store.GetOperationAsync("resume"))!.Attempt);
        var remote = await _store.ClaimAsync(Request("remote", false), "remote-owner", LeaseTime);
        await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == "remote")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation,
            (await _store.ClaimAsync(Request("remote", false), "recovery", LeaseTime)).Status);
        Assert.IsFalse(await _store.RenewAsync(remote.Lease!, LeaseTime));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _store.ClaimAsync(
            Request("resume") with {InputFingerprint = "changed"}, "owner", LeaseTime));
    }

    [TestMethod]
    public async Task Same_length_same_timestamp_change_invalidates_result_and_starts_new_generation()
    {
        var registration = await Files("changed");
        var original = await Adopt("changed", registration);
        var file = registration.Files[0];
        var timestamp = File.GetLastWriteTimeUtc(file);
        var bytes = await File.ReadAllBytesAsync(file);
        bytes[0] ^= 1;
        await File.WriteAllBytesAsync(file, bytes);
        File.SetLastWriteTimeUtc(file, timestamp);
        Assert.IsNull(await _store.ResolveAsync("intent-changed"));
        Assert.AreEqual(ArtifactLocationState.Modified, (await _store.GetArtifactAsync(original.Id))!.State);
        var claim = await _store.ClaimAsync(Request("changed"), "retry", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Acquired, claim.Status);
        Assert.AreEqual(2, (await _store.GetOperationAsync("changed"))!.Attempt);
        var replacement = await _store.CompleteAsync(claim.Lease!, registration);
        Assert.AreNotEqual(original.Fingerprint, replacement.Fingerprint);
        Assert.AreNotEqual(original.Id, replacement.Id);
    }

    [TestMethod]
    public async Task AppData_paths_rebase_but_user_paths_remain_absolute()
    {
        var appArtifact = await Adopt("app", await Files("app"));
        var userRoot = Path.Combine(_root, "user-downloads");
        var userArtifact = await Adopt("user", await Files("user", root: userRoot));
        var appRow = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == appArtifact.Id);
        var userRow = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == userArtifact.Id);
        Assert.IsTrue(appRow.RootIsAppData);
        Assert.IsFalse(Path.IsPathRooted(appRow.RootPath));
        Assert.IsFalse(userRow.RootIsAppData);
        Assert.AreEqual(userRoot, userRow.RootPath);
        var relocated = Path.Combine(_root, "relocated-appdata");
        Directory.Move(_appData, relocated);
        _appData = relocated;
        Assert.IsNotNull(await _store.ResolveByIdAsync(appArtifact.Id));
        Assert.IsTrue((await _store.GetArtifactAsync(appArtifact.Id))!.RootDirectory.StartsWith(relocated));
        Assert.AreEqual(userRoot, (await _store.ResolveByIdAsync(userArtifact.Id))!.RootDirectory);
    }

    [TestMethod]
    public async Task Baidu_layout_artifacts_sharing_files_fence_each_others_consumers()
    {
        var physical = Path.Combine(_appData, "baidu-download");
        var wrapper = Path.Combine(physical, "bakabase-owned");
        var file = Path.Combine(wrapper, "author", "image.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "source");
        var source = new CloudSource {Id = "source", ProviderId = "baidu", Url = "https://pan.baidu.com/s/example"};
        var current = new CloudDownloadTarget {Id = "target", Sources = [source], SelectedSourceId = source.Id};
        var separate = current with {DownloadLayout = "separateDirectory"};
        var currentIntent = DownloadArtifactCoordinator.ArtifactIntent(current, physical);
        var separateIntent = DownloadArtifactCoordinator.ArtifactIntent(separate, physical);
        var first = await _store.AdoptAsync(new(currentIntent, currentIntent, "download", "source"),
            new(wrapper, [file], "download", Managed: true), "current");
        var second = await _store.AdoptAsync(new(separateIntent, separateIntent, "download", "source"),
            new(physical, [file], "download", Managed: true), "separate");
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.IsTrue(await _store.AttachConsumerAsync(second.OperationId, "separate-reader", LeaseTime));
        Assert.IsFalse(await _store.TryQuarantineAsync(first.Id));
        Assert.IsTrue(File.Exists(file));
        await _store.ReleaseConsumerAsync(second.OperationId, "separate-reader");
        Assert.IsTrue(await _store.TryQuarantineAsync(first.Id));
        Assert.IsFalse(await _store.AttachConsumerAsync(second.OperationId, "late-reader", LeaseTime));
    }

    [TestMethod]
    public async Task Subset_quarantine_respects_parent_and_shared_file_consumers_and_retains_fence_after_move()
    {
        var registration = await Files("archive");
        var unrelated = Path.Combine(registration.RootDirectory, "note.txt");
        await File.WriteAllTextAsync(unrelated, "do not move");
        var original = await Adopt("whole", registration with {Files = registration.Files.Append(unrelated).ToArray()});
        var alias = await Adopt("alias", registration);
        var subset = await Adopt("subset", registration with {ParentArtifactIds = new[] {original.Id}});
        Assert.IsTrue(await _store.AttachConsumerAsync(original.OperationId, "reader", LeaseTime));
        Assert.IsFalse(await _store.TryQuarantineAsync(subset.Id));
        await _store.ReleaseConsumerAsync(original.OperationId, "reader");
        Assert.IsTrue(await _store.AttachConsumerAsync(alias.OperationId, "reader-alias", LeaseTime));
        Assert.IsFalse(await _store.TryQuarantineAsync(subset.Id));
        await _store.ReleaseConsumerAsync(alias.OperationId, "reader-alias");
        Assert.IsTrue(await _store.TryQuarantineAsync(subset.Id));
        Assert.IsFalse(await _store.AttachConsumerAsync(original.OperationId, "late-reader", LeaseTime));
        var quarantine = Path.Combine(_appData, "quarantine", subset.Id);
        var relative = subset.Manifest.Entries.Single().RelativePath;
        var destination = Path.Combine(quarantine, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(subset.Files[0], destination);
        await _store.SetLocationAsync(subset.Id, quarantine, ArtifactLocationState.Quarantined);
        Assert.IsFalse(await _store.AttachConsumerAsync(alias.OperationId, "late-alias", LeaseTime));
        Assert.IsNull(await _store.ResolveByIdAsync(subset.Id));
        Assert.AreEqual("do not move", await File.ReadAllTextAsync(unrelated));
        File.Move(destination, subset.Files[0]);
        await _store.SetLocationAsync(subset.Id, registration.RootDirectory, ArtifactLocationState.Available);
        Assert.IsNotNull(await _store.ResolveByIdAsync(subset.Id));
        Assert.IsTrue(await _store.AttachConsumerAsync(original.OperationId, "after-restore", LeaseTime));
    }

    [TestMethod]
    public async Task Expired_consumer_does_not_block_cleanup_and_unmanaged_artifact_cannot_be_quarantined()
    {
        var managed = await Adopt("managed", await Files("managed"));
        Assert.IsTrue(await _store.AttachConsumerAsync(managed.OperationId, "expired", LeaseTime));
        await _db.Set<ArtifactConsumerDbModel>().ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        Assert.IsTrue(await _store.TryQuarantineAsync(managed.Id));
        var unmanaged = await Adopt("unmanaged", await Files("unmanaged", false));
        Assert.IsFalse(await _store.TryQuarantineAsync(unmanaged.Id));
    }

    [TestMethod]
    public async Task Waiting_workflow_pins_survive_lease_expiry_and_terminal_workflows_release_protection()
    {
        var output = await Adopt("pinned", await Files("pinned"));
        _db.Add(new WorkflowRunDbModel {Id = 72, WorkflowDefinitionId = 1, Status = WorkflowRunStatus.Waiting});
        await _db.SaveChangesAsync();
        Assert.IsTrue(await _store.PinConsumerAsync(output.OperationId, "workflow:72:input", "workflow", "72"));
        await _db.Set<ArtifactConsumerDbModel>().ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddDays(-2)));
        Assert.IsTrue(await _store.HasActiveConsumersAsync(output.OperationId));
        Assert.IsFalse(await _store.TryQuarantineAsync(output.Id));
        await _db.Set<WorkflowRunDbModel>().Where(r => r.Id == 72)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        Assert.IsFalse(await _store.HasActiveConsumersAsync(output.OperationId));
        Assert.IsTrue(await _store.TryQuarantineAsync(output.Id));
    }

    [TestMethod]
    public async Task Invalid_nonrestartable_result_requires_reconciliation_instead_of_reexecution()
    {
        var registration = await Files("remote-result");
        var claim = await _store.ClaimAsync(Request("nonrestartable", false), "owner", LeaseTime);
        var result = await _store.CompleteAsync(claim.Lease!, registration);
        File.Delete(result.Files[0]);
        var retry = await _store.ClaimAsync(Request("nonrestartable", false), "new-owner", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation, retry.Status);
        Assert.IsNull(retry.Lease);
    }

    [TestMethod]
    public async Task Shared_manifest_keeps_existing_download_fingerprint_and_does_not_adopt_siblings()
    {
        var files = await Files("legacy");
        await File.WriteAllTextAsync(Path.Combine(files.RootDirectory, "unrelated.txt"), "unrelated");
        var legacyEntries = new List<string[]>();
        foreach (var file in files.Files.OrderBy(f => f, StringComparer.Ordinal))
            legacyEntries.Add(new[] {Path.GetRelativePath(files.RootDirectory, file).Replace('\\', '/'),
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant()});
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(legacyEntries)))).ToLowerInvariant();
        var result = await new DownloadResultService(_db, () => _appData).RecordFilesAsync(1, ThirdPartyId.ExHentai,
            "legacy-source", "legacy", files.RootDirectory, files.Files.ToArray(), null);
        var artifact = await Adopt("legacy", files);
        Assert.AreEqual(expected, result.Fingerprint);
        Assert.AreEqual(result.Fingerprint, artifact.Fingerprint);
        Assert.AreEqual(1, artifact.Files.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_adoption_reuses_one_published_result(bool sameOwner)
    {
        var files = await Files("adoption");
        await using var secondDb = OpenDb();
        var second = Store(secondDb);
        var outputs = await Task.WhenAll(_store.AdoptAsync(Request("adopt"), files, "first"),
            second.AdoptAsync(Request("adopt"), files, sameOwner ? "first" : "second"));
        Assert.AreEqual(outputs[0].Id, outputs[1].Id);
        Assert.AreEqual(1, await _db.Set<ArtifactDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task Manifest_rejects_missing_outside_duplicate_and_symbolic_link_files()
    {
        var files = await Files("unsafe");
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => ArtifactManifestFiles.CaptureAsync(
            files.RootDirectory, new[] {Path.Combine(files.RootDirectory, "missing.txt")}));
        var outside = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtifactManifestFiles.CaptureAsync(files.RootDirectory, new[] {outside}));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtifactManifestFiles.CaptureAsync(files.RootDirectory,
            new[] {files.Files[0], files.Files[0]}));
        if (OperatingSystem.IsWindows()) return;
        var link = Path.Combine(files.RootDirectory, "linked.txt");
        File.CreateSymbolicLink(link, outside);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtifactManifestFiles.CaptureAsync(files.RootDirectory, new[] {link}));
        var manifest = await ArtifactManifestFiles.CaptureAsync(files.RootDirectory, files.Files.ToArray());
        File.Delete(files.Files[0]);
        File.CreateSymbolicLink(files.Files[0], outside);
        Assert.AreEqual(ArtifactLocationState.Modified, (await ArtifactManifestFiles.ValidateAsync(files.RootDirectory, manifest)).State);
    }
}
