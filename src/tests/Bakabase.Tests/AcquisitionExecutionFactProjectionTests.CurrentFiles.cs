using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionExecutionFactProjectionTests
{
    [TestMethod]
    [DataRow(Process)]
    [DataRow(Assess)]
    [DataRow(Publish)]
    public async Task CurrentDirectoryContinuationRemainsDownloadedPendingAtEachLocalStage(string kind)
    {
        var item = await Seed(kind);
        var artifact = await Artifact(item, kind: AcquisitionCurrentDirectoryFiles.Kind, slot: "p:artifact.continuation");
        if (kind == Process)
        {
            SetNodes(item, Node("current", Process, "p", new {targetId = "p", continueFrom = new
                {artifactId = artifact.Id, operationId = artifact.OperationId, fingerprint = artifact.Fingerprint}}));
            SetTargets(item, new CloudDownloadTarget {Id = "p", ProcessingWaitReason = "PasswordUnknown"});
        }
        else
        {
            var target = Processed("p", artifact, "PasswordUnknown");
            if (kind == Assess) target = target with
            {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "PasswordUnknown")};
            SetTargets(item, target);
        }
        Prompt(item, reason: "PasswordUnknown", review: kind == Assess ? artifact : null);
        var facts = await Facts(item);
        Assert.IsTrue(facts.Matches(["downloadedPending"]));
        Assert.IsFalse(facts.Matches(["spaceInsufficient"]));
        Assert.AreEqual("/unreachable/fact-test", artifact.RootPath, "List facts must not inspect user storage.");
    }

    [TestMethod]
    [DataRow("other-run")]
    [DataRow("other-task")]
    [DataRow("old-revision")]
    [DataRow("other-part")]
    [DataRow("wrong-binding-kind")]
    [DataRow("processing-slot-only")]
    [DataRow("wrong-operation-kind")]
    [DataRow("wrong-operation-id")]
    [DataRow("incomplete-operation")]
    [DataRow("stale-fingerprint")]
    [DataRow("missing-fingerprint")]
    [DataRow("unavailable-artifact")]
    [DataRow("pending-resume")]
    public async Task CurrentDirectoryFactsRequireExactCurrentAdoptionAndReview(string variant)
    {
        var item = await Seed(Assess);
        var artifact = await Artifact(item, kind: AcquisitionCurrentDirectoryFiles.Kind, slot: "p:artifact.continuation");
        var target = Processed("p", artifact) with
        {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")};
        var binding = await _db.ArtifactOperationBindings.SingleAsync();
        switch (variant)
        {
            case "other-run": binding.OwnerId = "99999"; break;
            case "other-task": (await _db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = (item.Task.Id + 1).ToString(); break;
            case "old-revision": (await _db.Set<WorkflowRunSourceDbModel>().SingleAsync()).Revision = "0"; break;
            case "other-part": binding.Slot = "q:artifact.continuation"; break;
            case "wrong-binding-kind": binding.OwnerKind = "other-module"; break;
            case "processing-slot-only": binding.Slot = "p:process:complete"; break;
            case "wrong-operation-kind": (await _db.ArtifactOperations.SingleAsync()).Kind = "files.process"; break;
            case "wrong-operation-id": target = target with {ProcessingOperationId = "other-operation"}; break;
            case "incomplete-operation": (await _db.ArtifactOperations.SingleAsync()).State = ArtifactOperationState.Running; break;
            case "stale-fingerprint": target = target with {DeliveryReview = target.DeliveryReview! with {Fingerprint = new string('b', 64)}}; break;
            case "missing-fingerprint": target = target with {DeliveryReview = target.DeliveryReview! with {Fingerprint = ""}}; break;
            case "unavailable-artifact": artifact.State = ArtifactLocationState.Modified; break;
            case "pending-resume": item.Run.PendingSignalJson = "{}"; break;
        }
        SetTargets(item, target); Prompt(item, review: artifact);
        var facts = await Facts(item);
        Assert.IsFalse(facts.Matches(["downloadedPending"]), variant);
        Assert.IsFalse(facts.Matches(Both), variant);
    }

    [TestMethod]
    public async Task CurrentDirectoryContinuationFingerprintMustMatchAtProcessingInput()
    {
        var item = await Seed();
        var artifact = await Artifact(item, kind: AcquisitionCurrentDirectoryFiles.Kind, slot: "p:artifact.continuation");
        SetTargets(item, new CloudDownloadTarget {Id = "p", ProcessingWaitReason = "DiskSpaceLimit"}); Prompt(item);
        foreach (var fingerprint in new[] {"", new string('b', 64)})
        {
            SetNodes(item, Node("current", Process, "p", new {targetId = "p", continueFrom = new
                {artifactId = artifact.Id, operationId = artifact.OperationId, fingerprint}}));
            Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
    }

    [TestMethod]
    public async Task CurrentDirectoryInputCannotSupplyMissingLocalFilesForAnotherPart()
    {
        var item = await Seed(Assess, "a", "b");
        var artifact = await Artifact(item, "a", AcquisitionCurrentDirectoryFiles.Kind, "a:artifact.continuation");
        SetNodes(item, Node("current", Assess, "a"), Node("next", Process, "b"), Node("publish", Publish));
        SetTargets(item, Processed("a", artifact) with
        {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")}, new() {Id = "b"});
        Prompt(item, "a", review: artifact);
        var facts = await Facts(item);
        Assert.IsFalse(facts.Matches(["downloadedPending"]));
        Assert.IsFalse(facts.Matches(Both));
    }
}
