using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Uncommitted_no_transform_copies_reclaim_both_registered_input_replicas_and_preserve_history(bool retiredInput, bool unbound)
    {
        var f = await AbandonedDeletionFixture(unbound ? "workflow:1:adaptive:part-fixture:0" : null,
            completedNoTransform: true);
        if (unbound)
            await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        if (retiredInput)
        {
            File.Delete(f.Raw.Files.Single());
            await db.Artifacts.Where(a => a.Id == f.Raw.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        }
        var stateText = await File.ReadAllTextAsync(f.StateFile);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(stateText, Json)!;
        var resultFile = state.FinalFiles.Single();
        Assert.IsTrue(state.Completed);
        Assert.IsTrue(File.Exists(f.Private)); Assert.IsTrue(File.Exists(resultFile));
        var claim = await store.GetOperationAsync(f.OperationId);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true, RetryRetainedFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:"));
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        Assert.AreEqual(2, group.Files.Count);
        Assert.AreEqual(2 * f.Raw.Manifest.Entries.Single().Length, group.Bytes);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsFalse(File.Exists(f.Private)); Assert.IsFalse(File.Exists(resultFile));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(stateText, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(claim!.State, after!.State); Assert.AreEqual(claim.Attempt, after.Attempt);
        Assert.AreEqual(claim.LeaseToken, after.LeaseToken); Assert.IsNull(after.OutputArtifactId);
        var repeated = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true, RetryRetainedFiles: true));
        Assert.AreEqual(0, (await f.Service.DeleteAsync(new(repeated.PreviewId, RetryRetainedFiles: true))).RemovedFiles);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Uncommitted_copy_skip_mode_does_not_hash_payloads_while_verified_mode_checks_each_copy(bool skip)
    {
        var f = await AbandonedDeletionFixture(completedNoTransform: true);
        File.Delete(f.Raw.Files.Single());
        await db.Artifacts.Where(a => a.Id == f.Raw.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.StateFile), Json)!;
        var resultFile = state.FinalFiles.Single();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: skip, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        // Alter only the result copy's equal-sized bytes, keeping its metadata fixed. Verified
        // mode must inspect result as well as inputs; explicit skip must not hash either copy.
        var stamp = File.GetLastWriteTimeUtc(resultFile);
        await File.WriteAllTextAsync(resultFile, new string('x', (int)new FileInfo(resultFile).Length));
        File.SetLastWriteTimeUtc(resultFile, stamp);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(!skip, File.Exists(f.Private));
        Assert.AreEqual(!skip, File.Exists(resultFile));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("missing-input-record")]
    [DataRow("uncommitted-input-producer")]
    [DataRow("foreign-input-owner")]
    [DataRow("registered-result")]
    [DataRow("waiting-owner")]
    [DataRow("active-consumer")]
    [DataRow("unknown-result-file")]
    [DataRow("transformed-checkpoint")]
    [DataRow("unrecorded-result-content")]
    [DataRow("different-final-manifest")]
    public async Task Uncommitted_copies_require_independent_input_proof_no_processing_and_no_other_readers(string protection)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part-fixture:0", completedNoTransform: true);
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.StateFile), Json)!;
        var resultFile = state.FinalFiles.Single();
        switch (protection)
        {
            case "missing-input-record":
                await db.Artifacts.Where(a => a.Id == f.Raw.Id).ExecuteDeleteAsync();
                break;
            case "uncommitted-input-producer":
                await db.ArtifactOperations.Where(o => o.Id == f.Raw.OperationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Failed));
                break;
            case "foreign-input-owner":
                Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "other-owner", f.Raw.OperationId));
                break;
            case "registered-result":
                await store.AdoptAsync(new("another-owner", "another-owner", "manual", "input"),
                    new(Path.GetDirectoryName(resultFile)!, [resultFile]), "other-owner");
                break;
            case "waiting-owner":
                await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting));
                break;
            case "active-consumer":
                Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "reader", TimeSpan.FromMinutes(5)));
                break;
            case "unknown-result-file":
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(resultFile)!, "manual.key"), "retain unknown attachment");
                break;
            case "transformed-checkpoint":
                state.Operations["completed-extraction"] = new() {Completed = true};
                await File.WriteAllTextAsync(f.StateFile, JsonSerializer.Serialize(state, Json));
                break;
            case "unrecorded-result-content":
                state.FileContents.Remove(ArtifactManifestFiles.RelativeFile(Path.GetDirectoryName(f.StateFile)!, resultFile));
                await File.WriteAllTextAsync(f.StateFile, JsonSerializer.Serialize(state, Json));
                break;
            case "different-final-manifest":
                state.FinalManifest = state.FinalManifest! with {Entries = state.FinalManifest.Entries.Select(e => e with {Sha256 = new string('f', 64)}).ToArray()};
                await File.WriteAllTextAsync(f.StateFile, JsonSerializer.Serialize(state, Json));
                break;
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true, RetryRetainedFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup?.Groups.Any(g => g.ArtifactId.StartsWith("abandoned-input:") && g.Eligible) == true);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private), protection);
        Assert.IsTrue(File.Exists(resultFile), protection);
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.StateFile));
    }
}
