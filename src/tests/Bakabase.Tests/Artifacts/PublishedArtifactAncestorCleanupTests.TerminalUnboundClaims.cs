using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow(ArtifactOperationState.Failed)]
    [DataRow(ArtifactOperationState.Unknown)]
    public async Task Terminal_unbound_workflow_claim_can_reclaim_only_its_verified_private_current_file_input(ArtifactOperationState state)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part-fixture:0", currentInputScenario: "captured");
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.State, state).SetProperty(o => o.LeaseUntil, (DateTime?)null));
        var before = await store.GetOperationAsync(f.OperationId);
        var history = await File.ReadAllTextAsync(f.StateFile);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(result.RemovedFiles > 0);
        Assert.IsFalse(File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists), "The user-owned current directory is not part of this private selection.");
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(history, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(before!.State, after!.State);
        Assert.AreEqual(before.LeaseOwner, after.LeaseOwner);
        Assert.IsNull(after.OutputArtifactId);
        Assert.AreEqual(0, await db.ArtifactOperationBindings.CountAsync(b => b.OperationId == f.OperationId));
    }

    [DataTestMethod]
    [DataRow("future-lease")]
    [DataRow("foreign-claim")]
    [DataRow("inactive-consumer")]
    [DataRow("wrong-binding")]
    [DataRow("wrong-producer")]
    [DataRow("unknown-file")]
    [DataRow("manifest-changed")]
    [DataRow("waiting-owner")]
    public async Task Terminal_unbound_private_cleanup_keeps_all_owner_input_and_unknown_file_guards(string scenario)
    {
        var owner = scenario == "foreign-claim" ? "workflow:2:adaptive:part-fixture:0" : "workflow:1:adaptive:part-fixture:0";
        var input = scenario is "wrong-binding" or "wrong-producer" ? scenario : "captured";
        var f = await AbandonedDeletionFixture(owner, currentInputScenario: input);
        if (scenario == "inactive-consumer")
        {
            Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "historical-consumer", TimeSpan.FromMinutes(5)));
            await store.ReleaseConsumerAsync(f.OperationId, "historical-consumer");
        }
        var lease = scenario == "future-lease" ? DateTime.UtcNow.AddMinutes(5) : (DateTime?)null;
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.State, ArtifactOperationState.Failed).SetProperty(o => o.LeaseUntil, lease));
        if (scenario == "waiting-owner")
            await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting));
        if (scenario == "unknown-file")
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.Private)!, "manual.key"), "keep");
        if (scenario == "manifest-changed")
        {
            var json = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
            json["inputManifest"]!["fingerprint"] = "changed";
            await File.WriteAllTextAsync(f.StateFile, json.ToJsonString());
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup?.Groups.Any(g => g.ArtifactId.StartsWith("abandoned-input:") && g.Eligible) == true);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.StateFile));
    }
}
