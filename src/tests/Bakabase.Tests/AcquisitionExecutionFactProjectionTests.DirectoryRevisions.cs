using System.Text.Json.Nodes;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionExecutionFactProjectionTests
{
    private static JsonArray DirectoryRevisions(Case item, string stage = "processing", int currentRevision = 2)
    {
        var original = JsonNode.Parse(item.Task.PlanJson!)!;
        var history = new JsonArray();
        for (var revision = 1; revision <= currentRevision; revision++)
        {
            var plan = original.DeepClone();
            if (revision > 1)
                plan["stageDirectories"] = new JsonObject
                {
                    ["version"] = 1,
                    ["parts"] = new JsonObject
                    {
                        ["p"] = new JsonObject
                        {
                            [stage] = new JsonObject
                            {
                                ["directory"] = "/unreachable/current-stage-" + revision,
                                ["ownership"] = "borrowed", ["evidence"] = "userDirectoryRevision",
                                ["bindingId"] = Guid.NewGuid().ToString("N")
                            }
                        }
                    }
                };
            history.Add(new JsonObject
            {
                ["revision"] = revision, ["plan"] = plan, ["sourceRefs"] = new JsonArray(),
                ["reason"] = revision == 1 ? null : "directories.changed",
                ["previousRunId"] = revision == 1 ? null : item.Run.Id
            });
        }
        item.Task.PlanRevision = currentRevision;
        item.Task.PlanJson = history[^1]!["plan"]!.ToJsonString();
        item.Task.PlanHistoryJson = history.ToJsonString();
        return history;
    }

    [DataTestMethod]
    [DataRow("download")]
    [DataRow("processing")]
    [DataRow("delivery")]
    public async Task Directory_only_revisions_preserve_recorded_local_input_without_rewriting_run_history(string stage)
    {
        var item = await Seed(); var artifact = await Artifact(item);
        SetTargets(item, Download("p", artifact)); Prompt(item);
        var history = DirectoryRevisions(item, stage, currentRevision: 3);
        var frozen = item.Run.CurrentItemJson;
        var source = await _db.Set<WorkflowRunSourceDbModel>().SingleAsync();
        Assert.AreEqual("1", source.Revision);

        var facts = await Facts(item);

        Assert.IsTrue(facts.Matches(Both), stage);
        Assert.AreEqual("1", source.Revision); Assert.AreEqual(frozen, item.Run.CurrentItemJson);
        Assert.AreEqual(history.ToJsonString(), item.Task.PlanHistoryJson);
        // Both move and adopt-only edits write this same committed revision contract.
        // Recorded facts need no filesystem reads; continuation still captures current files.
        Assert.IsFalse(Directory.Exists("/unreachable/current-stage-3"));
    }

    [TestMethod]
    public async Task Directory_revision_preserves_successful_adaptive_output_waiting_for_review()
    {
        var item = await Seed(Assess); var artifact = await Artifact(item, kind: "processedFiles");
        (await _db.ArtifactOperationBindings.SingleAsync()).Slot = "p:adaptive:output:" + artifact.Id;
        SetTargets(item, Processed("p", artifact, "FILE_DELIVERY_RESULT_UNCERTAIN") with
        {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "FILE_DELIVERY_RESULT_UNCERTAIN")});
        Prompt(item, reason: "FILE_DELIVERY_RESULT_UNCERTAIN", review: artifact);
        DirectoryRevisions(item);
        var facts = await Facts(item);
        Assert.IsTrue(facts.Matches(["downloadedPending"]));
        Assert.IsFalse(facts.Matches(["spaceInsufficient"]));
    }

    [DataTestMethod]
    [DataRow("no-history")]
    [DataRow("invalid-history")]
    [DataRow("missing-original")]
    [DataRow("missing-middle")]
    [DataRow("duplicate-revision")]
    [DataRow("string-revision")]
    [DataRow("unmarked-edit")]
    [DataRow("wrong-run")]
    [DataRow("different-part-plan")]
    [DataRow("reverted-plan-change")]
    [DataRow("different-source")]
    [DataRow("different-source-refs")]
    [DataRow("current-plan-not-recorded")]
    [DataRow("source-revision-proof")]
    [DataRow("conflicting-run-revision")]
    [DataRow("foreign-task-owner")]
    [DataRow("new-current-run")]
    [DataRow("retired-input")]
    public async Task Directory_revision_facts_do_not_accept_unproven_plan_edits_or_changed_execution(string variant)
    {
        var item = await Seed(); var artifact = await Artifact(item);
        SetTargets(item, Download("p", artifact)); Prompt(item);
        var history = DirectoryRevisions(item, currentRevision: 3);
        switch (variant)
        {
            case "missing-original": history.RemoveAt(0); break;
            case "missing-middle": history.RemoveAt(1); break;
            case "duplicate-revision": history.Add(history[1]!.DeepClone()); break;
            case "string-revision": history[1]!["revision"] = "2"; break;
            case "unmarked-edit": history[1]!["reason"] = "User revised processing instructions."; break;
            case "wrong-run": history[1]!["previousRunId"] = item.Run.Id + 1; break;
            case "different-part-plan":
                history[2]!["plan"]!["parts"]![0]!["processing"] = new JsonObject {["requirement"] = "notRequired"};
                item.Task.PlanJson = history[2]!["plan"]!.ToJsonString(); break;
            case "reverted-plan-change": history[1]!["plan"]!["adaptiveFileProcessingContext"] = "Changed then reverted."; break;
            case "different-source":
                history[2]!["plan"]!["parts"]![0]!["selectedSourceId"] = "another-source";
                item.Task.PlanJson = history[2]!["plan"]!.ToJsonString(); break;
            case "different-source-refs": history[1]!["sourceRefs"] = new JsonArray(new JsonObject {["entityId"] = "another-post"}); break;
            case "current-plan-not-recorded":
                var changed = JsonNode.Parse(item.Task.PlanJson!)!; changed["outputDirectory"] = "/another/root";
                item.Task.PlanJson = changed.ToJsonString(); break;
            case "source-revision-proof": history[1]!["sourceRevision"] = new JsonObject {["decisionId"] = "another-source"}; break;
            case "conflicting-run-revision":
            case "foreign-task-owner":
                _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = item.Run.Id, Module = "acquisition", EntityKind = "task",
                    EntityId = variant == "foreign-task-owner" ? "9999" : item.Task.Id.ToString(), Revision = "2"}); break;
            case "new-current-run":
                var next = new WorkflowRunDbModel {Status = item.Run.Status, CurrentStepIndex = 0, StartedAt = DateTime.UtcNow};
                _db.Add(next); await _db.SaveChangesAsync(); item.Task.WorkflowRunId = next.Id;
                _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = next.Id, Module = "acquisition", EntityKind = "task",
                    EntityId = item.Task.Id.ToString(), Revision = "1"}); break;
            case "retired-input": artifact.State = Bakabase.Modules.Workflow.Artifacts.ArtifactLocationState.Retired; break;
        }
        item.Task.PlanHistoryJson = variant == "no-history" ? null : variant == "invalid-history" ? "{broken" : history.ToJsonString();
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]), variant);
    }
}
