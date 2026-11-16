using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<string> RetireStagedInputAsync(ArtifactRecord input, ArtifactRecord publication)
    {
        var candidate = (await Preview(publication)).Single(c => c.ArtifactId == input.Id);
        Assert.IsTrue(candidate.Eligible, candidate.Reason);
        var reclaimed = await cleanup.ReclaimAsync(candidate, [1], default);
        Assert.IsTrue(reclaimed.Completed, reclaimed.Reason);
        var retired = (await store.GetArtifactAsync(input.Id))!;
        Assert.AreEqual(ArtifactLocationState.Retired, retired.State);
        var receipt = Path.Combine(root, "file-processing", "quarantine-receipts",
            Path.GetFileName(retired.RootDirectory) + ".json");
        Assert.IsTrue(File.Exists(receipt));
        Assert.IsFalse(File.Exists(input.Files.Single()));
        return receipt;
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Retired_staged_input_reclaims_only_failed_private_copy_with_full_retirement_proof(bool skip)
    {
        var f = await AbandonedDeletionFixture(stagedOnly: true);
        var receiptFile = await RetireStagedInputAsync(f.Raw, f.Final);
        var receiptText = await File.ReadAllTextAsync(receiptFile);
        var stateText = await File.ReadAllTextAsync(f.StateFile);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(stateText, Json)!;
        Assert.IsFalse(state.Completed); Assert.IsNull(state.PendingOperation);
        Assert.AreEqual(0, state.Operations.Count); Assert.AreEqual(0, state.FileTransitions.Count);
        var claim = await store.GetOperationAsync(f.OperationId);
        var retired = await store.GetArtifactAsync(f.Raw.Id);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true,
            SkipFileVerification: skip, RetryRetainedFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:"));
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        Assert.AreEqual(1, group.Files.Count); Assert.AreEqual(f.Private, group.Files.Single().Path);
        Assert.AreEqual(f.Raw.Manifest.Entries.Single().Length, group.Bytes);
        var deleted = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(1, deleted.RemovedFiles);
        Assert.IsFalse(File.Exists(f.Private)); Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(stateText, await File.ReadAllTextAsync(f.StateFile));
        Assert.AreEqual(receiptText, await File.ReadAllTextAsync(receiptFile));
        var after = (await store.GetOperationAsync(f.OperationId))!;
        Assert.AreEqual(claim!.State, after.State); Assert.AreEqual(claim.LeaseToken, after.LeaseToken);
        Assert.IsNull(after.OutputArtifactId);
        var inputAfter = (await store.GetArtifactAsync(f.Raw.Id))!;
        Assert.AreEqual(retired!.State, inputAfter.State); Assert.AreEqual(retired.RootDirectory, inputAfter.RootDirectory);
        var repeated = await f.Service.PreviewAsync(new([f.Task.Id], true, true,
            SkipFileVerification: skip, RetryRetainedFiles: true));
        Assert.AreEqual(0, (await f.Service.DeleteAsync(new(repeated.PreviewId, RetryRetainedFiles: true))).RemovedFiles);
    }

    [DataTestMethod]
    [DataRow("missing-receipt")]
    [DataRow("malformed-receipt")]
    [DataRow("not-purged")]
    [DataRow("different-manifest")]
    [DataRow("different-location")]
    [DataRow("foreign-input-owner")]
    [DataRow("unfinished-input-producer")]
    [DataRow("active-consumer")]
    [DataRow("registered-private-file")]
    [DataRow("unknown-file")]
    [DataRow("pending-operation")]
    [DataRow("no-acceptance")]
    public async Task Retired_staged_input_requires_independent_receipt_and_exclusive_untouched_input(string protection)
    {
        var f = await AbandonedDeletionFixture(stagedOnly: true);
        var receiptFile = await RetireStagedInputAsync(f.Raw, f.Final);
        var accept = protection != "no-acceptance";
        switch (protection)
        {
            case "missing-receipt": File.Delete(receiptFile); break;
            case "malformed-receipt": await File.WriteAllTextAsync(receiptFile, "{"); break;
            case "not-purged":
            case "different-manifest":
            case "different-location":
            {
                var receipt = JsonNode.Parse(await File.ReadAllTextAsync(receiptFile))!;
                if (protection == "not-purged") receipt["status"] = "Quarantined";
                else if (protection == "different-manifest") receipt["manifest"]!["entries"]![0]!["sha256"] = new string('f', 64);
                else receipt["originalRoot"] = "/outside/unrelated";
                await File.WriteAllTextAsync(receiptFile, receipt.ToJsonString());
                break;
            }
            case "foreign-input-owner":
                Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "foreign", f.Raw.OperationId));
                break;
            case "unfinished-input-producer":
                await db.ArtifactOperations.Where(o => o.Id == f.Raw.OperationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Failed));
                break;
            case "active-consumer":
                Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "outside-reader", TimeSpan.FromMinutes(5)));
                break;
            case "registered-private-file":
                await store.AdoptAsync(new("outside-private", "outside-private", "manual", "input"),
                    new(Path.GetDirectoryName(f.Private)!, [f.Private]), "another-owner");
                break;
            case "unknown-file":
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.Private)!, "unknown.key"), "preserve");
                break;
            case "pending-operation":
            {
                var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
                state["pendingOperation"] = new JsonObject();
                await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
                break;
            }
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, accept,
            SkipFileVerification: accept, RetryRetainedFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup?.Groups.Any(g => g.ArtifactId.StartsWith("abandoned-input:") && g.Eligible) == true, protection);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private), protection);
        Assert.IsTrue(f.Final.Files.All(File.Exists)); Assert.IsTrue(File.Exists(f.StateFile));
    }

    [DataTestMethod]
    [DataRow("receipt")]
    [DataRow("consumer")]
    [DataRow("private-file")]
    public async Task Retired_staged_input_rechecks_proof_and_private_identity_after_preview(string change)
    {
        var f = await AbandonedDeletionFixture(stagedOnly: true);
        var receipt = await RetireStagedInputAsync(f.Raw, f.Final);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true,
            SkipFileVerification: true, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        if (change == "receipt") File.Delete(receipt);
        else if (change == "consumer") Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "new-reader", TimeSpan.FromMinutes(5)));
        else File.SetLastWriteTimeUtc(f.Private, File.GetLastWriteTimeUtc(f.Private).AddMinutes(1));
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private)); Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Retired_staged_input_keeps_normal_content_verification_when_not_explicitly_skipped(bool skip)
    {
        var f = await AbandonedDeletionFixture(stagedOnly: true);
        await RetireStagedInputAsync(f.Raw, f.Final);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true,
            SkipFileVerification: skip, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        var stamp = File.GetLastWriteTimeUtc(f.Private);
        await File.WriteAllTextAsync(f.Private, new string('x', (int)new FileInfo(f.Private).Length));
        File.SetLastWriteTimeUtc(f.Private, stamp);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(!skip, File.Exists(f.Private)); Assert.IsTrue(f.Final.Files.All(File.Exists));
    }
}
