using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using System.IO.Compression;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ExtractionBoundaryAssemblyTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "extraction-boundary-" + Guid.NewGuid().ToString("N"));
    private static readonly FileProcessingPlan Plan = new() {Requirement = "required", Steps = [
        new() {Id = "outer", Op = "extractArchive"},
        new() {Id = "inner", Op = "extractArchive", Input = "outer"}]};
    private readonly TrackingMaterializer materializer = new();
    private readonly ZipExtraction extractor = new();

    [TestInitialize]
    public void Setup()
    {
        var download = Path.Combine(root, "download");
        Directory.CreateDirectory(download);
        using var inner = new MemoryStream();
        using (var archive = new ZipArchive(inner, ZipArchiveMode.Create, true))
        {
            using var stream = archive.CreateEntry("media.bin").Open();
            stream.Write(new byte[65536]);
        }
        using (var archive = ZipFile.Open(Path.Combine(download, "outer.zip"), ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("inner.zip").Open();
            stream.Write(inner.ToArray());
        }
        File.WriteAllText(Path.Combine(download, "note.pdf"), "independent source attachment");
    }

    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);
    private Task<FileProcessingPlanResult> Run(string step, ArtifactStoragePolicy? policy = null) =>
        new FileProcessingPlanExecutor(extractor, materializer).ExecuteAsync(Plan, Path.Combine(root, "download"),
            Directory.GetFiles(Path.Combine(root,"download")), Path.Combine(root,"state"), stopAfterStepId:step,
            storagePolicy:policy, extractionContentsAsRoot:true);

    [TestMethod]
    public async Task Generated_archive_moves_into_the_mixed_boundary_and_both_steps_can_replay()
    {
        var first = await Run("outer");
        Assert.IsTrue(first.Completed, first.Message);
        Assert.AreEqual(1, materializer.Moved.Count, "Only the committed extracted member moves into the mixed boundary.");
        Assert.IsFalse(File.Exists(materializer.Moved.Single().Source));
        var archive = first.Files.Single(p => Path.GetFileName(p)=="inner.zip");
        var note = first.Files.Single(p=>Path.GetFileName(p)=="note.pdf");
        var expected = await File.ReadAllBytesAsync(archive);
        await File.WriteAllTextAsync(Path.Combine(root,"download","note.pdf"), "changed after snapshot");
        Assert.AreEqual("independent source attachment", await File.ReadAllTextAsync(note));
        // Restore the external fixture before re-entering the original input verification.
        await File.WriteAllTextAsync(Path.Combine(root,"download","note.pdf"), "independent source attachment");
        var final = await Run("inner");
        Assert.IsTrue(final.Completed, final.Message);
        CollectionAssert.AreEquivalent(new[] {"media.bin","note.pdf"}, final.Files.Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(archive));
        CollectionAssert.AreEquivalent(first.Files.ToArray(),(await Run("outer")).Files.ToArray());
        Assert.AreEqual(2,extractor.Calls);
    }

    [TestMethod]
    public async Task Successful_group_promotes_its_private_output_alongside_an_explicitly_retained_failed_group()
    {
        var input = Path.Combine(root, "download");
        var missing = Path.Combine(input, "z.7z");
        await File.WriteAllTextAsync(missing, "original incomplete attachment");
        extractor.MissingName = "z.7z";
        var files = Directory.GetFiles(input);
        var manifest = await ArtifactManifestFiles.CaptureAsync(input, files, CancellationToken.None);
        var registration = new ArtifactRecord("fixture", "download", "download", input, files, manifest,
            manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        var owner = new ArchiveGroupOwner(17, "part", "outer");
        Task<FileProcessingPlanResult> Current(string? token) =>
            new FileProcessingPlanExecutor(extractor, materializer).ExecuteAsync(Plan, input, files, Path.Combine(root, "state"),
                stopAfterStepId: "outer", extractionContentsAsRoot: true,
                snapshotSource: new(registration, _ => Task.FromResult(manifest))
                    {ArchiveGroup = new(owner, registration.Id, manifest.Fingerprint, "process", 1, token)});
        var waiting = await Current(null);
        Assert.IsNotNull(waiting.IncompleteArchiveGroup);
        var result = await Current(waiting.IncompleteArchiveGroup.Token);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEqual(new[] {"inner.zip"}, materializer.Moved.Select(m => Path.GetFileName(m.Source)).ToArray());
        Assert.AreEqual(1, extractor.Calls);
        CollectionAssert.AreEquivalent(new[] {"inner.zip", "z.7z", "note.pdf"}, result.Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual("original incomplete attachment", await File.ReadAllTextAsync(missing));
        Assert.AreEqual("original incomplete attachment", await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory!, "z.7z")));
    }

    [TestMethod]
    public async Task Crash_after_atomic_move_can_retry_and_a_changed_survivor_is_never_overwritten()
    {
        materializer.Interrupt = true;
        await Assert.ThrowsAsync<IOException>(() => Run("outer"));
        var target = materializer.Moved.Single().Target;
        var data=await File.ReadAllBytesAsync(target);
        await File.WriteAllTextAsync(target, "unrelated");
        await Assert.ThrowsAsync<InvalidDataException>(() => Run("outer"));
        Assert.AreEqual("unrelated",await File.ReadAllTextAsync(target));
        await File.WriteAllBytesAsync(target,data);
        Assert.IsTrue((await Run("outer")).Completed);
        Assert.IsTrue((await Run("inner")).Completed);
        Assert.AreEqual(2,extractor.Calls);
    }

    [TestMethod]
    public async Task Unsupported_atomic_move_falls_back_to_independent_copy()
    {
        materializer.Unsupported = true;
        var boundary = await Run("outer");
        Assert.IsTrue(boundary.Completed,boundary.Message);
        var source=materializer.Attempted.Single();
        Assert.IsTrue(File.Exists(source));
        var registered=boundary.Files.Single(p=>Path.GetFileName(p)=="inner.zip");
        var bytes=await File.ReadAllBytesAsync(registered);
        await File.WriteAllTextAsync(source,"changed spare copy");
        CollectionAssert.AreEqual(bytes,await File.ReadAllBytesAsync(registered));
        Assert.IsTrue((await Run("inner")).Completed);
    }

    [TestMethod]
    public async Task Explicit_copy_and_retention_keep_their_output_even_in_a_mixed_boundary()
    {
        var result=await Run("outer",new() {Materialization="copy",CleanupIntermediates=false});
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0,materializer.Attempted.Count);
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(root,"state"),"inner.zip",SearchOption.AllDirectories).Count()>=2);
    }

    [TestMethod]
    public async Task Multiple_projections_and_external_members_do_not_authorize_promotion()
    {
        materializer.Interrupt = true;
        await Assert.ThrowsAsync<IOException>(() => Run("outer"));
        var stateFile = Directory.EnumerateFiles(root, "state.json", SearchOption.AllDirectories).Single();
        var directory = Path.GetDirectoryName(stateFile)!;
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        state.ExecutionRoot = directory;
        var generated = state.Active.Single(f => f.StepId == "outer");
        var original = state.Active.ToList();
        static HashSet<string> Candidates(FileProcessingPlanExecutor.State s, string d) =>
            FileProcessingPlanExecutor.PromotableExtractions(s, d, "outer", default);
        CollectionAssert.AreEquivalent(new[] {generated.Path}, Candidates(state, directory).ToArray());
        state.Active.Add(generated with {RelativePath = "additional-name.zip"});
        Assert.AreEqual(0, Candidates(state, directory).Count);
        state.Active = [..original, new(Path.Combine(root, "download", "note.pdf"), "another-note.pdf")];
        Assert.AreEqual(0, Candidates(state, directory).Count);
    }

    private async Task<FileProcessingPlanResult> StandaloneReview(bool registered = true,
        ArtifactStoragePolicy? policy = null, ArtifactStoragePolicyOverride? stepPolicy = null)
    {
        var input = Path.Combine(root, "download");
        var files = Directory.GetFiles(input);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "extract", Op = "extractArchive", Input = "download", Selector = "*.zip", StoragePolicy = stepPolicy}]};
        var manifest = await ArtifactManifestFiles.CaptureAsync(input, files, CancellationToken.None);
        var source = new ArtifactRecord("review-input", "review-op", "processedFiles", input, files, manifest,
            manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        return await new FileProcessingPlanExecutor(extractor, materializer).ExecuteAsync(plan, input, files,
            Path.Combine(root, "state"), storagePolicy: policy, extractionContentsAsRoot: true,
            snapshotSource: registered ? new(source, _ => Task.FromResult(manifest)) : null);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Standalone_review_promotes_verified_outputs_and_preserves_registered_originals(bool includeAttachment)
    {
        var input = Path.Combine(root, "download");
        var attachment = Path.Combine(input, "note.pdf");
        if (!includeAttachment) File.Delete(attachment);
        var before = await ArtifactManifestFiles.CaptureAsync(input, Directory.GetFiles(input), CancellationToken.None);
        var result = await StandaloneReview();
        Assert.IsTrue(result.Completed, result.Message);
        var moved = materializer.Moved.Single();
        Assert.AreEqual("inner.zip", Path.GetFileName(moved.Target));
        Assert.IsFalse(File.Exists(moved.Source));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(input, before)).Available);
        var final = result.Files.Single(f => Path.GetFileName(f) == "inner.zip");
        Assert.IsTrue(File.Exists(final));
        if (includeAttachment)
        {
            Assert.AreEqual("independent source attachment", await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory!, "note.pdf")));
            var stateFile = Directory.EnumerateFiles(root, "state.json", SearchOption.AllDirectories).Single();
            var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var originalCopy = state.FinalAssembly!.Files.Single(f => f.RelativePath == "note.pdf");
            Assert.IsFalse(originalCopy.Move);
            Assert.IsTrue(File.Exists(originalCopy.Source));
        }
        Assert.IsTrue((await StandaloneReview()).Completed);
        Assert.AreEqual(1, extractor.Calls);
        Assert.AreEqual(1, materializer.Moved.Count);
    }

    [DataTestMethod]
    [DataRow("unregistered-source")]
    [DataRow("explicit-copy")]
    [DataRow("explicit-keep")]
    [DataRow("step-copy")]
    [DataRow("step-keep")]
    public async Task Standalone_promotion_requires_registration_and_preserves_explicit_policies(string setting)
    {
        var result = await StandaloneReview(registered: setting != "unregistered-source", policy: setting switch {
            "explicit-copy" => new() {Materialization = "copy"}, "explicit-keep" => new() {CleanupIntermediates = false}, _ => null
        }, stepPolicy: setting switch {
            "step-copy" => new() {Materialization = "copy"}, "step-keep" => new() {CleanupIntermediates = false}, _ => null
        });
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(0, materializer.Moved.Count);
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(root, "state"), "inner.zip", SearchOption.AllDirectories).Count() == 2);
    }

    [TestMethod]
    public async Task Standalone_review_recovers_after_move_and_rejects_a_changed_pending_file()
    {
        materializer.Interrupt = true;
        await Assert.ThrowsAsync<IOException>(() => StandaloneReview());
        var move = materializer.Moved.Single();
        var original = await File.ReadAllBytesAsync(move.Target);
        await File.WriteAllTextAsync(move.Target, "unrelated data");
        await Assert.ThrowsAsync<InvalidDataException>(() => StandaloneReview());
        Assert.AreEqual("unrelated data", await File.ReadAllTextAsync(move.Target));
        await File.WriteAllBytesAsync(move.Target, original);
        Assert.IsTrue((await StandaloneReview()).Completed);
        Assert.AreEqual(1, materializer.Moved.Count);
        Assert.AreEqual(1, extractor.Calls);
    }

    [TestMethod]
    public async Task Standalone_review_with_unsupported_rename_keeps_independent_verified_source_and_result()
    {
        materializer.Unsupported = true;
        var result = await StandaloneReview();
        Assert.IsTrue(result.Completed, result.Message);
        var output = result.Files.Single(f => Path.GetFileName(f) == "inner.zip");
        var expected = await File.ReadAllBytesAsync(output);
        var source = materializer.Attempted.Single();
        Assert.IsTrue(File.Exists(source));
        await File.WriteAllTextAsync(source, "changed redundant copy");
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(output));
        Assert.IsTrue(File.Exists(Path.Combine(root, "download", "outer.zip")));
    }

    private sealed class ZipExtraction : IArchiveExtractionService
    {
        public int Calls;
        public string? MissingName;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry,IReadOnlyList<string?> passwords,
            Action<string>? log,CancellationToken ct) => Path.GetFileName(entry) == MissingName
                ? Task.FromResult(new ArchivePasswordProbe(false, null, [], "", "") {FailureCode = "MissingVolumes"})
                : Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,Action<int>? progress,CancellationToken ct)
        {
            Calls++;
            ZipFile.ExtractToDirectory(request.Files[0],request.Directory);
            return Task.FromResult(new ArchiveExtractionResult(true,request.Directory,""));
        }
    }

    private sealed class TrackingMaterializer : IFileArtifactMaterializer
    {
        public bool Interrupt, Unsupported;
        public readonly List<(string Source,string Target)> Moved=[];
        public readonly List<string> Attempted=[];
        private readonly FileArtifactMaterializer native=new();
        public Task<FileMaterializationResult> MaterializeAsync(string source,string target,string mode,CancellationToken ct) =>
            native.MaterializeAsync(source,target,"copy",ct);
        public async Task<bool> TryMoveAsync(string source,string target,CancellationToken ct)
        {
            Attempted.Add(source);
            if (Unsupported || !await native.TryMoveAsync(source,target,ct)) return false;
            Moved.Add((source,target));
            if (Interrupt) {Interrupt=false;throw new IOException("Injected interruption after move.");}
            return true;
        }
    }
}
