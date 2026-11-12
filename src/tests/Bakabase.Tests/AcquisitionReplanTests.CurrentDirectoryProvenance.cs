using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Artifact)> ProvenanceInput()
    {
        var (task, run, download) = await Failed();
        var root = Directory.CreateDirectory(Path.Combine(_root, "provenance-input")).FullName;
        const string relative = "recorded-transfer/recorded-extraction/original-author/content.pdf";
        var file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "verified publication bytes");
        var artifact = await Artifacts.AdoptAsync(new("provenance-process", "provenance-process", "files.process", download.Fingerprint),
            new(root, [file], "processedFiles", true, [download.Id], CreateMetadata: manifest =>
                JsonSerializer.Serialize(new FileArtifactProcessingMetadata(download.Id, "{}", null,
                    DirectoryProvenance: FileArtifactDeliveryLayout.Bind(manifest,
                        [new(relative, false, ["recorded-transfer", "recorded-transfer/recorded-extraction"])])), WorkflowJson.Options)), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:provenance", artifact.OperationId);
        return (task, run, artifact);
    }

    [TestMethod]
    public async Task Current_directory_not_required_continuation_delivers_without_recorded_wrappers_and_preserves_author_folder()
    {
        var (task, old, original) = await ProvenanceInput();
        var originalMetadata = original.MetadataJson;
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, old.Id, "The PDF is already complete.",
            [new("main", new() {Requirement = "notRequired"}, [original.Id], original.Id,
                UseAdaptiveAssessment: false, InputMode: "currentDirectory")], EnableAdaptiveAssessment: false);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.IsNotNull(FileArtifactDeliveryLayout.ReadProvenance(captured));
        var plan = revised.Plan with {DeliveryLayout = revised.Plan.DeliveryLayout! with
            {Settings = revised.Plan.DeliveryLayout!.Settings with {ContentLayout = new() {RemoveGeneratedDirectories = true}}}};
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[index].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        Assert.AreEqual(captured.Id, ((DownloadWorkset)processed.Replacement!).Targets.Single().ProcessedArtifactId);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(compiled.Nodes.Count - 1), processed.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var publishedId = ((DownloadWorkset)delivered.Replacement!).Targets.Single().PublishedArtifactId;
        var published = (await Artifacts.GetArtifactAsync(publishedId!))!;
        Assert.AreEqual("original-author/content.pdf", published.Manifest.Entries.Single().RelativePath);
        Assert.AreEqual("verified publication bytes", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual(originalMetadata, (await Artifacts.GetArtifactAsync(original.Id))!.MetadataJson);
        Assert.IsTrue(File.Exists(original.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, _continuationArchives.Extractions);
    }

    [DataTestMethod]
    [DataRow("unchanged")]
    [DataRow("added-file")]
    [DataRow("modified-file")]
    [DataRow("removed-parent-binding")]
    public async Task Historical_current_snapshot_recovers_only_same_task_receipted_layout_and_unknown_files_keep_wrappers(string scenario)
    {
        var (task, run, original) = await ProvenanceInput();
        var storage = new UserStoragePolicy(false);
        var first = await AcquisitionCurrentDirectoryFiles.CaptureAsync(Db, Artifacts, storage, null,
            task.Id, 2, run.Id, "main", original, null, default);
        var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(first.MetadataJson!, WorkflowJson.Options)!;
        // Reproduce a previous version's snapshot, which retained its parent receipt but
        // omitted layout evidence. Its immutable registered files and history stay intact.
        var legacyMetadata = JsonSerializer.Serialize(receipt with {DirectoryProvenance = null}, WorkflowJson.Options);
        await Db.Artifacts.Where(row => row.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.MetadataJson, legacyMetadata));
        if (scenario == "added-file") await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(original.Files.Single())!, "manual.key"), "retain unknown key");
        if (scenario == "modified-file") await File.WriteAllTextAsync(original.Files.Single(), "manually replaced contents");
        if (scenario == "removed-parent-binding")
            await Db.ArtifactOperationBindings.Where(binding => binding.OperationId == original.OperationId).ExecuteDeleteAsync();
        first = (await Artifacts.GetArtifactAsync(first.Id))!;
        var input = new AcquisitionPlanArtifactInput(task.Id, run.Id, first.Id, first.OperationId, first.Fingerprint);
        if (scenario == "removed-parent-binding")
        {
            Assert.IsNull(await AcquisitionCurrentDirectoryProvenance.ReadAsync(Db, Artifacts, task.Id, "main", input, default));
            return;
        }
        var second = await AcquisitionCurrentDirectoryFiles.CaptureAsync(Db, Artifacts, storage, null,
            task.Id, 3, run.Id, "main", first, null, default);
        var projection = FileArtifactDeliveryLayout.Preview(second, new() {RemoveGeneratedDirectories = true});
        Assert.AreEqual(scenario == "unchanged", projection.Changed);
        Assert.AreEqual(scenario == "added-file" ? 2 : 1, second.Files.Count);
        if (scenario == "unchanged") Assert.AreEqual("original-author/content.pdf", projection.OutputManifest.Entries.Single().RelativePath);
        else Assert.IsTrue(projection.OutputManifest.Entries.All(entry => entry.RelativePath.StartsWith("recorded-transfer/", StringComparison.Ordinal)));
        Assert.AreEqual(legacyMetadata, (await Artifacts.GetArtifactAsync(first.Id))!.MetadataJson);
        Assert.IsNull(await AcquisitionCurrentDirectoryProvenance.ReadAsync(Db, Artifacts, task.Id + 1, "main", input, default));
    }
}
