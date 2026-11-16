using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    public async Task LocalFiles_omitted_null_and_false_analysis_options_share_default_behavior_and_replay()
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var omittedJson = JsonSerializer.Serialize(new {previewToken = preview.PreviewToken, taskIds = new[] {f.TaskId}}, WorkflowJson.Options);
        var omitted = JsonSerializer.Deserialize<AcquisitionRecoveryApplyInput>(omittedJson, WorkflowJson.Options)!;
        Assert.IsNull(omitted.AnalyzeFilesFirst);
        var result = await f.Recovery.ApplyAsync(omitted);
        Assert.AreEqual("prepared", result.LocalFiles!.Single().Status);
        Assert.AreEqual("DEPENDENCY_REQUIRED", result.LocalFiles.Single().ReasonCode);
        Assert.AreEqual(result, await f.Recovery.ApplyAsync(omitted with {AnalyzeFilesFirst = false}));
        Assert.AreEqual(result, await f.Recovery.ApplyAsync(omitted with {AnalyzeFilesFirst = null}));
        Assert.AreEqual(0, f.Host.Calls);
    }

    [TestMethod]
    public async Task LocalFiles_explicit_analysis_replaces_old_operations_with_verified_input_and_preserves_source_context()
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var task = await _db.AcquisitionTasks.SingleAsync();
        var original = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)!;
        var part = original.Parts.Single();
        original = original with {Parts = [part with {Sources = [part.Sources.Single() with
            {ArchivePassword = "fixture-literal-password"}]}]};
        task.PlanJson = JsonSerializer.Serialize(original, WorkflowJson.Options);
        task.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, original, [], DateTime.UtcNow)}, WorkflowJson.Options);
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var oldRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var oldDownloads = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync());
        var oldResults = JsonSerializer.Serialize(await _db.DownloadResults.AsNoTracking().ToArrayAsync());
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsTrue(preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true));
        Assert.AreEqual(0, result.Skipped.Count, string.Join(",", result.Skipped.Select(s => s.ReasonCode)));
        Assert.AreEqual("started", result.LocalFiles!.Single().Status);
        var detail = (await f.Plans.GetAsync(f.TaskId, default))!;
        Assert.IsTrue(detail.Plan!.AdaptiveFileProcessing);
        Assert.AreEqual(5, detail.Plan.AdaptiveFileProcessingMaxIterations);
        Assert.AreEqual(original.Parts.Single().Sources.Single(), detail.Plan.Parts.Single().Sources.Single());
        Assert.AreEqual("extractArchive", detail.PlanHistory[0].Plan.Parts.Single().Processing!.Steps.Single().Op);
        var compiled = AcquisitionPlanCompiler.Compile(detail.Plan);
        Assert.IsTrue(compiled.Nodes.All(n => n.Kind is "acquisition.plan.continueFiles" or "acquisition.plan.process" or
            "fs.assessDelivery" or "fs.publishArtifacts"));
        var processing = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(
            compiled.Nodes.Single(n => n.Kind == "acquisition.plan.process").ConfigJson!, WorkflowJson.Options)!;
        Assert.IsTrue(processing.UseVerifiedInput);
        Assert.AreEqual(0, processing.Plan.Steps.Count);
        var assessment = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(
            compiled.Nodes.Single(n => n.Kind == "fs.assessDelivery").ConfigJson!, WorkflowJson.Options)!;
        Assert.IsTrue(assessment.EnableAdaptiveAssessment); Assert.IsTrue(assessment.RequiresAdaptiveDecision);
        Assert.AreEqual(5, assessment.MaxAdaptiveIterations);
        StringAssert.Contains(compiled.Nodes.Single(n => n.Kind == "acquisition.plan.continueFiles").ConfigJson!, "fixture-literal-password");
        Assert.AreEqual(oldRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.RunId)));
        Assert.AreEqual(oldDownloads, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(oldResults, JsonSerializer.Serialize(await _db.DownloadResults.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual(result, await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true, MaxAdaptiveIterations: 5)));
        var changed = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true, MaxAdaptiveIterations: 4)));
        Assert.AreEqual("RECOVERY_OUTCOME_UNKNOWN", changed.Message);
    }

    [DataTestMethod]
    [DataRow(true, false, 50L)]
    [DataRow(true, true, 1L)]
    [DataRow(false, null, 50L)]
    public async Task LocalFiles_analysis_respects_explicit_run_reserve(bool globalEmergency, bool? selectedEmergency, long expectedGiB)
    {
        const long gib = 1024L * 1024 * 1024;
        var f = await LocalFilesFixture();
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        options.Value.MinimumFreeSpaceBytes = 50 * gib;
        options.Value.UseEmergencySpaceReserve = globalEmergency;
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], selectedEmergency, true, 1));
        Assert.AreEqual("started", result.LocalFiles!.Single().Status);
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var grant = AcquisitionStorageOverrideGrant.Read(task);
        if (selectedEmergency == null) Assert.IsNull(grant);
        else
        {
            Assert.AreEqual(selectedEmergency.Value ? "emergency" : "configured", grant!.ReserveMode);
            Assert.AreEqual(task.WorkflowRunId, grant.WorkflowRunId); Assert.AreEqual(2, grant.PlanRevision);
        }
        var reserve = await _services.GetRequiredService<IStorageWritePolicyProvider>().GetMinimumFreeBytesAsync(
            new("workflow", task.WorkflowRunId!.Value.ToString()));
        Assert.AreEqual(expectedGiB * gib, reserve);
        Assert.AreEqual(globalEmergency, options.Value.UseEmergencySpaceReserve);
    }

    [DataTestMethod]
    [DataRow("workflow", true, null)]
    [DataRow("downloads", true, 5)]
    [DataRow("localFiles", false, 5)]
    [DataRow("localFiles", true, 0)]
    [DataRow("localFiles", true, 6)]
    public void Recovery_analysis_options_are_rejected_before_claiming_a_preview(string scope, bool analyze, int? iterations)
    {
        var token = Guid.NewGuid().ToString("N");
        var store = new AcquisitionRecoveryPreviewStore();
        store.Add(new(new(token, DateTimeOffset.UtcNow.AddMinutes(1), 1, 1, 0, [], scope),
            new Dictionary<int, string> {{1, "version"}}, new Dictionary<int, string> {{1, "evidence"}}));
        var error = Assert.ThrowsExactly<ArgumentException>(() => store.Claim(token, [1], analyzeFilesFirst: analyze, maxAdaptiveIterations: iterations));
        Assert.AreEqual("RECOVERY_ANALYSIS_OPTIONS_INVALID", error.Message);
        Assert.IsTrue(store.Claim(token, [1]).Claimed, "An invalid option must not consume a valid preview.");
    }

    [TestMethod]
    public async Task LocalFiles_prepared_analysis_survives_restart_without_reapplying_old_steps()
    {
        var f = await LocalFilesFixture(dependencyMissing: true); f.Host.FailBeforeRun = true;
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Recovery.ApplyAsync(
            new(preview.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true, MaxAdaptiveIterations: 3)));
        var prepared = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(2, prepared.PlanRevision); Assert.IsNull(prepared.WorkflowRunId);
        f.Host.FailBeforeRun = false;
        var restarted = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(), f.Plans, localFiles: f.Local);
        var next = await restarted.PreviewAsync(new([f.TaskId], "localFiles"));
        var result = await restarted.ApplyAsync(new(next.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true, MaxAdaptiveIterations: 3));
        Assert.AreEqual("started", result.LocalFiles!.Single().Status); Assert.AreEqual(2, result.LocalFiles.Single().PlanRevision);
        var current = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(prepared.PlanJson, current.PlanJson); Assert.AreEqual(1, f.Host.Calls);
    }

    [TestMethod]
    public async Task LocalFiles_prepared_old_plan_cannot_silently_satisfy_a_new_analysis_request()
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var prepared = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual("prepared", prepared.LocalFiles!.Single().Status);
        var next = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var result = await f.Recovery.ApplyAsync(new(next.PreviewToken, [f.TaskId], AnalyzeFilesFirst: true));
        Assert.AreEqual("RECOVERY_ANALYSIS_OPTIONS_CHANGED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, f.Host.Calls); Assert.AreEqual(2, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }
}
