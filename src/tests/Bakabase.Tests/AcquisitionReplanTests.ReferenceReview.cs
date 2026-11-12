using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Reference_only_review_verifies_every_large_manifest_leaf_without_embedding_or_truncating_it()
    {
        var (task, old, original, _, _) = await CompletedInput();
        var root = Directory.CreateDirectory(Path.Combine(_root, "large-delivery")).FullName;
        var paths = Enumerable.Range(0, 10001).Select(i => Path.Combine(root, $"file-{i:D5}.dat")).ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, "a");
        var input = await Artifacts.AdoptAsync(new("large-delivery", "large-delivery", "files.publish", original.Fingerprint),
            new(root, paths, "publishedFiles", false, [original.Id]), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", old.Id.ToString(), "main:artifact.continuation", input.OperationId);
        var full = ContinueInput(task, old.Id, input.Id);
        var regular = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, full, default));
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", regular.Message, "Ordinary processing revisions retain the eager evidence limit.");
        var request = full with {Items = [full.Items[0] with {Plan = new() {Requirement = "unknown"}}]};
        var combined = request with {Items = [request.Items[0] with {ArtifactEvidenceIds = [input.Id, original.Id]}]};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, combined, default));
        // Corrupt the final entry while retaining its size and timestamp: neither metadata
        // caching nor a truncated 10,000-file projection may admit this review.
        var modified = File.GetLastWriteTimeUtc(paths[^1]);
        await File.WriteAllTextAsync(paths[^1], "x"); File.SetLastWriteTimeUtc(paths[^1], modified);
        var corrupt = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", corrupt.Message);
        Assert.AreEqual(1, (await Plans.GetAsync(task.Id, default))!.Task.PlanRevision);
        await File.WriteAllTextAsync(paths[^1], "a"); File.SetLastWriteTimeUtc(paths[^1], modified);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status);
        var history = revised.PlanHistory.Last();
        Assert.IsNull(history.ArtifactEvidence, "A reference-only review must not copy the large manifest into task history or client detail.");
        var retained = history.Plan.Parts.Single().ContinueFrom!;
        Assert.AreEqual(input.Id, retained.ArtifactId); Assert.AreEqual(input.OperationId, retained.OperationId);
        Assert.AreEqual(input.Fingerprint, retained.Fingerprint); Assert.AreEqual(old.Id, retained.PreviousRunId);
        Assert.AreEqual(task.Id, retained.TaskId);
        Assert.AreEqual(10001, (await Artifacts.GetArtifactAsync(input.Id))!.Manifest.Entries.Count);
        Assert.IsFalse(JsonSerializer.Serialize(revised, WorkflowJson.Options).Contains("file-10000.dat"));

        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[index].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var before = await Db.ArtifactOperations.CountAsync();
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var waiting = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNotNull(waiting.Suspension);
        StringAssert.Contains(waiting.Suspension.PromptJson!, "ExtractionPlanUnknown");
        Assert.IsTrue(waiting.Suspension.PromptJson!.Length < 4096);
        Assert.AreEqual(before, await Db.ArtifactOperations.CountAsync());
        Assert.AreEqual(0, _continuationArchives.Extractions); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsFalse(Directory.EnumerateDirectories(_root, ".bakabase-work", SearchOption.AllDirectories).Any());
        // The existing output browser pages the exact retained artifact rather than a
        // history copy. Record the continuation node's ordinary artifact reference.
        run.NodeExecutions.Add(new() {NodeId = "files.continue", Kind = AcquisitionPlanContinueFilesActivity.ActivityKind,
            Attempt = 1, StartedAt = DateTime.UtcNow, Status = "succeeded", OutputsJson = JsonSerializer.Serialize(
                new[] {new WorkflowOutputBlock {Kind = "artifact", Key = "main:continuation", ArtifactId = input.Id}}, WorkflowJson.Options)});
        await Db.SaveChangesAsync();
        var page = await _services.GetRequiredService<WorkflowOutputQueryService<BakabaseDbContext>>()
            .GetArtifactFilesAsync(run.Id, input.Id, 51, 200);
        Assert.IsNotNull(page); Assert.AreEqual(10001, page.FileCount);
        Assert.AreEqual("file-10000.dat", page.Entries.Single().RelativePath);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
    }
}
