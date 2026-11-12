using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<(ArtifactRecord Raw, ArtifactRecord Boundary, ArtifactRecord Anchor, ArtifactRecord Publication, string State)> BoundaryFixture(string sourceCleanup = "delete")
    {
        var raw = await Artifact("download");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameFile", TargetName = "intermediate.bin"},
            new() {Id = "second", Op = "renameFile", TargetName = "content.bin", Input = "first"}]};
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = sourceCleanup};
        var executor = new FileProcessingPlanExecutor(new NoBoundaryExtraction());
        var workspace = Path.Combine(root, "checkpoint");
        var first = await executor.ExecuteAsync(plan, raw.RootDirectory, raw.Files, workspace,
            stopAfterStepId: "first", storagePolicy: policy);
        Assert.IsTrue(first.Completed, first.Message);
        var last = await executor.ExecuteAsync(plan, raw.RootDirectory, raw.Files, workspace,
            stopAfterStepId: "second", storagePolicy: policy);
        Assert.IsTrue(last.Completed, last.Message);
        async Task<ArtifactRecord> Register(FileProcessingPlanResult result, string step)
        {
            var id = Guid.NewGuid().ToString("N");
            var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(raw.Id,
                JsonSerializer.Serialize(plan, Json), null, CompletedThroughStepId: step, StoragePolicy: policy), Json);
            var artifact = await store.AdoptAsync(new(id, id, "files.process", raw.Fingerprint),
                new(result.OutputDirectory!, result.Files, "processedFiles", true, [raw.Id], metadata), "fixture");
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", id, artifact.OperationId));
            return artifact;
        }
        var boundary = await Register(first, "first"); var anchor = await Register(last, "second");
        var publication = await Artifact("publishedFiles", anchor);
        return (raw, boundary, anchor, publication, Path.Combine(Path.GetDirectoryName(last.OutputDirectory!)!, "state.json"));
    }

    [TestMethod]
    public async Task Registered_sibling_boundary_uses_real_execution_proof_without_changing_artifact_lineage()
    {
        var f = await BoundaryFixture();
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, f.Boundary, [f.Publication.Id])).Complete);
        var rows = await Preview(f.Publication);
        var row = rows.Single(r => r.ArtifactId == f.Boundary.Id); Assert.IsTrue(row.Eligible, row.Reason);
        Assert.AreEqual("ReferencedDescendant", rows.Single(r => r.ArtifactId == f.Raw.Id).Reason);
        // Public source cleanup cannot bypass registered-boundary policy/owner validation.
        var publicAttempt = await new ArchiveQuarantineService(store, root, new Storage()).QuarantineAsync(f.Boundary.Id, [f.Publication.Id]);
        Assert.IsFalse(publicAttempt.Completed);
        var publicDelete = await new ArchiveQuarantineService(store, root, new Storage()).DeleteAfterDeliveryAsync(f.Boundary.Id, [f.Publication.Id]);
        Assert.IsFalse(publicDelete.Completed);
        var result = await cleanup.ReclaimAsync(row, [1], default); Assert.IsTrue(result.Completed, result.Reason);
        Assert.IsFalse(File.Exists(f.Boundary.Files.Single())); Assert.IsTrue(File.Exists(f.Publication.Files.Single()));
        CollectionAssert.AreEqual(new[] {f.Raw.Id}, (await store.GetArtifactAsync(f.Boundary.Id))!.ParentArtifactIds.ToArray());
        Assert.IsTrue(File.Exists(f.State));
        Assert.AreEqual("AlreadyRetired", (await cleanup.ReclaimAsync(row, [1], default)).Reason);
    }

    [TestMethod]
    public async Task Retired_anchor_keeps_immutable_proof_and_receipt_for_remaining_sibling_cleanup()
    {
        var f = await BoundaryFixture();
        var anchor = (await Preview(f.Publication)).Single(r => r.ArtifactId == f.Anchor.Id);
        Assert.IsTrue((await cleanup.ReclaimAsync(anchor, [1], default)).Completed);
        var sibling = (await Preview(f.Publication)).Single(r => r.ArtifactId == f.Boundary.Id);
        Assert.IsTrue(sibling.Eligible, sibling.Reason);
        Assert.IsTrue((await cleanup.ReclaimAsync(sibling, [1], default)).Completed);
        Assert.IsTrue(File.Exists(f.Publication.Files.Single()));
    }

    [TestMethod]
    public async Task Global_original_download_retention_does_not_disable_processing_copy_cleanup()
    {
        var f = await BoundaryFixture("keep"); var rows = await Preview(f.Publication);
        Assert.AreEqual("RetentionPolicyKeep", rows.Single(r => r.ArtifactId == f.Raw.Id).Reason);
        var boundary = rows.Single(r => r.ArtifactId == f.Boundary.Id); Assert.IsTrue(boundary.Eligible, boundary.Reason);
        Assert.IsTrue((await cleanup.ReclaimAsync(boundary, [1], default)).Completed);
        Assert.IsTrue(File.Exists(f.Raw.Files.Single())); Assert.IsTrue(File.Exists(f.Publication.Files.Single()));
    }

    [DataTestMethod]
    [DataRow("incomplete")]
    [DataRow("pending")]
    [DataRow("origins")]
    [DataRow("step")]
    [DataRow("ledger")]
    [DataRow("transition")]
    [DataRow("extraFile")]
    [DataRow("keep")]
    [DataRow("missingInput")]
    [DataRow("missingPolicy")]
    [DataRow("partialReplacement")]
    [DataRow("retainedInput")]
    public async Task Sibling_proof_requires_complete_exact_recorded_execution(string mutation)
    {
        var f = await BoundaryFixture();
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.State), Json)!;
        var boundary = state.BoundaryResults["first"];
        switch (mutation)
        {
            case "incomplete": state.Completed = false; break;
            case "pending": state.PendingOperation = "first"; break;
            case "origins": boundary.Files[0] = boundary.Files[0] with {Origins = [new("unrelated.key")]}; break;
            case "step": boundary.Files[0] = boundary.Files[0] with {StepId = "second"}; break;
            case "ledger": state.FileContents.Clear(); break;
            case "transition": state.FileTransitions.Clear(); break;
            case "extraFile": boundary.Files.Add(boundary.Files[0]); break;
            case "keep": state.StepPolicies["first"] = new() {SourceCleanup = "keep"}; break;
            case "missingInput": state.InputManifest = null; break;
            case "missingPolicy": state.StepPolicies.Clear(); break;
            case "retainedInput": state.RetainedFiles.Add(state.Outputs["first"][0].Path); break;
            case "partialReplacement":
                var key = Path.GetRelativePath(Path.GetDirectoryName(f.State)!, state.Outputs["first"][0].Path).Replace('\\', '/');
                state.FileTransitions[key].Add("outputs/undelivered/key.bin");
                state.FileContents["outputs/undelivered/key.bin"] = state.FileContents[key] with {RelativePath = "outputs/undelivered/key.bin"};
                break;
        }
        await File.WriteAllTextAsync(f.State, JsonSerializer.Serialize(state, Json));
        var row = (await Preview(f.Publication)).Single(r => r.ArtifactId == f.Boundary.Id);
        Assert.IsFalse(row.Eligible, mutation); Assert.IsTrue(File.Exists(f.Boundary.Files.Single()));
    }

    [TestMethod]
    public async Task Sibling_evidence_changed_after_preview_is_not_applied_and_final_bytes_are_verified()
    {
        var f = await BoundaryFixture(); var row = (await Preview(f.Publication)).Single(r => r.ArtifactId == f.Boundary.Id);
        Assert.IsTrue(row.Eligible, row.Reason);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.State), Json)!;
        state.BoundaryResults["first"].Files[0] = state.BoundaryResults["first"].Files[0] with {StepId = "second"};
        var original = await File.ReadAllTextAsync(f.State);
        await File.WriteAllTextAsync(f.State, JsonSerializer.Serialize(state, Json));
        Assert.AreEqual("SelectionChanged", (await cleanup.ReclaimAsync(row, [1], default)).Reason);
        await File.WriteAllTextAsync(f.State, original);
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(f.Boundary.MetadataJson!, Json)!;
        var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json)!;
        plan.Steps[0] = plan.Steps[0] with {TargetName = "different.bin"};
        var changed = JsonSerializer.Serialize(metadata with {PlanJson = JsonSerializer.Serialize(plan, Json)}, Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == f.Boundary.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, changed));
        Assert.AreEqual("SelectionChanged", (await cleanup.ReclaimAsync(row, [1], default)).Reason);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == f.Boundary.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, f.Boundary.MetadataJson));
        await File.WriteAllTextAsync(f.Publication.Files.Single(), "changed final");
        Assert.IsFalse((await cleanup.ReclaimAsync(row, [1], default)).Completed);
        Assert.IsTrue(File.Exists(f.Boundary.Files.Single()));
    }

    [TestMethod]
    public async Task Registered_sibling_retention_and_completed_other_owner_references_remain_protected()
    {
        var f = await BoundaryFixture();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", f.Boundary.OperationId));
        Assert.AreEqual("SharedOwner", (await Preview(f.Publication)).Single(r => r.ArtifactId == f.Boundary.Id).Reason);
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(f.Boundary.MetadataJson!, Json)!;
        var kept = JsonSerializer.Serialize(metadata with {StoragePolicy = new() {CleanupIntermediates = false}}, Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == f.Boundary.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, kept));
        Assert.AreEqual("RetentionPolicyKeep", (await Preview(f.Publication, 1, 2)).Single(r => r.ArtifactId == f.Boundary.Id).Reason);
    }

    private sealed class NoBoundaryExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => throw new AssertFailedException("This fixture must not extract.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct) => throw new AssertFailedException("This fixture must not extract.");
    }
}
