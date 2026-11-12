using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunActivityKindTests
{
    [TestMethod]
    public void DbProjectionUsesLatestAttemptAtTheCursorRatherThanOtherNodesOrOldStats()
    {
        var row = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 1,
            StepStatsJson = JsonSerializer.Serialize(new[] {new WorkflowRunStepStat {StepIndex = 1, Kind = "old.stats"}}, WorkflowJson.Options),
            NodeExecutions = [
                new() {Id = 10, NodeId = "activity:2", StepIndex = 1, Attempt = 1, Kind = "old.attempt", Status = "waiting"},
                new() {Id = 12, NodeId = "activity:3", StepIndex = 2, Attempt = 1, Kind = "other.node", Status = "succeeded"},
                new() {Id = 11, NodeId = "activity:2", StepIndex = 1, Attempt = 2, Kind = "recorded.current", Status = "waiting"}]};
        Assert.AreEqual("recorded.current", WorkflowRunViewModel.From(row.ToDomainModel()).ActivityKind);
    }

    [TestMethod]
    public void LegacyRunCanOnlyFallBackToItsExactRecordedStep()
    {
        var run = new WorkflowRun {Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 1,
            StepStats = [new() {StepIndex = 0, Kind = "upstream"}]};
        Assert.IsNull(WorkflowRunViewModel.From(run).ActivityKind);
        run.StepStats.Add(new() {StepIndex = 1, Kind = "recorded.legacy"});
        Assert.AreEqual("recorded.legacy", WorkflowRunViewModel.From(run).ActivityKind);
    }

    [TestMethod]
    public void CursorlessBatchUsesOnlyARunningAttemptAndCompletedRunsRemainWithoutCurrentKind()
    {
        var run = new WorkflowRun {Status = WorkflowRunStatus.Running,
            NodeExecutions = [new() {Id = 1, Kind = "previous", Status = "succeeded"},
                new() {Id = 2, Kind = "current.batch", Status = "running"}]};
        Assert.AreEqual("current.batch", WorkflowRunViewModel.From(run).ActivityKind);
        run.Status = WorkflowRunStatus.Success;
        Assert.IsNull(WorkflowRunViewModel.From(run).ActivityKind);
    }
}
