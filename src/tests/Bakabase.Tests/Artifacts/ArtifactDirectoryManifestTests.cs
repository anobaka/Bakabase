using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactDirectoryManifestTests
{
    private string _root = null!;
    private string _input = null!;
    private string _archive = null!;
    private readonly Archives _archives = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "artifact-directories-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
        _archive = Path.Combine(_input, "outer.zip");
        await File.WriteAllTextAsync(_archive, "outer");
    }

    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task Legacy_fingerprint_is_unchanged_and_legacy_capture_never_adopts_empty_siblings()
    {
        Directory.CreateDirectory(Path.Combine(_input, "unregistered"));
        var legacy = await ArtifactManifestFiles.CaptureAsync(_input, [_archive]);
        Assert.IsNull(legacy.Directories);
        Assert.AreEqual(ArtifactManifestFiles.Fingerprint(legacy.Entries), legacy.Fingerprint);
        Assert.AreEqual(legacy.Fingerprint, ArtifactManifestFiles.Fingerprint(legacy.Entries, []));
        Assert.AreEqual(0, ArtifactManifestFiles.ResolveDirectories(_input, legacy).Count);
        var roundTrip = JsonSerializer.Deserialize<ArtifactManifest>(JsonSerializer.Serialize(legacy, Json), Json)!;
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_input, roundTrip)).Available);
    }

    [TestMethod]
    public async Task Explicit_empty_directory_is_fingerprinted_and_missing_directory_invalidates_reuse()
    {
        var save = Path.Combine(_input, "game", "Save"); Directory.CreateDirectory(save);
        var manifest = await ArtifactManifestFiles.CaptureAsync(_input, [_archive], directories: [save]);
        CollectionAssert.AreEqual(new[] {"game/Save"}, manifest.Directories!.ToArray());
        Assert.AreNotEqual(ArtifactManifestFiles.Fingerprint(manifest.Entries), manifest.Fingerprint);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_input, manifest)).Available);
        Directory.Delete(save);
        Assert.AreEqual(ArtifactLocationState.Missing, (await ArtifactManifestFiles.ValidateAsync(_input, manifest)).State);
        await File.WriteAllTextAsync(save, "user file");
        Assert.AreEqual(ArtifactLocationState.Modified, (await ArtifactManifestFiles.ValidateAsync(_input, manifest)).State);
    }

    [DataTestMethod]
    [DataRow("../escape")]
    [DataRow("/absolute")]
    [DataRow("a\\b")]
    [DataRow("x:y")]
    [DataRow("a//b")]
    [DataRow("outer.zip/child")]
    [DataRow("outer.zip")]
    public async Task Unsafe_or_file_colliding_directory_manifest_is_rejected_before_creation(string directory)
    {
        var source = await ArtifactManifestFiles.CaptureAsync(_input, [_archive]);
        var manifest = source with {Directories = [directory],
            Fingerprint = ArtifactManifestFiles.Fingerprint(source.Entries, [directory])};
        var target = Path.Combine(_root, "target");
        Assert.ThrowsException<InvalidDataException>(() => ArtifactManifestFiles.CreateDirectories(target, manifest));
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task Directory_source_and_destination_links_and_existing_files_are_never_followed_or_overwritten()
    {
        if (OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var save = Path.Combine(_input, "Save"); Directory.CreateSymbolicLink(save, outside);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            ArtifactManifestFiles.CaptureAsync(_input, [_archive], directories: [save]));
        Directory.Delete(save); Directory.CreateDirectory(save);
        var manifest = await ArtifactManifestFiles.CaptureAsync(_input, [_archive], directories: [save]);
        var target = Path.Combine(_root, "target"); Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(target, "Save"), outside);
        Assert.ThrowsException<InvalidDataException>(() => ArtifactManifestFiles.CreateDirectories(target, manifest));
        Directory.Delete(Path.Combine(target, "Save"));
        await File.WriteAllTextAsync(Path.Combine(target, "Save"), "retain");
        Assert.ThrowsException<InvalidDataException>(() => ArtifactManifestFiles.CreateDirectories(target, manifest));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(Path.Combine(target, "Save")));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(outside).Count());
    }

    private static FileProcessingPlan Plan() => new() {Requirement = "required", Steps = [
        new() {Id = "outer", Op = "extractArchive"},
        new() {Id = "inner", Op = "extractArchive", Input = "outer", Selector = "inner.zip"},
        new() {Id = "name", Op = "renameFile", Input = "inner", Selector = "game.bin", TargetName = "renamed.bin"}]};

    [TestMethod]
    public async Task Dynamic_boundaries_nested_extraction_and_rename_preserve_author_empty_leaves_on_replay()
    {
        var executor = new FileProcessingPlanExecutor(_archives);
        var checkpoint = Path.Combine(_root, "plan");
        var outer = await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint, stopAfterStepId: "outer");
        Assert.IsTrue(outer.Completed, outer.Message);
        Assert.IsTrue(Directory.Exists(Path.Combine(outer.OutputDirectory!, "outer", "Save")));
        var final = await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint);
        Assert.IsTrue(final.Completed, final.Message);
        CollectionAssert.AreEquivalent(new[] {"outer/Save", "outer/inner/plugins"},
            final.Directories!.Select(path => Path.GetRelativePath(final.OutputDirectory!, path).Replace('\\', '/')).ToArray());
        Assert.AreEqual(2, _archives.Calls);
        Assert.IsTrue(final.Files.Any(path => Path.GetFileName(path) == "renamed.bin"));
        var repeated = await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint);
        Assert.AreEqual(2, _archives.Calls);
        Assert.AreEqual(final.FileLineage!.OutputManifest!.Fingerprint, repeated.FileLineage!.OutputManifest!.Fingerprint);
        Directory.Delete(final.Directories[0]);
        try
        {
            await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint);
            Assert.Fail("Missing registered directories must invalidate the completed checkpoint.");
        }
        catch (IOException) { }
        Assert.AreEqual(2, _archives.Calls, "A missing directory must not repeat extraction.");
    }

    [DataTestMethod]
    [DataRow("currentDirectory", "collection/inner.zip", "collection/Save")]
    [DataRow("archiveDirectory", "collection/outer/inner.zip", "collection/outer/Save")]
    public async Task ExplicitExtractionLayoutKeepsArchiveMembersAndNeverMarksTheChosenDirectoryGenerated(
        string layout, string expectedFile, string expectedEmpty)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_input, "collection")).FullName;
        var archive = Path.Combine(directory, "outer.zip");
        File.Move(_archive, archive);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", ExtractionLayout = layout}]};
        var executor = new FileProcessingPlanExecutor(_archives);
        var result = await executor.ExecuteAsync(plan, _input, [archive], Path.Combine(_root, "explicit"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(expectedFile, Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()).Replace('\\', '/'));
        Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, expectedEmpty)));
        Assert.IsFalse(result.DirectoryProvenance?.Entries.Any(e => e.GeneratedAncestors.Count != 0) == true);
        var replay = await executor.ExecuteAsync(plan, _input, [archive], Path.Combine(_root, "explicit"));
        Assert.AreEqual(result.OutputDirectory, replay.OutputDirectory);
        Assert.AreEqual(1, _archives.Calls);
    }

    [TestMethod]
    public async Task ExtractHereWithAConflictingSiblingWaitsAndPreservesBothInputs()
    {
        var sibling = Path.Combine(_input, "inner.zip");
        await File.WriteAllTextAsync(sibling, "independent file");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "extract", Op = "extractArchive", Selector = "outer.zip", ExtractionLayout = "currentDirectory"}]};
        var executor = new FileProcessingPlanExecutor(_archives);
        var result = await executor.ExecuteAsync(plan, _input, [_archive, sibling], Path.Combine(_root, "conflict"));
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.AreEqual("PlannedTargetCollision", result.PathConflict?.Kind);
        Assert.AreEqual("independent file", await File.ReadAllTextAsync(sibling));
        Assert.AreEqual("outer", await File.ReadAllTextAsync(_archive));
    }

    [TestMethod]
    public async Task Next_processing_operation_accepts_only_registered_directories_and_does_not_scan_siblings()
    {
        var registered = Path.Combine(_input, "Save"); Directory.CreateDirectory(registered);
        Directory.CreateDirectory(Path.Combine(_input, "unregistered"));
        var result = await new FileProcessingPlanExecutor(_archives).ExecuteAsync(new() {Requirement = "notRequired"},
            _input, [_archive], Path.Combine(_root, "passthrough"), inputDirectories: [registered]);
        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEquivalent(new[] {"Save"}, result.Directories!.Select(Path.GetFileName).ToArray());
        Assert.IsFalse(Directory.Exists(Path.Combine(result.OutputDirectory!, "unregistered")));
        Assert.IsTrue(FileDispositionProofs.SameManifest(result.FileLineage!.InputManifest, result.FileLineage.OutputManifest));
    }

    [TestMethod]
    public async Task Completed_legacy_checkpoint_never_mints_directory_evidence_from_a_new_scan()
    {
        var executor = new FileProcessingPlanExecutor(_archives);
        var checkpoint = Path.Combine(_root, "plan");
        var complete = await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint);
        var stateFile = Path.Combine(Path.GetDirectoryName(complete.OutputDirectory!)!, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        ArtifactManifest? Legacy(ArtifactManifest? value) => value == null ? null :
            new(value.Entries, ArtifactManifestFiles.Fingerprint(value.Entries));
        state.DirectoryVersion = 0; state.ActiveDirectories = [];
        state.InputManifest = Legacy(state.InputManifest); state.FinalManifest = Legacy(state.FinalManifest);
        foreach (var operation in state.Operations.Values) operation.OutputManifest = Legacy(operation.OutputManifest);
        await File.WriteAllTextAsync(stateFile, JsonSerializer.Serialize(state, Json));
        Directory.CreateDirectory(Path.Combine(complete.OutputDirectory!, "later-unregistered"));
        var replay = await executor.ExecuteAsync(Plan(), _input, [_archive], checkpoint);
        Assert.IsTrue(replay.Completed); Assert.AreEqual(0, replay.Directories!.Count);
        Assert.AreEqual(state.FinalManifest!.Fingerprint, replay.FileLineage!.OutputManifest!.Fingerprint);
        Assert.AreEqual(2, _archives.Calls);
    }

    [TestMethod]
    public async Task Recursive_layers_preserve_empty_directories_through_budget_resume_and_completed_replay()
    {
        var executor = new RecursiveArchiveExecutor(_archives);
        var state = Path.Combine(_root, "recursive");
        var limited = new RecursiveArchiveOptions {MinimumFreeBytes = 0, MaxDepth = 1};
        var first = await executor.ExecuteAsync(_input, [_archive], state, limited);
        Assert.IsFalse(first.Completed); Assert.AreEqual("DepthLimit", first.WaitReason);
        Assert.AreEqual(1, _archives.Calls);
        var done = await executor.ExecuteAsync(_input, [_archive], state, limited with {MaxDepth = 8});
        Assert.IsTrue(done.Completed, done.Message);
        CollectionAssert.AreEquivalent(new[] {"outer/Save", "outer/inner/plugins"}, done.Directories!
            .Select(path => Path.GetRelativePath(done.OutputDirectory!, path).Replace('\\', '/')).ToArray());
        Assert.AreEqual(2, _archives.Calls);
        var again = await executor.ExecuteAsync(_input, [_archive], state, limited with {MaxDepth = 8});
        Assert.IsTrue(again.Completed); Assert.AreEqual(2, _archives.Calls);
        Assert.AreEqual(done.FileLineage!.OutputManifest!.Fingerprint, again.FileLineage!.OutputManifest!.Fingerprint);
    }

    [TestMethod]
    public async Task Recursive_checkpoint_rejects_changed_explicit_directory_inputs_without_reexecution()
    {
        var executor = new RecursiveArchiveExecutor(_archives);
        var save = Directory.CreateDirectory(Path.Combine(_input, "Save")).FullName;
        var state = Path.Combine(_root, "recursive");
        var options = new RecursiveArchiveOptions {MinimumFreeBytes = 0};
        var complete = await executor.ExecuteAsync(_input, [_archive], state, options, inputDirectories: [save]);
        Assert.IsTrue(complete.Completed, complete.Message);
        var extra = Directory.CreateDirectory(Path.Combine(_input, "unregistered-before")).FullName;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(_input, [_archive], state,
            options, inputDirectories: [save, extra]));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(_input, [_archive], state, options));
        Assert.IsTrue((await executor.ExecuteAsync(_input, [_archive], state, options, inputDirectories: [save])).Completed);
        Assert.AreEqual(2, _archives.Calls);
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int Calls { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", null));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Calls++;
            var outer = await File.ReadAllTextAsync(request.Files[0], ct) == "outer";
            Directory.CreateDirectory(Path.Combine(request.Directory, outer ? "Save" : "plugins"));
            await File.WriteAllTextAsync(Path.Combine(request.Directory, outer ? "inner.zip" : "game.bin"), outer ? "inner" : "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
