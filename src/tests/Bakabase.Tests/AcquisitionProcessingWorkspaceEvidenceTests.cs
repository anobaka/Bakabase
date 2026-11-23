using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionProcessingWorkspaceEvidenceTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BakabaseStageEvidence_" + Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void Initialize() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Clean() => Directory.Delete(_root, true);

    [DataTestMethod]
    [DataRow("owned", true)]
    [DataRow("borrowed", false)]
    [DataRow("replacement", false)]
    [DataRow("partial", false)]
    [DataRow("otherStage", false)]
    [DataRow("otherHash", false)]
    public async Task Only_committed_owned_members_match_the_durable_writer_record(string scenario, bool eligible)
    {
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        string file = Path.Combine(stage, "result.bin");
        await File.WriteAllTextAsync(file, "published payload");
        var manifest = await ArtifactManifestFiles.CaptureAsync(stage, [file]);
        var selected = new ArtifactRecord("stage-artifact", "operation", "processedFiles", stage, [file], manifest,
            manifest.Fingerprint, ArtifactLocationState.Available, true, ["processed"], null);
        var moved = new AcquisitionStageDirectoryMoveFile("result.bin", new FileInfo(file).Length, 1234, Completed: true);
        var move = new AcquisitionStageDirectoryMove("part", "processing", Path.Combine(_root, "private"), stage,
            true, [moved], [], Completed: true);
        var entry = new AcquisitionProcessingStageCommitReceipt(1, "operation", 1, 2, "part", "binding", stage,
            "processed", manifest.Fingerprint, move.SourceDirectory!, "intent", move, []);
        // The producer records the result ID after each member completed. Older journals leave
        // the aggregate Move.Completed at its original value, while artifact metadata sets it.
        var journal = entry with {ArtifactId = selected.Id, Fingerprint = manifest.Fingerprint, Move = move with {Completed = false}};
        switch (scenario)
        {
            case "borrowed": journal = journal with {ReusedFiles = ["result.bin"]}; break;
            case "replacement": journal = journal with {ArtifactId = "new-owner"}; break;
            case "partial": journal = journal with {Move = move with {Files = [moved with {Completed = false}]}}; break;
            case "otherStage": entry = entry with {Move = move with {Directory = _root}}; break;
            case "otherHash":
                var files = new List<AcquisitionStageDirectoryMoveFile> {moved with {CopiedHash = new string('a', 64)}};
                entry = entry with {Move = move with {Files = files}};
                journal = journal with {Move = move with {Files = files}};
                break;
        }
        File.Delete(file);
        Assert.AreEqual(eligible, AcquisitionProcessingWorkspaceEvidence.OwnsCommittedFiles(selected, entry, journal));
    }

    [DataTestMethod]
    [DataRow("private", true)]
    [DataRow("borrowedVolume", false)]
    [DataRow("pendingWriter", false)]
    [DataRow("keepFiles", false)]
    [DataRow("keptStep", false)]
    [DataRow("unrelatedLocation", false)]
    [DataRow("missingLineage", false)]
    [DataRow("movedInputs", false)]
    [DataRow("retainedInput", false)]
    [DataRow("differentBoundary", false)]
    [DataRow("wrongInputKey", false)]
    [DataRow("largeHistory", true)]
    public async Task Checkpoint_authorizes_exact_private_inputs_after_relocation_but_never_borrowed_volumes(string scenario, bool eligible)
    {
        var file = Path.Combine(_root, "source.rar");
        await File.WriteAllTextAsync(file, "verified archive");
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [file]);
        var input = new ArtifactRecord("input", "operation", "download", _root, [file], manifest, manifest.Fingerprint,
            ArtifactLocationState.Available, true, [], null);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};
        var root = Path.Combine(_root, "checkpoints");
        var directory = FileProcessingPlanExecutor.CheckpointInputDirectory(root, plan, input, null);
        Directory.CreateDirectory(directory);
        var output = input with {Id = "completed", RootDirectory = Path.Combine(directory, "result")};
        var privatePath = Path.Combine(directory, "inputs", "source.rar");
        var state = JsonSerializer.SerializeToNode(new
        {
            completed = true, lineageVersion = 1, inputManifest = manifest,
            storagePolicy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "delete"},
            boundaryResults = new {extract = new {directory = output.RootDirectory, manifest}},
            outputs = new {download = new[] {new {path = privatePath, relativePath = "source.rar"}}}
        }, JsonSerializerOptions.Web)!.AsObject();
        switch (scenario)
        {
            case "borrowedVolume": state["readOnlyVolumeArtifactId"] = input.Id; break;
            case "pendingWriter": state["pendingOperation"] = "pending-extract"; break;
            case "keepFiles": state["storagePolicy"]!["sourceCleanup"] = "keep"; break;
            case "keptStep": state["stepPolicies"] = new JsonObject {["extract"] = new JsonObject {["cleanupIntermediates"] = false}}; break;
            case "unrelatedLocation": state["outputs"]!["download"]![0]!["path"] = file; break;
            case "missingLineage": state["lineageVersion"] = 0; break;
            case "movedInputs": state["operations"] = new JsonObject {["step"] = new JsonObject {
                ["renameFiles"] = new JsonArray(new JsonObject {["move"] = true})}}; break;
            case "retainedInput": state["retainedFiles"] = new JsonArray(privatePath); break;
            case "differentBoundary": state["boundaryResults"]!["extract"]!["manifest"]!["fingerprint"] = "changed"; break;
            case "wrongInputKey":
                state["outputs"]!["Download"] = state["outputs"]!["download"]!.DeepClone();
                state["outputs"]!.AsObject().Remove("download");
                break;
        }
        var statePath = Path.Combine(directory, "state.json");
        if (scenario == "largeHistory")
        {
            // Unrelated output paths can exceed the ordinary resume inspection limit.
            // The input filename, policies and observed manifests remain small.
            await using var stream = new StreamWriter(statePath);
            await stream.WriteAsync("{\"finalFiles\":[");
            var names = string.Concat(Enumerable.Repeat("\"historical-output-file\",", 4096));
            for (var i = 0; i < 740; i++) await stream.WriteAsync(names);
            await stream.WriteAsync("\"last\"],");
            await stream.WriteAsync(state.ToJsonString()[1..]);
        }
        else await File.WriteAllTextAsync(statePath, state.ToJsonString());
        File.Delete(file);
        var result = await FileProcessingPlanExecutor.ReadReclaimableInputAsync(root, plan, input, output, "extract", null, default);
        Assert.AreEqual(eligible, result != null);
        if (result != null)
        {
            Assert.AreEqual(directory, result.Directory);
            Assert.IsTrue(FileDispositionProofs.SameManifest(manifest, result.InputManifest));
        }
    }
    [DataTestMethod]
    [DataRow("confirmed", true)]
    [DataRow("noReceipt", false)]
    [DataRow("differentSource", false)]
    [DataRow("otherStage", false)]
    [DataRow("unproven", false)]
    [DataRow("unrelatedMember", false)]
    public async Task Only_a_proven_replacement_in_the_same_stage_with_a_matching_removal_receipt_withdraws_old_files(
        string scenario, bool withdrawn)
    {
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var oldFile = Path.Combine(stage, "old.rar");
        var newFile = Path.Combine(stage, "result.bin");
        await File.WriteAllTextAsync(oldFile, "raw archive");
        await File.WriteAllTextAsync(newFile, "expanded data");
        var original = await ArtifactManifestFiles.CaptureAsync(stage, [oldFile]);
        var expanded = await ArtifactManifestFiles.CaptureAsync(stage, [newFile]);
        var input = new ArtifactRecord("input", "one", "processedFiles", stage, [oldFile], original,
            original.Fingerprint, ArtifactLocationState.Missing, true, [], null);
        var source = new ArtifactRecord("private", "two", "processedFiles", Path.Combine(_root, "private"),
            [], expanded, expanded.Fingerprint, ArtifactLocationState.Retired, true, [input.Id], null);
        var current = source with {Id = "current", RootDirectory = stage, State = ArtifactLocationState.Available,
            Files = [newFile], ParentArtifactIds = [source.Id]};
        var proof = FileDispositionProofs.Create(input, expanded, new([new("old.rar", ["result.bin"], Replaced: true)], original, expanded));
        var journal = new AcquisitionProcessingStageCommitReceipt(1, "operation", 1, 2, "part", "binding", stage,
            source.Id, source.Fingerprint, source.RootDirectory, "intent", new("part", "processing", source.RootDirectory,
                stage, true, [], [], Completed: true), [], current.Id, Fingerprint: expanded.Fingerprint, ReplacedInputsRemoved: ["old.rar"]);
        switch (scenario)
        {
            case "noReceipt": journal = journal with {ReplacedInputsRemoved = null}; break;
            case "differentSource": journal = journal with {SourceArtifactId = "other"}; break;
            case "otherStage": input = input with {RootDirectory = Path.Combine(_root, "foreign")}; break;
            case "unproven": proof = proof with {InputFingerprint = "stale-proof"}; break;
            case "unrelatedMember": journal = journal with {ReplacedInputsRemoved = ["other.rar"]}; break;
        }
        var removed = AcquisitionProcessingWorkspaceEvidence.ReplacedStageInputs(input, source, current, proof, journal);
        Assert.AreEqual(withdrawn ? 1 : 0, removed.Count);
        if (withdrawn) Assert.AreEqual(original.Entries.Single(), removed.Single());
    }
}
