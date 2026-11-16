using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    public async Task LocalFiles_prepare_only_registers_each_download_once_and_leaves_execution_for_later()
    {
        var first = await LocalFilesFixture();
        var originalRoot = _root;
        _root = Directory.CreateDirectory(Path.Combine(originalRoot, "second-task")).FullName;
        int secondId;
        try { secondId = (await LocalFilesFixture()).TaskId; }
        finally { _root = originalRoot; }

        int[] ids = [first.TaskId, secondId];
        var downloads = await _db.DownloadTasks.AsNoTracking().OrderBy(d => d.Id).ToArrayAsync();
        var results = await _db.DownloadResults.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync();
        var oldRuns = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync());
        var preview = await first.Recovery.PreviewAsync(new(ids, "localFiles"));
        Assert.AreEqual(2, preview.ContinuableCount);
        Assert.IsTrue(preview.Rows.All(r => r.LocalFiles?.SupportsPrepareOnly == true));
        var input = new AcquisitionRecoveryApplyInput(preview.PreviewToken, ids, PrepareOnly: true);
        var saved = await first.Recovery.ApplyAsync(input);
        Assert.AreEqual(0, saved.Skipped.Count);
        Assert.AreEqual(0, saved.ContinuedTaskIds.Count);
        Assert.AreEqual(2, saved.LocalFiles!.Count);
        Assert.IsTrue(saved.LocalFiles.All(row => row is {Status: "prepared", WorkflowRunId: null, PlanRevision: 2, ReasonCode: null}));
        Assert.AreEqual(saved, await first.Recovery.ApplyAsync(input));
        var changed = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            first.Recovery.ApplyAsync(input with {PrepareOnly = false}));
        Assert.AreEqual("RECOVERY_OUTCOME_UNKNOWN", changed.Message);

        var restarted = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(), first.Plans, localFiles: first.Local);
        var fresh = await restarted.PreviewAsync(new(ids, "localFiles"));
        var replay = await restarted.ApplyAsync(new(fresh.PreviewToken, ids, PrepareOnly: true));
        CollectionAssert.AreEqual(saved.LocalFiles.ToArray(), replay.LocalFiles!.ToArray());
        foreach (var id in ids)
        {
            var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            var receipt = AcquisitionLocalFilesRecoveryReceipt.Read(task)!;
            Assert.IsFalse(task.BuilderStateJson!.Contains("supportsPrepareOnly", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(2, receipt.ResultingRevision);
            Assert.IsNotNull(receipt.Parts.Single().ArtifactId);
            Assert.IsTrue(await first.Plans.CanAnalyzePreparedFilesAsync(task, default));
            Assert.AreEqual("ready", task.Phase);
        }
        Assert.AreEqual(0, first.Host.Calls);
        Assert.AreEqual(2, await _db.Artifacts.CountAsync());
        Assert.AreEqual(oldRuns, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync()));
        Assert.AreEqual(JsonSerializer.Serialize(downloads), JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().OrderBy(d => d.Id).ToArrayAsync()));
        Assert.AreEqual(JsonSerializer.Serialize(results), JsonSerializer.Serialize(await _db.DownloadResults.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync()));
        foreach (var result in results)
            foreach (var file in JsonSerializer.Deserialize<string[]>(result.FilesJson)!)
                Assert.AreEqual("verified existing bytes", await File.ReadAllTextAsync(file));
        var selected = await restarted.PreviewAsync(new([first.TaskId], "localFiles"));
        var run = await restarted.ApplyAsync(new(selected.PreviewToken, [first.TaskId]));
        Assert.AreEqual("started", run.LocalFiles!.Single().Status);
        Assert.AreEqual(2, run.LocalFiles.Single().PlanRevision);
        Assert.AreEqual(1, first.Host.Calls);
    }

    [DataTestMethod]
    [DataRow("workflow", null, null, null)]
    [DataRow("downloads", null, null, null)]
    [DataRow("localFiles", true, null, null)]
    [DataRow("localFiles", false, 2, null)]
    [DataRow("localFiles", null, null, true)]
    [DataRow("localFiles", null, null, false)]
    public void LocalFiles_prepare_only_rejects_conflicting_options_without_consuming_the_preview(
        string scope, bool? analyze, int? iterations, bool? reserve)
    {
        var token = Guid.NewGuid().ToString("N");
        var store = new AcquisitionRecoveryPreviewStore();
        store.Add(new(new(token, DateTimeOffset.UtcNow.AddMinutes(1), 1, 1, 0, [], scope),
            new Dictionary<int, string> {{1, "version"}}, new Dictionary<int, string> {{1, "evidence"}}));
        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            store.Claim(token, [1], reserve, analyze == true, iterations, prepareOnly: true));
        Assert.AreEqual("RECOVERY_PREPARE_OPTIONS_INVALID", error.Message);
        Assert.IsTrue(store.Claim(token, [1]).Claimed);
    }

    [TestMethod]
    public async Task LocalFiles_prepare_only_leaves_a_pending_stage_file_operation_untouched()
    {
        var f = await LocalFilesFixture();
        var task = await _db.AcquisitionTasks.SingleAsync();
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(task.BuilderStateJson!, WorkflowJson.Options)!;
        task.BuilderStateJson = JsonSerializer.Serialize(state with {ProcessingStageCommitId = "old-commit"}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsNull(preview.Rows.Single().LocalFiles!.SupportsPrepareOnly);
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        Assert.AreEqual("RECOVERY_PREPARATION_CONFLICT", result.Skipped.Single().ReasonCode);
        var current = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(task.BuilderStateJson, current.BuilderStateJson);
        Assert.AreEqual(1, current.PlanRevision);
        Assert.AreEqual(f.RunId, current.WorkflowRunId);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(0, await _db.Artifacts.CountAsync());
    }
}
