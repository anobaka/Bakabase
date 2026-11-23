using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.Logging.Abstractions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class FileArtifactPublisherTests
{
    private string _root = null!;
    private string _appData = null!;
    private string _destination = null!;
    private ArtifactOperationStore _store = null!;
    private BakabaseDbContext _db = null!;
    private ServiceProvider _services = null!;
    private FileArtifactPublisher Publisher => new(_store, new Storage(), () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
    [TestInitialize] public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "artifact-publisher-" + Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "appdata"); _destination = Path.Combine(_root, "delivery");
        Directory.CreateDirectory(_appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(_root, "artifacts.db")).Options;
        _db = new(options); await _db.Database.EnsureCreatedAsync(); _store = new(_db, () => _appData);
        _services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => _appData)).BuildServiceProvider();
    }
    [TestCleanup] public async Task Cleanup()
    { await _services.DisposeAsync(); await _db.DisposeAsync(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private async Task<ArtifactRecord> Input()
    {
        var root = Path.Combine(_appData, "shared"); Directory.CreateDirectory(Path.Combine(root, "nested"));
        var first = Path.Combine(root, "one.txt"); var second = Path.Combine(root, "nested", "two.txt");
        await File.WriteAllTextAsync(first, "one"); await File.WriteAllTextAsync(second, "two");
        await File.WriteAllTextAsync(Path.Combine(root, "unselected.txt"), "private");
        return await _store.AdoptAsync(new("source", "source-intent", "processed", "source-fingerprint"),
            new(root, new[] {first, second}, Managed: true), "input");
    }

    [TestMethod] public async Task Copies_only_exact_manifest_and_keeps_shared_originals_and_existing_siblings()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var unrelated = Path.Combine(_destination, "existing.txt"); await File.WriteAllTextAsync(unrelated, "retain");
        var result = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNotNull(result.Artifact, result.Message); Assert.AreEqual("publishedFiles", result.Artifact.Kind);
        Assert.AreEqual(2, result.Artifact.Files.Count);
        CollectionAssert.AreEquivalent(new[] {"one.txt", Path.Combine("nested", "two.txt")},
            result.Artifact.Files.Select(f => Path.GetRelativePath(_destination, f)).ToArray());
        Assert.IsTrue(input.Files.All(File.Exists)); Assert.IsFalse(File.Exists(Path.Combine(_destination, "unselected.txt")));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unrelated));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, input.Manifest)).Available);
        Assert.IsFalse(Directory.EnumerateDirectories(_destination, ".bakabase-publish-*").Any());
        CollectionAssert.AreEqual(new[] {input.Id}, result.Artifact.ParentArtifactIds.ToArray());
        Assert.IsFalse(await _store.HasActiveConsumersAsync(input.OperationId));
    }

    [TestMethod] public async Task Publishes_registered_empty_directories_without_adopting_siblings_and_reuses_the_same_manifest()
    {
        var files = await Input();
        var save = Path.Combine(files.RootDirectory, "game", "Save"); Directory.CreateDirectory(save);
        Directory.CreateDirectory(Path.Combine(files.RootDirectory, "unregistered-empty"));
        var input = await _store.AdoptAsync(new("directory-source", "directory-source", "processed", "source"),
            new(files.RootDirectory, files.Files, Directories: [save]), "input");
        var result = await Publisher.PublishAsync("directory-processing", input, _destination, "publisher");
        Assert.IsNotNull(result.Artifact, result.Message);
        CollectionAssert.AreEqual(new[] {"game/Save"}, result.Artifact.Manifest.Directories!.ToArray());
        Assert.AreEqual(input.Fingerprint, result.Artifact.Fingerprint);
        Assert.IsTrue(Directory.Exists(Path.Combine(_destination, "game", "Save")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_destination, "unregistered-empty")));
        Directory.Delete(input.RootDirectory, true);
        Assert.AreEqual(result.Artifact.Id,
            (await Publisher.PublishAsync("directory-processing", input, _destination, "reuse")).Artifact?.Id);
        Directory.Delete(Path.Combine(_destination, "game", "Save"));
        Assert.IsNull(await Publisher.ResolveAsync("directory-processing", _destination));
    }

    [TestMethod] public async Task Registered_directory_conflicting_with_a_user_file_blocks_publication_without_overwriting()
    {
        var files = await Input();
        var save = Path.Combine(files.RootDirectory, "Save"); Directory.CreateDirectory(save);
        var input = await _store.AdoptAsync(new("directory-source", "directory-source", "processed", "source"),
            new(files.RootDirectory, files.Files, Directories: [save]), "input");
        Directory.CreateDirectory(_destination);
        await File.WriteAllTextAsync(Path.Combine(_destination, "Save"), "keep");
        var result = await Publisher.PublishAsync("directory-processing", input, _destination, "publisher");
        Assert.IsNull(result.Artifact); Assert.AreEqual("PublicationConflict", result.WaitReason);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(_destination, "Save")));
        Assert.IsTrue(input.Files.All(File.Exists));
    }

    [TestMethod] public void Registered_files_and_directories_must_not_overlap_owned_staging()
    {
        var staging = Path.Combine(_destination, ".bakabase-publish-operation");
        Assert.ThrowsException<InvalidDataException>(() => FileArtifactPublisher.EnsureOutsideStaging(staging, [staging]));
        Assert.ThrowsException<InvalidDataException>(() => FileArtifactPublisher.EnsureOutsideStaging(staging, [Path.Combine(staging, "empty")]));
        FileArtifactPublisher.EnsureOutsideStaging(staging, [staging + "-authored", Path.Combine(_destination, "Save")]);
        Assert.IsFalse(Directory.Exists(_destination));
    }

    [TestMethod] public async Task Verified_delivery_can_be_reused_after_all_temporary_source_files_are_removed()
    {
        var input = await Input();
        var result = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        foreach (var path in input.Files) File.Delete(path);
        Assert.IsNull(await _store.ResolveByIdAsync(input.Id));
        var restoredPublisher = Publisher;
        Assert.AreEqual(result.Artifact!.Id, (await restoredPublisher.ResolveAsync("processing-intent", _destination))!.Id);
        var reused = await restoredPublisher.PublishAsync("processing-intent", input, _destination, "another-run");
        Assert.AreEqual(result.Artifact.Id, reused.Artifact!.Id);
        await File.WriteAllTextAsync(reused.Artifact.Files[0], "changed");
        Assert.IsNull(await Publisher.ResolveAsync("processing-intent", _destination));
    }

    [TestMethod] public async Task Projected_publication_records_real_mapping_and_is_reusable_without_the_original_artifact()
    {
        var root = Path.Combine(_appData, "projected"); var file = Path.Combine(root, "system", "Author", "images", "a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "verified payload");
        var save = Path.Combine(root, "system", "Author", "Save"); Directory.CreateDirectory(save);
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, [file], directories: [save]);
        var provenance = FileArtifactDeliveryLayout.Bind(manifest, [new("system/Author/images/a.jpg", false, ["system"]),
            new("system/Author/Save", true, ["system"])]);
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null, "{}", null, DirectoryProvenance: provenance), WorkflowJson.Options);
        var input = await _store.AdoptAsync(new("project-source", "project-source", "process", "projected"),
            new(root, [file], "processedFiles", true, MetadataJson: metadata, Directories: [save]), "input");
        var policy = new ArtifactDeliveryPolicy {CollapseSingleRootDirectory = true};
        var result = await Publisher.PublishAsync("project", input, _destination, "publisher", deliveryPolicy: policy);
        Assert.IsNotNull(result.Artifact, result.Message); var published = result.Artifact;
        Assert.IsTrue(File.Exists(file)); Assert.IsTrue(File.Exists(Path.Combine(_destination, "images", "a.jpg")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_destination, "Save"))); Assert.IsFalse(Directory.Exists(Path.Combine(_destination, "system")));
        var receipt = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(published.MetadataJson!, WorkflowJson.Options)!;
        Assert.IsTrue(FileDispositionProofs.ValidFor(receipt.FileDisposition, input, published));
        Assert.AreEqual("preserved", receipt.FileDisposition!.Entries.Single().Disposition);
        Assert.AreEqual("images/a.jpg", receipt.FileDisposition.Entries.Single().Outputs.Single().RelativePath);
        Assert.AreEqual(input.Fingerprint, receipt.DeliveryLayout!.InputFingerprint);
        Directory.Delete(root, true);
        Assert.AreEqual(published.Id, (await Publisher.ResolveAsync("project", _destination, deliveryPolicy: policy))?.Id);
        Assert.IsNull(await Publisher.ResolveAsync("project", _destination, deliveryPolicy: new()));
        Assert.IsNull(await Publisher.ResolveAsync("project", _destination));
    }

    [TestMethod] public async Task Changed_policy_does_not_create_a_second_tree_beside_an_existing_delivery()
    {
        var root = Path.Combine(_appData, "projected"); var file = Path.Combine(root, "old", "a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "original");
        var input = await _store.AdoptAsync(new("projection-input", "projection-input", "processed", "projection"),
            new(root, [file], "processedFiles", true), "input");
        Assert.IsNotNull((await Publisher.PublishAsync("same", input, _destination, "consumer")).Artifact);
        var newer = await Publisher.PublishAsync("same", input, _destination, "consumer", deliveryPolicy: new() {CollapseSingleRootDirectory = true});
        Assert.AreEqual("PublicationConflict", newer.WaitReason); Assert.IsNull(newer.Artifact);
        Assert.IsFalse(File.Exists(Path.Combine(_destination, "a.jpg"))); Assert.IsTrue(File.Exists(Path.Combine(_destination, "old", "a.jpg")));
        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod] public async Task Existing_different_files_are_never_overwritten_and_another_target_can_be_selected()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var conflict = Path.Combine(_destination, "one.txt"); await File.WriteAllTextAsync(conflict, "user-file");
        var result = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNull(result.Artifact); Assert.AreEqual("PublicationConflict", result.WaitReason);
        Assert.AreEqual("user-file", await File.ReadAllTextAsync(conflict)); Assert.IsTrue(input.Files.All(File.Exists));
        Assert.IsNotNull((await Publisher.PublishAsync("processing-intent", input, _destination + "-other", "publisher")).Artifact);
    }

    [TestMethod] public async Task Relocated_delivery_reuses_the_existing_receipt_without_copying_missing_originals()
    {
        var input = await Input();
        var published = (await Publisher.PublishAsync("processing-intent", input, _destination, "publisher")).Artifact!;
        var moved = Path.Combine(_root, "nas", "delivered");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(_destination, moved);
        // Simulate the verified offline migration: payload moved, references rebased, IDs retained.
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == published.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RootPath, moved).SetProperty(a => a.RootIsAppData, false));
        var mapDirectory = Path.Combine(_appData, "artifact-workspaces");
        Directory.CreateDirectory(mapDirectory);
        await File.WriteAllTextAsync(Path.Combine(mapDirectory, "relocations.json"), JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(1, [new(_destination, moved)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Directory.Delete(input.RootDirectory, true);
        var noCopies = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), (_, _, _) => throw new AssertFailedException("A delivered artifact must be reused."));
        var reused = await noCopies.PublishAsync("processing-intent", input, moved, "after-migration");
        Assert.AreEqual(published.Id, reused.Artifact?.Id);
        Assert.AreEqual(moved, reused.Artifact?.RootDirectory);
        Assert.AreEqual(published.Id, (await noCopies.ResolveAsync("processing-intent", moved))?.Id);
        Assert.AreEqual(1, await _db.Set<ArtifactOperationDbModel>().CountAsync(o => o.Kind == "files.publish"));
    }

    [TestMethod] public async Task Relocated_download_preserves_identity_without_collapsing_distinct_sources_or_sibling_directories()
    {
        var oldRoot = Path.Combine(_root, "old-downloads");
        var newRoot = Path.Combine(_root, "nas", "downloads");
        var metadata = Path.Combine(_appData, "artifact-workspaces");
        Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "relocations.json"), JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(1, [new(oldRoot, newRoot)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        var source = new CloudSource {Id = "source", ProviderId = "http", Url = "https://example.invalid/one.zip", Kind = "http"};
        var target = new CloudDownloadTarget {Id = "target", Sources = [source], SelectedSourceId = source.Id};
        var before = DownloadArtifactCoordinator.DownloadIntent(target, oldRoot);
        Assert.AreEqual(before, DownloadArtifactCoordinator.DownloadIntent(target, newRoot, resolver));
        Assert.AreNotEqual(before, DownloadArtifactCoordinator.DownloadIntent(target, newRoot + "-other", resolver));
        Assert.AreNotEqual(before, DownloadArtifactCoordinator.DownloadIntent(target with {AcquisitionNonce = "new-generation"}, newRoot, resolver));
        Assert.AreNotEqual(before, DownloadArtifactCoordinator.DownloadIntent(target with {Sources = [source with {Url = "https://example.invalid/two.zip"}]}, newRoot, resolver));
    }

    [TestMethod] public async Task Same_size_and_timestamp_cannot_substitute_for_the_expected_publication_hash()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var original = input.Files.Single(path => Path.GetFileName(path) == "one.txt");
        var conflict = Path.Combine(_destination, "one.txt");
        await File.WriteAllTextAsync(conflict, "bad");
        File.SetLastWriteTimeUtc(conflict, File.GetLastWriteTimeUtc(original));
        Assert.AreEqual(new FileInfo(original).Length, new FileInfo(conflict).Length);
        var result = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNull(result.Artifact); Assert.AreEqual("PublicationConflict", result.WaitReason);
        Assert.AreEqual("bad", await File.ReadAllTextAsync(conflict));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest)).Available);
    }

    [TestMethod] public async Task Conflict_releases_its_lease_so_a_new_consumer_can_retry_immediately()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var path = Path.Combine(_destination, "one.txt"); await File.WriteAllTextAsync(path, "conflict");
        var waiting = await Publisher.PublishAsync("processing-intent", input, _destination, "first-consumer");
        Assert.AreEqual("PublicationConflict", waiting.WaitReason);
        File.Delete(path);
        var resumed = await Publisher.PublishAsync("processing-intent", input, _destination, "second-consumer");
        Assert.IsNotNull(resumed.Artifact, resumed.Message);
        Assert.AreEqual("one", await File.ReadAllTextAsync(path));
    }

    [TestMethod] public async Task Copy_io_failure_is_diagnosable_and_retry_reuses_intact_source_without_overwriting_user_files()
    {
        var input = await Input();
        Directory.CreateDirectory(_destination);
        var unrelated = Path.Combine(_destination, "user.txt");
        await File.WriteAllTextAsync(unrelated, "retain");
        var failing = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), async (_, output, ct) =>
            {
                await output.WriteAsync(new byte[] {1}, ct);
                throw new IOException("Bad address: PRIVATE_FILE_PATH; remote text mentioned LOCAL_STORAGE_RESERVE_LOW");
            });

        var wait = await failing.PublishAsync("processing-intent", input, _destination, "first-consumer");

        Assert.IsNull(wait.Artifact);
        Assert.AreEqual("StorageUnavailable", wait.WaitReason);
        StringAssert.StartsWith(wait.Message!, "PUBLICATION_IO_ERROR:");
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest)).Available);
        Assert.IsFalse(File.Exists(Path.Combine(_destination, "one.txt")));
        var workset = new DownloadWorkset {Targets = [new() {Id = "files", Problem = wait.Message}]};
        var warning = workset.GetWorkflowOutputMetadata("fs.publishArtifacts").Single();
        Assert.AreEqual("PUBLICATION_IO_ERROR", warning.Code);
        Assert.IsFalse(JsonSerializer.Serialize(warning).Contains("PRIVATE_FILE_PATH"));

        var waitingOperation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.publish");
        Assert.AreEqual(ArtifactOperationState.Running, waitingOperation.State);
        Assert.AreEqual(1, waitingOperation.Attempt);
        var resumed = await Publisher.PublishAsync("processing-intent", input, _destination, "first-consumer");
        Assert.IsNotNull(resumed.Artifact, resumed.Message);
        Assert.AreEqual(1, (await _store.GetOperationAsync(waitingOperation.Id))!.Attempt);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unrelated));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, input.Manifest)).Available);
        Assert.IsTrue(input.Files.All(File.Exists));
        Assert.IsFalse(Directory.EnumerateDirectories(_destination, ".bakabase-publish-*").Any());
    }

    [DataTestMethod]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW", false)]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW", true)]
    [DataRow("LOCAL_STORAGE_FULL", false)]
    [DataRow("LOCAL_STORAGE_ACCESS_DENIED", false)]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE", false)]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED", false)]
    public async Task Typed_storage_failures_retain_their_specific_workflow_code(string code, bool wrapped)
    {
        var input = await Input();
        var publisher = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), (_, _, _) =>
            {
                if (wrapped) throw new IOException("PRIVATE_FILE_PATH", new LocalStorageException(code));
                throw new LocalStorageException(code);
            });

        var waiting = await publisher.PublishAsync("processing-intent", input, _destination, "publisher");

        Assert.IsNull(waiting.Artifact);
        Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
        Assert.AreEqual(code, waiting.Message);
        var workset = new DownloadWorkset {Targets = [new() {Id = "files", Problem = waiting.Message}]};
        Assert.AreEqual(code, workset.GetWorkflowOutputMetadata("fs.publishArtifacts").Single().Code);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest)).Available);
        Assert.IsFalse(File.Exists(Path.Combine(_destination, "one.txt")));
    }

    [TestMethod] public async Task Unreadable_input_waits_for_storage_without_reporting_missing_files_or_creating_publication()
    {
        var input = await Input();
        await using (var unavailable = new FileStream(input.Files[0], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var waiting = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
            Assert.IsNull(waiting.Artifact);
            Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
            Assert.IsFalse(await _db.Set<ArtifactOperationDbModel>().AnyAsync(o => o.Kind == "files.publish"));
        }
        Assert.IsNotNull((await Publisher.PublishAsync("processing-intent", input, _destination, "publisher")).Artifact);
    }

    [TestMethod] public async Task Unreadable_final_verification_keeps_the_same_generation_and_reuses_already_delivered_files()
    {
        var input = await Input();
        var copies = 0;
        FileStream? unavailable = null;
        var publisher = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), async (source, destination, ct) =>
            {
                await source.CopyToAsync(destination, ct);
                if (++copies == input.Files.Count)
                    unavailable = new FileStream(Path.Combine(_destination, input.Manifest.Entries[0].RelativePath),
                        FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });
        try
        {
            var waiting = await publisher.PublishAsync("processing-intent", input, _destination, "publisher");
            Assert.IsNull(waiting.Artifact);
            Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
            var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.publish");
            Assert.AreEqual(ArtifactOperationState.Running, operation.State);
            Assert.AreEqual(1, operation.Attempt);
        }
        finally { if (unavailable != null) await unavailable.DisposeAsync(); }
        var noAdditionalCopies = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), (_, _, _) => throw new AssertFailedException("Delivered bytes must be reused."));
        var resumed = await noAdditionalCopies.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNotNull(resumed.Artifact, resumed.Message);
        Assert.AreEqual(1, (await _store.GetOperationAsync(resumed.Artifact.OperationId))!.Attempt);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, input.Manifest)).Available);
    }

    [TestMethod] public async Task Unrecognized_staging_directories_and_their_files_are_preserved()
    {
        var input = await Input();
        var key = ArtifactManifestFiles.Hash(FileArtifactPublisher.Intent("processing-intent", _destination) + "\n" + input.Fingerprint);
        var stage = Path.Combine(_destination, ".bakabase-publish-" + key, "files"); Directory.CreateDirectory(stage);
        var unexpected = Path.Combine(stage, "one.txt"); await File.WriteAllTextAsync(unexpected, "user-data");
        var waiting = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNull(waiting.Artifact); Assert.AreEqual("PublicationConflict", waiting.WaitReason);
        Assert.AreEqual("user-data", await File.ReadAllTextAsync(unexpected));
        Assert.IsFalse(File.Exists(Path.Combine(_destination, "one.txt")));
    }

    [TestMethod] public async Task Restart_adopts_hash_matching_committed_file_without_rewriting_it()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var first = Path.Combine(_destination, "one.txt"); await File.WriteAllTextAsync(first, "one");
        var written = File.GetLastWriteTimeUtc(first);
        var intent = FileArtifactPublisher.Intent("processing-intent", _destination);
        var key = ArtifactManifestFiles.Hash(intent + "\n" + input.Fingerprint);
        var stagingRoot = Path.Combine(_destination, ".bakabase-publish-" + key);
        var stage = Path.Combine(stagingRoot, "files", "nested"); Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, ".owner"), key + ":" + input.Fingerprint);
        await File.WriteAllTextAsync(Path.Combine(stage, "two.txt"), "partial");
        var result = await Publisher.PublishAsync("processing-intent", input, _destination, "publisher");
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(first));
        Assert.AreEqual("two", await File.ReadAllTextAsync(Path.Combine(_destination, "nested", "two.txt")));
    }

    [TestMethod] public async Task Application_data_and_symbolic_link_targets_are_rejected_before_publication()
    {
        var input = await Input();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Publisher.PublishAsync("processing-intent", input, Path.Combine(_appData, "public"), "publisher"));
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_destination);
            var link = Path.Combine(_root, "delivery-link"); Directory.CreateSymbolicLink(link, _destination);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Publisher.PublishAsync("processing-intent", input, link, "publisher"));
        }
        Assert.IsTrue(input.Files.All(File.Exists));
    }
    [TestMethod] public async Task Acquisition_planning_reuses_cloud_intent_publication_after_raw_and_internal_results_are_removed()
    {
        var input = await Input();
        var source = new CloudSource {Id = "source", ProviderId = "http", Kind = "http", Url = "https://example.test/files.zip"};
        var target = new CloudDownloadTarget {Id = "cloud-target", Sources = [source], SelectedSourceId = source.Id};
        var downloadIntent = DownloadArtifactCoordinator.DownloadIntent(target, Path.Combine(_root, "downloads"));
        const string recipe = "{\"requirement\":\"notRequired\",\"steps\":[]}";
        var config = new FileArtifactProcessingConfig();
        var intent = FileArtifactProcessor.Intent(downloadIntent, recipe, null, config);
        var archives = new NoExtraction();
        var processor = new FileArtifactProcessor(_store, new(archives), new(archives), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>());
        var processed = (await processor.ExecuteAsync(intent, input, recipe, null, config, "cloud-consumer", null, null, null, default)).Artifact!;
        var published = (await Publisher.PublishAsync(intent, processed, _destination, "cloud-publisher")).Artifact!;
        foreach (var path in input.Files.Concat(processed.Files)) File.Delete(path);
        Assert.IsNull(await _store.ResolveByIdAsync(input.Id)); Assert.IsNull(await _store.ResolveByIdAsync(processed.Id));
        var before = await _db.Set<ArtifactOperationDbModel>().CountAsync();
        using var services = new ServiceCollection().AddSingleton<IArtifactOperationStore>(_store).AddSingleton(processor)
            .AddSingleton(Publisher).AddSingleton<IUserStoragePolicy>(new NoUserFileAccess())
            .AddSingleton<IWorkflowActivityRegistry>(new WorkflowActivityRegistry([new PublishFileArtifactsActivity()])).BuildServiceProvider();
        var item = new AcquisitionWorkItem {ResourceId = 41, LeadKind = AcquisitionLeadKind.SharedPage, LeadValue = source.Url,
            WorkingDirectory = Path.Combine(_root, "not-downloaded"), TargetDirectory = _destination, ExtractionPlanJson = recipe,
            Links = [new(source.Url)], SelectedLinkIndex = 0, Variables = new Dictionary<string, string> { ["download.intentKey"] = downloadIntent }};
        var context = new WorkflowExecutionContext {RunId = 901, WorkflowDefinitionId = 1, TriggerKind = "acquisition.requested",
            Payload = item, ActivityConfigJson = "{}", FollowingActivities = [new("fs.publishArtifacts", "{}")], Services = services, Logger = NullLogger.Instance};
        var planned = (AcquisitionWorkItem)await new ProcessLocalFileArtifactsActivity().TrySatisfyAsync(context, item, "{}", default);
        Assert.IsTrue(planned.AlreadyProcessed); Assert.AreEqual(published.Id, planned.Variables["artifact.outputId"]);
        Assert.AreEqual(published.Id, planned.Variables["artifact.publishedId"]);
        Assert.AreEqual(_destination, planned.ExtractedDirectory);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, published.Manifest)).Available);
        var executed = await new ProcessLocalFileArtifactsActivity().ProcessItemAsync(context, planned, default);
        Assert.IsNull(executed.Suspension);
        Assert.IsTrue(((AcquisitionWorkItem)executed.Replacement!).AlreadyProcessed);
        Assert.AreEqual(before, await _db.Set<ArtifactOperationDbModel>().CountAsync(), "Planning and consuming a valid publication must not create another download or processing operation.");
    }
    [TestMethod]
    public async Task DynamicAcquisitionNodesReuseLegacyPublicationAfterRawAndProcessedInputsAreCleaned()
    {
        var input = await Input();
        var source = new CloudSource {Id = "source", ProviderId = "http", Kind = "http", Url = "https://example.test/files.zip"};
        var target = new CloudDownloadTarget {Id = "main", Sources = [source], SelectedSourceId = source.Id};
        var workset = new DownloadWorkset {Id = "new-acquisition", OutputDirectory = Path.Combine(_root, "downloads"), Targets = [target]};
        const string recipe = "{\"requirement\":\"notRequired\",\"steps\":[]}";
        var config = new FileArtifactProcessingConfig();
        var intent = FileArtifactProcessor.Intent(DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory), recipe, null, config);
        var archives = new NoExtraction();
        var processor = new FileArtifactProcessor(_store, new(archives), new(archives), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>());
        var processed = (await processor.ExecuteAsync(intent, input, recipe, null, config, "legacy", null, null, null, default)).Artifact!;
        var published = (await Publisher.PublishAsync(intent, processed, _destination, "legacy-publication")).Artifact!;
        foreach (var path in input.Files.Concat(processed.Files)) File.Delete(path);
        var before = await _db.Set<ArtifactOperationDbModel>().CountAsync();
        var activity = new Bakabase.Service.Components.Acquisition.Planning.AcquisitionPlanProcessingActivity();
        var publicationConfig = JsonSerializer.Serialize(new FileArtifactPublicationConfig {TargetDirectory = _destination}, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var nodeConfig = JsonSerializer.Serialize(new Bakabase.Service.Components.Acquisition.Planning.AcquisitionPlanProcessingConfig("main", new() {Requirement = "notRequired"}),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var services = new ServiceCollection().AddSingleton<IArtifactOperationStore>(_store).AddSingleton(processor)
            .AddSingleton<IFileArtifactProcessor>(processor)
            .AddSingleton(Publisher).AddSingleton<IUserStoragePolicy>(new NoUserFileAccess())
            .AddSingleton<IWorkflowActivityRegistry>(new WorkflowActivityRegistry([activity, new PublishFileArtifactsActivity()])).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = 902, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = nodeConfig, FollowingActivities = [new("fs.publishArtifacts", publicationConfig)], Services = services, Logger = NullLogger.Instance};
        var planned = (DownloadWorkset)await activity.TrySatisfyAsync(context, workset, nodeConfig, default);
        Assert.AreEqual(published.Id, planned.Targets.Single().PublishedArtifactId);
        Assert.AreEqual(published.Id, planned.Targets.Single().ProcessedArtifactId);
        var execution = await activity.ProcessItemAsync(context, planned, default);
        Assert.IsNull(execution.Suspension);
        Assert.AreEqual(before, await _db.Set<ArtifactOperationDbModel>().CountAsync());
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, published.Manifest)).Available);
    }

    private sealed class NoExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords, Action<string>? output, CancellationToken ct) => throw new AssertFailedException("No extraction is authorized by this plan.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) => throw new AssertFailedException("No extraction is authorized by this plan.");
    }
    private sealed class NoUserFileAccess : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => false;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => throw new AssertFailedException("Ledger-owned artifacts must not be treated as new user file inputs.");
        public void EnsureTreeMutationAllowed(string path) => throw new AssertFailedException("Shared files must not be moved.");
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
