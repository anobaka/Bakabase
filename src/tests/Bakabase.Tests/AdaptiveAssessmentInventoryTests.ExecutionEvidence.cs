using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    private static (ArtifactRecord Input, ArtifactRecord Output, ArtifactOperationDbModel Operation) CompletedExecution()
    {
        var input = Input("private/archive.zip") with {Id = "input", Fingerprint = "input-fingerprint"};
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new()
            {Id = "private-step", Op = "extractArchive", Selector = "private/archive.zip", Password = "private-password"}]};
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id,
            JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "private-parent-metadata"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var output = Input("output.mp4") with {Id = "output", OperationId = "completed-operation", Fingerprint = "output-fingerprint",
            ParentArtifactIds = [input.Id], MetadataJson = metadata};
        var operation = new ArtifactOperationDbModel {Id = output.OperationId, Kind = "files.process", State = ArtifactOperationState.Completed,
            OutputArtifactId = output.Id, InputFingerprint = input.Fingerprint};
        return (input, output, operation);
    }

    [TestMethod]
    public void Completed_processing_context_is_bound_to_current_output_and_excludes_private_plan_values()
    {
        var (input, output, operation) = CompletedExecution();
        var completed = AdaptiveProcessingExecutionEvidence.Capture(1, input, output, operation);
        Assert.IsNotNull(completed);
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null, null, null,
            AdaptiveProcessingExecutionEvidence.ForCurrent(output, [completed]));
        using var json = JsonDocument.Parse(context);
        var evidence = json.RootElement.GetProperty("executionEvidence");
        Assert.AreEqual(output.Id, evidence.GetProperty("currentArtifactId").GetString());
        Assert.AreEqual(output.Fingerprint, evidence.GetProperty("currentFingerprint").GetString());
        var execution = evidence.GetProperty("completedProcessing")[0];
        Assert.AreEqual(1, execution.GetProperty("iteration").GetInt32());
        Assert.AreEqual(input.Id, execution.GetProperty("inputArtifactId").GetString());
        Assert.AreEqual("extractArchive", execution.GetProperty("operations")[0].GetProperty("kind").GetString());
        Assert.AreEqual(1, execution.GetProperty("operations")[0].GetProperty("count").GetInt32());
        Assert.IsFalse(context.Contains("private", StringComparison.Ordinal));
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "do not apply the same");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "authorize deletion");
    }

    [DataTestMethod]
    [DataRow("missing")] [DataRow("running")] [DataRow("failed")] [DataRow("wrong-output")]
    [DataRow("wrong-input")] [DataRow("wrong-parent")] [DataRow("partial")]
    [DataRow("current-directory")] [DataRow("invalid-plan")]
    public void Proposed_failed_partial_or_unbound_processing_is_never_described_as_completed(string fault)
    {
        var (input, output, operation) = CompletedExecution();
        switch (fault)
        {
            case "running": operation.State = ArtifactOperationState.Running; break;
            case "failed": operation.State = ArtifactOperationState.Failed; break;
            case "wrong-output": operation.OutputArtifactId = "another-output"; break;
            case "wrong-input": operation.InputFingerprint = "another-input"; break;
            case "wrong-parent": output = output with {ParentArtifactIds = ["another-input"]}; break;
            case "current-directory": output = output with {Kind = "currentFiles"}; break;
            case "partial": output = output with {MetadataJson = output.MetadataJson!.Replace("\"completedThroughStepId\":null", "\"completedThroughStepId\":\"private-step\"")}; break;
            case "invalid-plan": output = output with {MetadataJson = output.MetadataJson!.Replace("extractArchive", "deleteAll")}; break;
        }
        Assert.IsNull(AdaptiveProcessingExecutionEvidence.Capture(1, input, output, fault == "missing" ? null : operation));
    }

    [TestMethod]
    public void Stale_or_disconnected_history_cannot_describe_a_different_current_artifact()
    {
        var (input, output, operation) = CompletedExecution();
        var completed = AdaptiveProcessingExecutionEvidence.Capture(1, input, output, operation)!;
        Assert.IsNull(AdaptiveProcessingExecutionEvidence.ForCurrent(output with {Fingerprint = "changed"}, [completed]));
        Assert.IsNull(AdaptiveProcessingExecutionEvidence.ForCurrent(output, [completed with {OutputArtifactId = "different"}]));
        Assert.IsNull(AdaptiveProcessingExecutionEvidence.ForCurrent(output,
            [completed, completed with {Iteration = 2, InputArtifactId = "different"}]));
    }

    [TestMethod]
    public void Execution_summary_does_not_relax_incomplete_inventory_final_guard()
    {
        var (_, output, _) = CompletedExecution();
        var inventory = Input(Enumerable.Range(0, 201).Select(i => $"{i}.jpg").ToArray()) with {Id = output.Id, Fingerprint = output.Fingerprint};
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null, null, null,
            new(output.Id, output.Fingerprint, [new(1, "input", "input-fingerprint", output.Id, output.Fingerprint, [new("extractArchive", 1)])]));
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(inventory.Manifest);
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"status":"final","operations":[]}""")),
            inventory, view.Paths, view.Truncated, context);
        Assert.IsTrue(decision.NeedsHumanReview);
        Assert.AreEqual("INCOMPLETE_FILE_INVENTORY", decision.DiagnosticCode);
    }
}
