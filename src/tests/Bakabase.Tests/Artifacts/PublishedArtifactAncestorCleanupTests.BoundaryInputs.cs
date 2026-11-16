using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("shared")]
    [DataRow("unlistedEmptyDirectory")]
    [DataRow("alreadyRemovedFile")]
    [DataRow("foreignDirectoryReference")]
    public async Task Abandon_removes_only_the_suspended_boundary_exclusively_staged_files(string scenario)
    {
        var task = await UnfinishedStageDeletion();
        var shared = scenario == "shared";
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(source, "volume-set")).FullName;
        var file = Path.Combine(folder, "archive.zip");
        await File.WriteAllTextAsync(file, "fixture archive");
        var input = await store.AdoptAsync(new("boundary-source", "boundary-source", "download", "fixture"),
            new(source, [file], "download", true), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:artifact.download", input.OperationId));
        var target = new CloudDownloadTarget { Id = "nested.part", Title = "Test boundary", DownloadArtifactId = input.Id };
        var plan = new FileProcessingPlan { Requirement = "required", Steps = [new() { Id = "extract", Op = "extractArchive", Selector = "volume-set/archive.zip" }] };
        var config = new AcquisitionPlanProcessingConfig(target.Id, plan, "extract");
        var processing = new FileArtifactProcessingConfig { StorageTitle = target.Title };
        var resolved = FileExtractionLayouts.Apply(FileArtifactProcessor.WithDefaultPasswords(AcquisitionPlanProcessingActivity.EffectivePlan(config, target), null), processing.ExtractionLayout);
        var key = FileArtifactProcessor.OperationKey("boundary-intent", input, resolved, null, processing);
        var claim = await store.ClaimAsync(new(key, "boundary-intent", "files.process", input.Fingerprint), "workflow:1:part:fixture", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim.Lease);
        var workspaces = new FileProcessingWorkspaceResolver(root, new Storage());
        var allocation = Directory.CreateDirectory(Path.Combine(root, "boundary workspace")).FullName;
        await workspaces.ResolveOperationAsync(key, 1, input.RootDirectory, allocation, null, true, null, target.Title, default);
        var boundary = await workspaces.ResolveCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, resolved), key, 1,
            input, resolved, null, allocation, null, target.Title, default);
        var executor = new FileProcessingPlanExecutor(new AbandonedExtraction());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(resolved, input.RootDirectory, input.Files,
            boundary, stopAfterStepId: "extract", identityPath: workspaces.IdentityPath));
        var stateFile = Directory.GetFiles(boundary, "state.json", SearchOption.AllDirectories).Single();
        var staged = Directory.GetFiles(boundary, "archive.zip", SearchOption.AllDirectories).Single();
        var state = await File.ReadAllTextAsync(stateFile);
        var node = WorkflowRunPlans.Serialize(new() { Name = "fixture", TriggerKind = "manual",
            Nodes = [new() { NodeId = "extract-node", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(config, WorkflowJson.Options) }] });
        var current = JsonSerializer.Serialize(new { type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name,
            item = new DownloadWorkset { Targets = [target] } }, WorkflowJson.Options);
        var wait = JsonSerializer.Serialize(new { reason = "PasswordUnknown", artifactId = input.Id, targetId = target.Id, knownStepId = "extract" });
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(r => r.CurrentStepIndex, 0)
            .SetProperty(r => r.WaitReason, "cloud.fileProcessing").SetProperty(r => r.PlanSnapshotJson, node)
            .SetProperty(r => r.CurrentItemJson, current).SetProperty(r => r.WaitPromptJson, wait));
        await db.ArtifactOperations.Where(o => o.Id == key).ExecuteUpdateAsync(set => set.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddHours(-1)));
        if (shared)
            await store.ClaimAsync(new(ArtifactManifestFiles.Hash("foreign"), "foreign-intent", "files.process", input.Fingerprint),
                "workflow:3:part:fixture", TimeSpan.FromMinutes(5));

        var inputs = Path.Combine(Path.GetDirectoryName(stateFile)!, "inputs");
        var sibling = Path.Combine(inputs, "manual-empty-directory");
        if (scenario == "alreadyRemovedFile") File.Delete(staged);
        if (scenario == "foreignDirectoryReference")
        {
            Directory.CreateDirectory(sibling);
            db.AcquisitionTasks.Add(new() {Title = "Other task", WorkflowRunId = 2,
                Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Waiting,
                PlanJson = JsonSerializer.Serialize(new {input = sibling})});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var preview = await task.Service.PreviewAsync(new([task.Task.Id], true));
        var row = preview.Rows.Single();
        var groups = row.Cleanup!.Groups;
        Assert.AreEqual(!shared && scenario != "alreadyRemovedFile", groups.SelectMany(g => g.Eligible ? g.Files : []).Any(f => f.Path == staged),
            string.Join("\n", groups.Select(g => $"{g.ArtifactId}: {g.ReasonCode}: {g.Directory}")));
        if (shared) Assert.IsTrue(groups.Any(g => g.Shared && g.ReasonCode == "ReferencedPath" && g.ArtifactId.StartsWith("abandoned-boundary:")));
        if (scenario == "unlistedEmptyDirectory") Directory.CreateDirectory(sibling);
        var removed = await task.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(shared, File.Exists(staged), JsonSerializer.Serialize(removed.CleanupResults, Json));
        Assert.AreEqual(shared || scenario is "foreignDirectoryReference", Directory.Exists(Path.GetDirectoryName(staged)!));
        Assert.AreEqual(shared || scenario is "unlistedEmptyDirectory" or "foreignDirectoryReference", Directory.Exists(inputs));
        Assert.AreEqual(scenario is "unlistedEmptyDirectory" or "foreignDirectoryReference", Directory.Exists(sibling));
        Assert.AreEqual(state, await File.ReadAllTextAsync(stateFile));
        Assert.IsTrue(task.Publication.Files.All(File.Exists));
    }
}
