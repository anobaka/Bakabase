using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    [DataRow("present")]
    [DataRow("released")]
    [DataRow("new-user-file")]
    [DataRow("changed-content")]
    [DataRow("started-revision")]
    [DataRow("saved-user-file")]
    public async Task Ready_legacy_directory_capture_only_recovers_exact_download_and_SMB_tombstone(string condition)
    {
        var (task, oldRun, origin) = await CurrentDirectoryInput();
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, oldRun.Id, "Continue with a two-step local plan.",
            [new("main", new() {Requirement = "required", Steps = [
                new() {Id = "outer", Op = "extractArchive", Input = "download"},
                new() {Id = "inner", Op = "extractArchive", Input = "outer"}]}, [origin.Id], origin.Id, InputMode: "currentDirectory")]);
        var prepared = await Plans.ReplanAsync(task.Id, request, default);
        var parts = prepared.Plan!.Parts;
        var captureId = parts.Single().ContinueFrom!.ArtifactId;
        var artifact = await Db.Artifacts.SingleAsync(a => a.Id == captureId);
        var marker = Path.Combine(origin.RootDirectory, ".smbdeleteAAA12ef4.4");
        await File.WriteAllTextAsync(marker, "old open object");
        var files = origin.Files.Append(marker).ToList();
        if (condition == "saved-user-file")
        {
            var userFile = Path.Combine(origin.RootDirectory, ".smbdeletenotes.txt");
            await File.WriteAllTextAsync(userFile, "intentionally selected"); files.Add(userFile);
        }
        // Reconstitute the historical writer's complete directory listing in this fixture.
        var oldManifest = await ArtifactManifestFiles.CaptureAsync(origin.RootDirectory, files);
        artifact.ManifestJson = JsonSerializer.Serialize(oldManifest, WorkflowJson.Options);
        artifact.Fingerprint = oldManifest.Fingerprint;
        var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(artifact.MetadataJson!, WorkflowJson.Options)!;
        artifact.MetadataJson = JsonSerializer.Serialize(receipt with {Fingerprint = oldManifest.Fingerprint}, WorkflowJson.Options);
        var oldPlan = prepared.Plan with {Parts = parts.Select(p => p with
            {ContinueFrom = p.ContinueFrom! with {Fingerprint = oldManifest.Fingerprint}}).ToList()};
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        row.PlanJson = JsonSerializer.Serialize(oldPlan, WorkflowJson.Options);
        var history = prepared.PlanHistory.Select(h => h.Revision != row.PlanRevision ? h : h with {Plan = oldPlan,
            ArtifactEvidence = h.ArtifactEvidence?.Select(a => a.ArtifactId == captureId
                ? a with {Manifest = oldManifest, Fingerprint = oldManifest.Fingerprint} : a).ToList()}).ToList();
        row.PlanHistoryJson = JsonSerializer.Serialize(history, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var revision = row.PlanRevision;
        var oldSnapshot = oldRun.PlanSnapshotJson;
        if (condition == "released") File.Delete(marker);
        if (condition == "new-user-file") await File.WriteAllTextAsync(Path.Combine(origin.RootDirectory, ".smbdeletenotes.txt"), "user");
        if (condition == "changed-content") await File.WriteAllTextAsync(origin.Files.Single(), "changed bytes!");
        if (condition == "started-revision")
        {
            Db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Pending, PlanSnapshotJson = oldSnapshot, CurrentItemJson = "{}",
                Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = revision.ToString()}]});
            await Db.SaveChangesAsync();
        }
        var runs = 0;
        var workflow = new ReserveRunHost(async (plan, _, sources, ct) =>
        {
            Assert.IsFalse(plan.Nodes.Any(n => n.Kind.StartsWith("baidu.") || n.Kind.StartsWith("download.enqueue")));
            var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Pending, PlanSnapshotJson = WorkflowRunPlans.Serialize(plan),
                Sources = sources!.Select(s => new WorkflowRunSourceDbModel {Module = s.Module, EntityKind = s.EntityKind, EntityId = s.EntityId,
                    Revision = s.Revision ?? "", ResultKey = s.ResultKey ?? ""}).ToList()};
            Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync(ct); runs++;
            return new WorkflowRun {Id = run.Id, Status = run.Status};
        });
        var plans = new AcquisitionPlanService(Db, [], _services.GetRequiredService<IAcquisitionService>(), workflow,
            _services.GetRequiredService<DownloadIntentService>(), _services.GetRequiredService<IUserStoragePolicy>(),
            artifacts: Artifacts, workspaces: _services.GetRequiredService<FileProcessingWorkspaceResolver>());
        if (condition is not ("present" or "released"))
        {
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => plans.RunAsync(task.Id, revision, default));
            Assert.AreEqual(revision, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanRevision);
            Assert.AreEqual(0, runs);
            return;
        }
        var artifactCount = await Db.Artifacts.CountAsync();
        Assert.AreEqual(revision, (await plans.PreflightAsync(task.Id, default)).Task.PlanRevision);
        Assert.AreEqual(artifactCount, await Db.Artifacts.CountAsync());
        var started = await plans.RunAsync(task.Id, revision, default);
        Assert.AreEqual(revision + 1, started.Task.PlanRevision);
        var corrected = (await Artifacts.GetArtifactAsync(started.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.IsFalse(corrected.Managed);
        CollectionAssert.AreEqual(origin.Manifest.Entries.ToArray(), corrected.Manifest.Entries.ToArray());
        CollectionAssert.AreEqual(new[] {"outer", "inner"}, started.Plan.Parts.Single().Processing!.Steps.Select(s => s.Id).ToArray());
        var historical = started.PlanHistory.Single(h => h.Revision == revision);
        Assert.AreEqual(captureId, historical.Plan.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(oldManifest.Fingerprint, historical.Plan.Parts.Single().ContinueFrom!.Fingerprint);
        Assert.AreEqual(oldManifest.Fingerprint, (await Artifacts.GetArtifactAsync(captureId))!.Fingerprint);
        Assert.AreEqual(oldSnapshot, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == oldRun.Id)).PlanSnapshotJson);
        Assert.AreEqual(condition == "present", File.Exists(marker));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => plans.RunAsync(task.Id, revision, default));
        Assert.AreEqual(1, runs);
    }
}
