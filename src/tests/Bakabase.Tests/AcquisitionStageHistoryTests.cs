using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionStageHistoryTests
{
    private static WorkflowNodeExecutionDbModel Node(string kind, int index, string status, int attempt = 1,
        string resumeKind = "initial") => new()
    {
        NodeId = $"node-{index}", Kind = kind, StepIndex = index, Status = status, Attempt = attempt,
        ResumeKind = resumeKind, StartedAt = DateTime.UtcNow
    };

    [TestMethod]
    public async Task Attempts_group_sequential_steps_keep_retry_boundaries_and_use_the_retained_input_stage()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        db.Add(task);
        await db.SaveChangesAsync();
        var before = new WorkflowRunDbModel
        {
            Sources = [new() { Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString() }],
            NodeExecutions = [
                Node("download.selectSources", 0, "succeeded"),
                Node("download.awaitCompletion", 1, "waiting"),
                Node("download.awaitCompletion", 1, "succeeded", 2, "signal"),
                Node("acquisition.plan.process", 2, "succeeded"),
                Node("fs.assessDelivery", 3, "waiting")]
        };
        var after = new WorkflowRunDbModel
        {
            PlanSnapshotJson = """{"nodes":[{"kind":"acquisition.plan.continueFiles"},{"kind":"fs.publishArtifacts"}]}""",
            NodeExecutions = [Node("acquisition.plan.continueFiles", 0, "succeeded"), Node("fs.publishArtifacts", 1, "succeeded"),
                Node("download.cleanupArtifacts", 2, "succeeded")]
        };
        after.NodeExecutions[1].OutputsJson = """[{"kind":"artifact","key":"published","artifactId":"confirmed-file","directory":"/output","fileCount":1}]""";
        db.AddRange(before, after);
        await db.SaveChangesAsync();
        task.WorkflowRunId = after.Id;
        await db.SaveChangesAsync();

        var attempts = await AcquisitionStageHistory.ReadAsync(db, task.Id, after.Id, default);
        CollectionAssert.AreEqual(new[] {"download:1:waiting", "download:2:completed", "processing:1:waiting", "delivery:1:completed"},
            attempts.Select(x => $"{x.Stage}:{x.Attempt}:{x.Status}").ToArray());
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "fs.publishArtifacts", "download.cleanupArtifacts"},
            attempts[^1].Nodes.ToArray());
        Assert.AreEqual("confirmed-file", attempts[^1].Outputs.Single().ArtifactId);
        var readAgain = await AcquisitionStageHistory.ReadAsync(db, task.Id, after.Id, default);
        CollectionAssert.AreEqual(attempts.Select(x => x.Id).ToArray(), readAgain.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task AssessmentReceiptsRemainDiscoverableWhenTheModelSucceededButItsProcessingStepWaits()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        using var cleanup = services as IDisposable;
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        db.Add(task);
        await db.SaveChangesAsync();
        var metadata = new WorkflowOutputMetadataCapture();
        metadata.Add([AssessmentCallReceipt.Create("artifact", 0, null,
            "{\"status\":\"continue\",\"reason\":\"private context\"}", true).Block("part")]);
        var node = Node("fs.assessDelivery", 0, "waiting");
        node.OutputsJson = metadata.Json;
        var run = new WorkflowRunDbModel
        {
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}],
            Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 0,
            PlanSnapshotJson = """{"nodes":[{"kind":"fs.assessDelivery"}]}""",
            WaitReason = "cloud.fileProcessing", WaitPromptJson = """{"reason":"ProcessingInputNotMatched"}""",
            NodeExecutions = [node, Node("fs.assessDelivery", 0, "waiting", 2, "signal")]
        };
        db.Add(run);
        await db.SaveChangesAsync();
        var attempts = await AcquisitionStageHistory.ReadAsync(db, task.Id, run.Id, default);
        Assert.IsTrue(attempts[0].HasAssessmentCalls);
        Assert.IsFalse(attempts[1].HasAssessmentCalls, "A later manual continuation did not call the model.");
        Assert.IsTrue(attempts.All(a => a.Outputs.Count == 0), "The listing exposes presence only, not the private receipt.");
        Assert.IsFalse(JsonSerializer.Serialize(attempts).Contains("private context"));
    }

    [TestMethod]
    public async Task Latest_saved_assessment_wait_is_scoped_to_its_own_invocation_and_never_exposes_raw_text()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        db.Add(task);
        await db.SaveChangesAsync();
        WorkflowRunDbModel Waiting(string message) => new()
        {
            Sources = [new() { Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString() }],
            Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 0, WaitReason = "cloud.fileProcessing",
            PlanSnapshotJson = """{"nodes":[{"kind":"fs.assessDelivery"}]}""",
            WaitPromptJson = JsonSerializer.Serialize(new { adaptiveAssessment = true, deliveryReview = true, message }),
            NodeExecutions = [Node("fs.assessDelivery", 0, "waiting"), Node("fs.assessDelivery", 0, "waiting", 2, "signal")]
        };
        var old = Waiting("LLM_DECISION_INVALID: 模型返回的处理方案不是有效 JSON。");
        var current = Waiting("LLM_OUTPUT_TRUNCATED: private provider text");
        var unrelated = Waiting("unknown: private file name");
        var wrongCursor = Waiting("LLM_ASSESSMENT_FAILED");
        wrongCursor.CurrentStepIndex = 1;
        old.NodeExecutions[0].OutputsJson = """[{"kind":"warning","key":"adaptive-assessment","code":"LLM_ASSESSMENT_FAILED"}]""";
        db.AddRange(old, current, unrelated, wrongCursor);
        await db.SaveChangesAsync();
        var attempts = await AcquisitionStageHistory.ReadAsync(db, task.Id, current.Id, default);
        var oldAttempts = attempts.Where(x => x.RunId == old.Id).ToArray();
        Assert.AreEqual("LLM_ASSESSMENT_FAILED", oldAttempts[0].Outputs.Single().Code);
        Assert.AreEqual("LLM_DECISION_INVALID_JSON", oldAttempts[1].Outputs.Single().Code);
        var currentAttempts = attempts.Where(x => x.RunId == current.Id).ToArray();
        Assert.AreEqual(0, currentAttempts[0].Outputs.Count);
        var diagnostic = currentAttempts[1].Outputs.Single();
        Assert.AreEqual("adaptive-assessment", diagnostic.Key);
        Assert.AreEqual("LLM_OUTPUT_TRUNCATED", diagnostic.Code);
        Assert.IsNull(diagnostic.Message);
        Assert.IsTrue(attempts.Where(x => x.RunId == unrelated.Id || x.RunId == wrongCursor.Id)
            .All(x => x.Outputs.Count == 0));
    }

    [TestMethod]
    public async Task Backfill_requires_agreement_excludes_shared_or_foreign_runs_and_preserves_waits()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var first = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        var second = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        WorkflowRunDbModel OldRun(int ownerId) => new()
        {
            Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 1, WaitPromptJson = """{"reason":"decision"}""",
            PayloadJson = JsonSerializer.Serialize(new {id = $"acquisition:{ownerId}:2"}),
            PlanSnapshotJson = """{"nodes":[{"kind":"download.awaitCompletion"},{"kind":"fs.assessDelivery"}]}""",
            StepStatsJson = """[{"stepIndex":0,"kind":"download.awaitCompletion","failedCount":0}]"""
        };
        var previous = OldRun(first.Id);
        var current = OldRun(first.Id);
        var conflicting = OldRun(first.Id);
        var foreign = OldRun(second.Id);
        db.AddRange(previous, current, conflicting, foreign);
        await db.SaveChangesAsync();
        first.WorkflowRunId = current.Id;
        first.PlanHistoryJson = JsonSerializer.Serialize(new[] { previous.Id, conflicting.Id, foreign.Id }
            .Select(id => new {previousRunId = id}));
        second.WorkflowRunId = conflicting.Id;
        await db.SaveChangesAsync();

        Assert.AreEqual(2, await AcquisitionStageHistoryMigration.BackfillAsync(db));
        Assert.AreEqual(0, await AcquisitionStageHistoryMigration.BackfillAsync(db));
        var links = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().ToListAsync();
        CollectionAssert.AreEquivalent(new[] { previous.Id, current.Id }, links.Select(l => l.WorkflowRunId).ToArray());
        Assert.IsTrue(links.All(l => l.EntityId == first.Id.ToString() && l.Revision == "2"));
        var attempts = await AcquisitionStageHistory.ReadAsync(db, first.Id, current.Id, default);
        Assert.AreEqual(4, attempts.Count);
        Assert.IsTrue(attempts.All(a => a.Legacy && a.StartedAt == null && a.Outputs.Count == 0));
        Assert.AreEqual("waiting", attempts[^1].Status);
        Assert.AreEqual(0, await db.WorkflowNodeExecutions.CountAsync());
        Assert.AreEqual(current.WaitPromptJson, (await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == current.Id)).WaitPromptJson);
    }
}
