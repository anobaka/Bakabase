using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("active-run")]
    [DataRow("interrupted")]
    [DataRow("no-proof")]
    [DataRow("keep")]
    [DataRow("input-keep")]
    [DataRow("no-cleanup")]
    [DataRow("borrowed")]
    [DataRow("foreign-reference")]
    [DataRow("changed-input")]
    [DataRow("changed-output")]
    [DataRow("other-conflict")]
    public async Task Stage_commit_replaces_only_proven_consumed_directory_blockers_and_recovers_without_reextracting(string variant)
    {
        var (task, runId, source) = await StageCommitInput(false);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var archive = Path.Combine(binding.Directory, "images");
        await File.WriteAllTextAsync(archive, "measured archive bytes");
        var input = await Artifacts.AdoptAsync(new("stage-archive-input", "stage-archive-input", "files.process", "input"),
            new(binding.Directory, [archive], "processedFiles", variant != "borrowed",
                MetadataJson: JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null, "{}", null,
                    StoragePolicy: new() {SourceCleanup = variant == "input-keep" ? "keep" : "quarantine"}), WorkflowJson.Options)), "fixture");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:process:archive", input.OperationId);
        var proof = FileDispositionProofs.Create(input, source.Manifest, new([
            new("images", source.Manifest.Entries.Select(e => e.RelativePath).ToArray(), Replaced: true)
        ], input.Manifest, source.Manifest));
        var metadata = new FileArtifactProcessingMetadata(input.Id, "{}", null,
            StoragePolicy: new() {CleanupIntermediates = variant != "no-cleanup", SourceCleanup = variant == "keep" ? "keep" : "quarantine"},
            FileDisposition: variant == "no-proof" ? null : proof);
        await Db.Artifacts.Where(a => a.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson,
            JsonSerializer.Serialize(metadata, WorkflowJson.Options)));
        source = (await Artifacts.GetArtifactAsync(source.Id))!;
        if (variant == "active-run")
        {
            var activeInput = JsonSerializer.Serialize(new {input = archive});
            await Db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants.WorkflowRunStatus.Running)
                .SetProperty(r => r.CurrentItemJson, activeInput));
        }
        if (variant == "foreign-reference") {Db.ResourcesV2.Add(new() {Path = archive, IsFile = true}); await Db.SaveChangesAsync();}
        if (variant == "changed-input") await File.WriteAllTextAsync(archive, "a different archive!!");
        if (variant == "changed-output") await File.WriteAllTextAsync(source.Files.Single(), "other content");
        if (variant == "other-conflict")
        {
            // An unrelated output clash must be found before any consumed input is retired.
            Directory.CreateDirectory(Path.Combine(binding.Directory, "another.bin"));
            var resultRoot = Directory.CreateDirectory(Path.Combine(_root, "another-result")).FullName;
            var copied = Path.Combine(resultRoot, "images", "one.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
            File.Copy(source.Files.Single(), copied);
            var other = Path.Combine(resultRoot, "another.bin"); await File.WriteAllTextAsync(other, "output");
            source = await Artifacts.AdoptAsync(new("stage-with-conflict", "stage-with-conflict", "files.process", input.Fingerprint),
                new(resultRoot, [copied, other], "processedFiles", true, CreateMetadata: manifest =>
                    JsonSerializer.Serialize(metadata with {FileDisposition = FileDispositionProofs.Create(input, manifest,
                        new([new("images", ["images/one.jpg"], Replaced: true)], input.Manifest, manifest))}, WorkflowJson.Options)), "fixture");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:process:conflict", source.OperationId);
        }
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        Task<ArtifactRecord> Commit(IFileArtifactMaterializer? materializer = null) => AcquisitionProcessingStageCommit.CommitCoreAsync(
            Db, Artifacts, new CompositeStorage(), workspaces, task.Id, runId, "main", binding, source, default, materializer);
        if (variant == "interrupted")
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => Commit(new InterruptedStageCommitMove()));
            Assert.IsFalse(File.Exists(archive));
            Assert.IsTrue(source.Files.All(File.Exists), "Completed extracted outputs survive an interrupted handoff.");
            Assert.IsTrue((await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).BuilderStateJson!.Contains("processingStageCommitId"));
        }
        if (variant is "valid" or "active-run" or "interrupted")
        {
            var result = await Commit();
            Assert.AreEqual(binding.Directory, result.RootDirectory);
            Assert.IsTrue(Directory.Exists(archive));
            Assert.AreEqual("image content", await File.ReadAllTextAsync(Path.Combine(archive, "one.jpg")));
            Assert.AreEqual(result.Id, (await Commit()).Id);
            var receipt = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(
                await File.ReadAllTextAsync(Directory.GetFiles(workspaces.StageCommitMetadataDirectory, "*.json").Single()), WorkflowJson.Options)!;
            CollectionAssert.Contains(receipt.ReplacedInputsRemoved!.ToArray(), "images");
        }
        else
        {
            await Assert.ThrowsAsync<Exception>(() => Commit());
            Assert.IsTrue(File.Exists(archive), "Unknown, retained, changed, or externally referenced archive files stay intact.");
            Assert.IsTrue(source.Files.All(File.Exists));
        }
        Assert.IsTrue(File.Exists(Path.Combine(binding.Directory, "unknown-extra.txt")));
    }

    private sealed class InterruptedStageCommitMove : IFileArtifactMaterializer
    {
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct) =>
            throw new IOException("Injected interruption after verified blocker removal.");
        public Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct) =>
            throw new InvalidOperationException("No copy should be started.");
    }
}
