using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionExecutionFactProjectionTests
{
    [DataTestMethod]
    [DataRow(Assess)]
    [DataRow(Publish)]
    public async Task SuccessfulAdaptiveRoundOutputRemainsLocalAtReviewAndDelivery(string stage)
    {
        var item = await Seed(stage);
        var artifact = await Artifact(item, kind: "processedFiles");
        (await _db.ArtifactOperationBindings.SingleAsync()).Slot = "p:adaptive:output:" + artifact.Id;
        var target = Processed("p", artifact, "FILE_DELIVERY_RESULT_UNCERTAIN");
        if (stage == Assess) target = target with
            {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "FILE_DELIVERY_RESULT_UNCERTAIN")};
        SetTargets(item, target); Prompt(item, reason: "FILE_DELIVERY_RESULT_UNCERTAIN", review: stage == Assess ? artifact : null);
        var facts = await Facts(item);
        Assert.IsTrue(facts.Matches(["downloadedPending"]));
        Assert.IsFalse(facts.Matches(["spaceInsufficient"]));
        Assert.AreEqual("/unreachable/fact-test", artifact.RootPath);
    }

    [DataTestMethod]
    [DataRow("input-slot")]
    [DataRow("other-artifact-slot")]
    [DataRow("other-part")]
    [DataRow("other-run")]
    [DataRow("other-task")]
    [DataRow("wrong-binding-kind")]
    [DataRow("old-revision")]
    [DataRow("wrong-operation-kind")]
    [DataRow("wrong-operation-id")]
    [DataRow("incomplete-operation")]
    [DataRow("stale-fingerprint")]
    [DataRow("pending-resume")]
    [DataRow("deleted-task")]
    [DataRow("completed-task")]
    [DataRow("current-files-not-processing-output")]
    public async Task AdaptiveOutputFactsDoNotRelaxCurrentOwnershipAndSuccessRequirements(string variant)
    {
        var item = await Seed(Assess);
        var artifact = await Artifact(item, kind: variant == "current-files-not-processing-output"
            ? AcquisitionCurrentDirectoryFiles.Kind : "processedFiles");
        var binding = await _db.ArtifactOperationBindings.SingleAsync();
        binding.Slot = "p:adaptive:output:" + artifact.Id;
        var target = Processed("p", artifact) with
            {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")};
        switch (variant)
        {
            case "input-slot": binding.Slot = "p:adaptive:input:" + artifact.Id; break;
            case "other-artifact-slot": binding.Slot = "p:adaptive:output:another-artifact"; break;
            case "other-part": binding.Slot = "q:adaptive:output:" + artifact.Id; break;
            case "other-run": binding.OwnerId = "99999"; break;
            case "other-task": (await _db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = "99999"; break;
            case "wrong-binding-kind": binding.OwnerKind = "another-module"; break;
            case "old-revision": (await _db.Set<WorkflowRunSourceDbModel>().SingleAsync()).Revision = "0"; break;
            case "wrong-operation-kind": (await _db.ArtifactOperations.SingleAsync()).Kind = "files.captureCurrentDirectory"; break;
            case "wrong-operation-id": target = target with {ProcessingOperationId = "different-operation"}; break;
            case "incomplete-operation": (await _db.ArtifactOperations.SingleAsync()).State = ArtifactOperationState.Running; break;
            case "stale-fingerprint": target = target with {DeliveryReview = target.DeliveryReview! with {Fingerprint = new string('b', 64)}}; break;
            case "pending-resume": item.Run.PendingSignalJson = "{}"; break;
            case "deleted-task": item.Task.DeletedAt = DateTime.UtcNow; break;
            case "completed-task": item.Task.Status = AcquisitionStatus.Completed; break;
        }
        SetTargets(item, target); Prompt(item, review: artifact);
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]), variant);
    }

    [TestMethod]
    public async Task AdaptiveOutputCannotMakeAnUndownloadedSiblingLocal()
    {
        var item = await Seed(Assess, "a", "b");
        var artifact = await Artifact(item, "a", "processedFiles");
        (await _db.ArtifactOperationBindings.SingleAsync()).Slot = "a:adaptive:output:" + artifact.Id;
        SetNodes(item, Node("current", Assess, "a"), Node("next", Process, "b"), Node("publish", Publish));
        SetTargets(item, Processed("a", artifact) with
            {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")}, new() {Id = "b"});
        Prompt(item, "a", review: artifact);
        var facts = await Facts(item);
        Assert.IsFalse(facts.Matches(["downloadedPending"]));
        Assert.IsFalse(facts.Matches(Both));
    }

    [TestMethod]
    public async Task ValidLargeNestedManifestIsReducedWithoutLosingLocalProcessingFact()
    {
        var item = await Seed(Assess);
        var artifact = await Artifact(item, kind: "processedFiles");
        (await _db.ArtifactOperationBindings.SingleAsync()).Slot = "p:adaptive:output:" + artifact.Id;
        var entries = Enumerable.Range(0, 4104).Select(i => new ArtifactManifestEntry(
            "outer/" + new string('包', 40) + "/game/" + i.ToString("D5") + ".bin", i + 1, new string('a', 64))).ToArray();
        artifact.Fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        artifact.ManifestJson = Serialize(new ArtifactManifest(entries, artifact.Fingerprint));
        Assert.IsTrue(artifact.ManifestJson.Length > 1048576);
        Assert.IsTrue(artifact.ManifestJson.Length < 4194304);
        SetTargets(item, Processed("p", artifact) with
            {DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")});
        Prompt(item, review: artifact);
        Assert.IsTrue((await Facts(item)).Matches(Both));
    }

    [TestMethod]
    public async Task OversizedOrTooManyManifestEntriesRemainUnknown()
    {
        var item = await Seed(Publish); var artifact = await Artifact(item, kind: "processedFiles");
        SetTargets(item, Processed("p", artifact)); Prompt(item);
        foreach (var entries in new[]
                 {
                     Enumerable.Range(0, 6000).Select(i => new ArtifactManifestEntry(new string('a', 800) + "/" + i,
                         1, new string('a', 64))).ToArray(),
                     Enumerable.Range(0, 10001).Select(i => new ArtifactManifestEntry("file/" + i,
                         1, new string('a', 64))).ToArray()
                 })
        {
            artifact.Fingerprint = ArtifactManifestFiles.Fingerprint(entries);
            artifact.ManifestJson = Serialize(new ArtifactManifest(entries, artifact.Fingerprint));
            Assert.IsTrue(artifact.ManifestJson.Length > 4194304 || entries.Length > 10000);
            Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
    }
}
