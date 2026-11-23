using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionAdaptiveContinuationTests
{
    private static AcquisitionPlanTaskDetail LocalDetail(params AcquisitionPlanArtifactCandidate[] candidates) => new(
        new AcquisitionTask {Id = 42, PlanRevision = 3, WorkflowRunId = 81},
        new AcquisitionPlan {Title = "Local", OutputDirectory = Path.GetTempPath(),
            Parts = [new() {Id = "part"}]}, [], null, [], [], []) {ReplanArtifacts = candidates};

    [TestMethod]
    public void BatchAiUsesTheSameCurrentDirectoryEligibilityWithoutRequiringAnOldAiNode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "current-files");
        var detail = LocalDetail(new AcquisitionPlanArtifactCandidate("part", "old-download", "download", dir, "old", 2, CanReadCurrentDirectory: true));
        var input = AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 4);
        Assert.IsNotNull(input);
        Assert.AreEqual(3, input.ExpectedRevision);
        Assert.AreEqual(81, input.ExpectedRunId);
        Assert.IsTrue(input.EnableAdaptiveAssessment);
        Assert.AreEqual(4, input.MaxAdaptiveIterations);
        Assert.IsTrue(input.UseCurrentStorageDefaults);
        Assert.AreEqual("currentDirectory", input.Items.Single().InputMode);
        Assert.AreEqual("unknown", input.Items.Single().Plan.Requirement);
        Assert.AreEqual("old-download", input.Items.Single().ContinueFromArtifactId);
        Assert.AreEqual(0, input.Items.Single().Plan.Steps.Count);
    }

    [TestMethod]
    public void BatchAiNeverGuessesBetweenDirectoriesOrFallsBackFromAnUnavailableLatestResult()
    {
        var first = Path.Combine(Path.GetTempPath(), "download");
        var second = Path.Combine(Path.GetTempPath(), "processed");
        var detail = LocalDetail(new AcquisitionPlanArtifactCandidate("part", "d", "download", first, "d", 1, CanReadCurrentDirectory: true),
            new("part", "p", "processedFiles", second, "p", 1, CanReadCurrentDirectory: true));
        Assert.IsNull(AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3));
        detail = detail with {StageDirectories = new() {TaskId = 42, Parts = new() { ["part"] = new()
            {Download = new(first, "borrowed", "record"), Processing = new(second, "borrowed", "record")} }}};
        Assert.AreEqual("p", AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3)!.Items.Single().ContinueFromArtifactId);
        detail = detail with {ReplanArtifacts = [detail.ReplanArtifacts[0], detail.ReplanArtifacts[1] with {CanReadCurrentDirectory = false}]};
        Assert.IsNull(AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3));
    }

    [TestMethod]
    public void BatchAiRequiresCurrentFileEvidenceForEveryPartAndDoesNotMutatePriorPlan()
    {
        var dir = Path.GetTempPath();
        var detail = LocalDetail(new AcquisitionPlanArtifactCandidate("part", "p", "processedFiles", dir, "p", 1, CanReadCurrentDirectory: true));
        detail = detail with {Plan = detail.Plan! with {Parts = [detail.Plan.Parts[0], new() {Id = "other"}]}};
        var before = System.Text.Json.JsonSerializer.Serialize(detail);
        Assert.IsNull(AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3));
        Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(detail));
    }

    [TestMethod]
    public void BatchAiUsesDownloadedFilesBeforeTheAllocatedProcessingDirectoryHasAnyResult()
    {
        var download = Path.Combine(Path.GetTempPath(), "download");
        var processing = Path.Combine(Path.GetTempPath(), "processing");
        var detail = LocalDetail(new AcquisitionPlanArtifactCandidate("part", "d", "download", download, "d", 1, CanReadCurrentDirectory: true)) with
        {StageDirectories = new() {TaskId = 42, Parts = new() { ["part"] = new()
            {Download = new(download, "exclusive", "taskAllocation"), Processing = new(processing, "exclusive", "taskAllocation")} }}};
        Assert.AreEqual("d", AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3)!.Items.Single().ContinueFromArtifactId);
        detail = detail with {StageDirectories = detail.StageDirectories with {Parts = new() { ["part"] =
            detail.StageDirectories.Parts["part"] with {Processing = new(processing, "borrowed", "processingRecord")} }}};
        Assert.IsNull(AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3), "A missing actual result cannot silently fall back to downloads.");
    }

    private static WorkflowRunDbModel Waiting(string kind, string prompt) => new()
    {
        Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 0, WaitReason = "cloud.fileProcessing",
        WaitPromptJson = prompt,
        PlanSnapshotJson = WorkflowRunPlans.Serialize(new WorkflowRunPlan
        {
            Name = "Local files", TriggerKind = "cloud.downloadRequested",
            Nodes = [new() {NodeId = "process", Kind = kind, ConfigJson = """{"targetId":"part"}"""}]
        })
    };

    [TestMethod]
    public void Encrypted_archive_waits_for_password_on_legacy_and_adaptive_runs()
    {
        foreach (var kind in new[] {"acquisition.plan.process", "fs.assessDelivery"})
        {
            var run = Waiting(kind, """{"reason":"PasswordUnknown","targetId":"part","artifactId":"known"}""");
            var result = AcquisitionAdaptiveMigrationService.Inspect(new(), run);
            Assert.IsFalse(result.CanMigrate);
            Assert.AreEqual("ADAPTIVE_MIGRATION_PASSWORD_REQUIRED", result.ReasonCode);
        }
    }

    [TestMethod]
    public async Task Queued_reanalysis_changes_only_the_reviewed_assessment_node()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var run = Waiting("fs.assessDelivery", """{"reason":"FILE_DELIVERY_RESULT_UNCERTAIN","targetId":"part","artifactId":"known"}""");
        var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson)!;
        var reviewed = plan.Nodes.Single();
        var other = reviewed with {NodeId = "another-assessment", ConfigJson = """{"enableAdaptiveAssessment":true,"maxAdaptiveIterations":1}"""};
        run.CurrentStepIndex = 1;
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = [other, reviewed]});
        db.Add(run);
        await db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {WorkflowRunId = run.Id, PlanRevision = 2};
        db.Add(task);
        await db.SaveChangesAsync();
        Assert.IsFalse(AcquisitionAdaptiveMigrationService.Inspect(task, run).AlreadyEnabled);
        var service = new AcquisitionAdaptiveMigrationService(db, new AssessmentHost());
        var option = await service.PreviewForBatchAsync(task, run, 4, default);
        Assert.IsNotNull(option);
        var executed = await service.ApplyForBatchAsync(task.Id, option.Value.Fingerprint, 4, default);
        Assert.AreEqual((run.Id, 2), executed);
        var updated = WorkflowRunPlans.Read((await db.WorkflowRuns.FindAsync(run.Id))!.PlanSnapshotJson)!.Nodes;
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(other), System.Text.Json.JsonSerializer.Serialize(updated[0]));
        using var config = System.Text.Json.JsonDocument.Parse(updated[1].ConfigJson);
        Assert.AreEqual(4, config.RootElement.GetProperty("maxAdaptiveIterations").GetInt32());
    }

    private sealed class AssessmentHost : Bakabase.Modules.Workflow.Abstractions.Services.IWorkflowRunResumer
    {
        public Task ReprocessWaitingAsync(int runId, CancellationToken ct) => Task.CompletedTask;
        public Task ResumeAsync(int runId, string signalJson, CancellationToken ct) => throw new AssertFailedException();
        public Task RequeueAsync(int runId, CancellationToken ct) => throw new AssertFailedException();
        public Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct) => throw new AssertFailedException();
    }

    [TestMethod]
    public async Task Preview_cannot_resume_a_different_artifact_at_the_same_cursor()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var run = Waiting("fs.assessDelivery", """{"reason":"FILE_DELIVERY_RESULT_UNCERTAIN","targetId":"part","artifactId":"first"}""");
        db.Add(run);
        await db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {BuilderId = "prepared", WorkflowRunId = run.Id};
        db.Add(task);
        await db.SaveChangesAsync();
        var service = new AcquisitionAdaptiveMigrationService(db, null!);
        var preview = await service.PreviewAsync(new([task.Id]));
        Assert.IsTrue(preview.Rows.Single().CanMigrate);
        run.WaitPromptJson = run.WaitPromptJson!.Replace("first", "second");
        await db.SaveChangesAsync();
        var result = await service.ApplyAsync(preview.Token, [task.Id]);
        Assert.AreEqual("ADAPTIVE_MIGRATION_CHANGED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, result.MigratedTaskIds.Count);
        Assert.AreEqual(WorkflowRunStatus.Waiting, run.Status);
    }
}
