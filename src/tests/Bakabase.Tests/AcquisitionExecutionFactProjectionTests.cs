using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class AcquisitionExecutionFactProjectionTests
{
    private SqliteConnection _connection = null!;
    private BakabaseDbContext _db = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Process = AcquisitionPlanProcessingActivity.ActivityKind;
    private const string Assess = AssessFileArtifactDeliveryActivity.ActivityKind;
    private const string Publish = "fs.publishArtifacts";
    private static readonly string[] Both = ["downloadedPending", "spaceInsufficient"];

    [TestInitialize] public async Task Setup()
    {
        _connection = new("Data Source=:memory:"); await _connection.OpenAsync();
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }
    [TestCleanup] public void Cleanup() {_db.Dispose(); _connection.Dispose();}

    private sealed record Case(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, WorkflowNodeExecutionDbModel Node);
    private async Task<Case> Seed(string kind = Process, params string[] parts)
    {
        if (parts.Length == 0) parts = ["p"];
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 0,
            WaitReason = kind == Publish ? "artifacts.publication" : "cloud.fileProcessing", StartedAt = DateTime.UtcNow};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Recorded local files", Status = AcquisitionStatus.Waiting,
            WorkflowRunId = run.Id, PlanRevision = 1,
            PlanJson = Serialize(new {parts = parts.Select(id => new {id}).ToArray()})};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        var node = new WorkflowNodeExecutionDbModel {WorkflowRunId = run.Id, NodeId = "current", Kind = kind,
            StepIndex = 0, Attempt = 1, Status = "waiting", WaitReason = run.WaitReason, StartedAt = DateTime.UtcNow};
        _db.WorkflowNodeExecutions.Add(node);
        var item = new Case(task, run, node);
        SetNodes(item, Node("current", kind, parts[0]));
        SetTargets(item, parts.Select(id => new CloudDownloadTarget {Id = id}).ToArray());
        await _db.SaveChangesAsync(); return item;
    }
    private static object Node(string id, string kind, string? targetId = null, object? config = null) => new
        {nodeId = id, kind, configJson = Serialize(config ?? new {targetId, source = "processed"})};
    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private static void SetNodes(Case item, params object[] nodes) => item.Run.PlanSnapshotJson = Serialize(new {triggerKind = CloudWorkflow.Trigger, nodes});
    private static void SetTargets(Case item, params CloudDownloadTarget[] targets) => item.Run.CurrentItemJson = Serialize(new
        {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name, item = new DownloadWorkset {Targets = targets.ToList()}});
    private async Task<ArtifactDbModel> Artifact(Case item, string part = "p", string kind = "download", string? slot = null)
    {
        var id = Guid.NewGuid().ToString("N");
        // No file is created: lists must classify recorded evidence without touching storage.
        var entries = new[] {new ArtifactManifestEntry("subtitle.srt", 12000, new string('a', 64))};
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        var artifact = new ArtifactDbModel {Id = id, OperationId = id, Kind = kind, RootPath = "/unreachable/fact-test",
            Fingerprint = fingerprint, State = ArtifactLocationState.Available,
            ManifestJson = Serialize(new ArtifactManifest(entries, fingerprint))};
        _db.Artifacts.Add(artifact);
        _db.ArtifactOperations.Add(new() {Id = id, State = ArtifactOperationState.Completed, OutputArtifactId = id,
            Kind = kind switch {"download" => "download", "publishedFiles" => "files.publish",
                AcquisitionCurrentDirectoryFiles.Kind => AcquisitionCurrentDirectoryFiles.OperationKind, _ => "files.process"}, InputFingerprint = "bound-input"});
        _db.ArtifactOperationBindings.Add(new() {Id = id, OwnerKind = "workflow", OwnerId = item.Run.Id.ToString(),
            Slot = slot ?? part + (kind == "download" ? ":artifact.download" : kind == "publishedFiles" ? ":artifact.publish" : ":process:step"), OperationId = id});
        await _db.SaveChangesAsync(); return artifact;
    }
    private static CloudDownloadTarget Download(string part, ArtifactDbModel artifact, string reason = "DiskSpaceLimit") => new()
        {Id = part, DownloadArtifactId = artifact.Id, DownloadOperationId = artifact.OperationId, ProcessingWaitReason = reason};
    private static CloudDownloadTarget Processed(string part, ArtifactDbModel artifact, string reason = "DiskSpaceLimit") => new()
        {Id = part, ProcessedArtifactId = artifact.Id, ProcessingOperationId = artifact.OperationId, PublicationWaitReason = reason};
    private static void Prompt(Case item, string part = "p", string reason = "DiskSpaceLimit", ArtifactDbModel? review = null)
        => item.Run.WaitPromptJson = Serialize(new {targetId = part, reason, artifactId = review?.Id, fingerprint = review?.Fingerprint, deliveryReview = review != null});
    private async Task<AcquisitionExecutionFacts> Facts(Case item)
    {
        await _db.SaveChangesAsync();
        return (await new AcquisitionExecutionFactProjection(_db).ReadAsync([item.Task.Id], default))[item.Task.Id];
    }

    [TestMethod] public async Task CurrentBoundDownloadAndExactStorageWaitMatchWithoutFilesystemReads()
    {
        var item = await Seed(); var artifact = await Artifact(item);
        SetTargets(item, Download("p", artifact)); Prompt(item);
        Assert.IsTrue((await Facts(item)).Matches(Both));
        Assert.AreEqual("/unreachable/fact-test", artifact.RootPath);
        Assert.AreEqual(WorkflowRunStatus.Waiting, item.Run.Status);
    }

    [TestMethod] public async Task CurrentProcessedInputPublishesAfterOriginalDownloadRetired()
    {
        var item = await Seed(Publish); var original = await Artifact(item); original.State = ArtifactLocationState.Retired;
        var processed = await Artifact(item, kind: "processedFiles");
        SetTargets(item, Processed("p", processed) with {DownloadArtifactId = original.Id, DownloadOperationId = original.OperationId}); Prompt(item);
        Assert.IsTrue((await Facts(item)).Matches(Both));
    }

    [TestMethod] public async Task ReopenedPublishedArtifactRemainsPendingUntilNestedReviewAccepted()
    {
        var item = await Seed(Assess); var artifact = await Artifact(item, kind: "publishedFiles");
        _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow", OwnerId = item.Run.Id.ToString(),
            Slot = "p:process:delivery-input:" + artifact.Id, OperationId = artifact.OperationId});
        var target = Processed("p", artifact) with {PublishedArtifactId = artifact.Id, PublicationOperationId = artifact.OperationId,
            PublishedFiles = ["/unreachable/fact-test/subtitle.srt"], PublicationStatus = "reused",
            DeliveryReview = new(artifact.Id, artifact.Fingerprint, WaitReason: "DiskSpaceLimit")};
        SetTargets(item, target); Prompt(item, review: artifact);
        Assert.IsTrue((await Facts(item)).Matches(Both));
        SetTargets(item, target with {DeliveryReview = target.DeliveryReview! with {Fingerprint = new string('b', 64)}});
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        SetTargets(item, target with {DeliveryReview = target.DeliveryReview! with {Accepted = true}});
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }

    [TestMethod] public async Task PartialPartsAndUntrustedPublicationOrCleanupMarkersDoNotClaimAllInputsReady()
    {
        var item = await Seed(Process, "a", "b"); var artifact = await Artifact(item, "a");
        SetNodes(item, Node("current", Process, "a"), Node("next", Process, "b"), Node("publish", Publish)); Prompt(item, "a");
        foreach (var missing in new[] {new CloudDownloadTarget {Id = "b"}, new() {Id = "b", PublishedArtifactId = "missing"},
                     new() {Id = "b", PublicationStatus = "reused"}, new() {Id = "b", CleanupStatus = "retained"}})
        {
            SetTargets(item, Download("a", artifact), missing);
            Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
            Assert.IsFalse((await Facts(item)).Matches(Both));
        }
        SetTargets(item, Download("a", artifact), new() {Id = "b", Excluded = true});
        Assert.IsTrue((await Facts(item)).Matches(Both));
    }

    [TestMethod] public async Task TaskLevelSpaceFailureCannotSupplyAnotherPartLocalProcessingConjunction()
    {
        var facts = new AcquisitionExecutionFacts(true, [new("a", "node", 1, true, false)]).WithTaskSpaceFailure();
        Assert.IsTrue(facts.Matches(["downloadedPending"])); Assert.IsTrue(facts.Matches(["spaceInsufficient"]));
        Assert.IsTrue(facts.Matches(Both));
        var crossPart = new AcquisitionExecutionFacts(true, [new("a", "node", 1, true, false), new("b", "node", 1, false, true)]);
        Assert.IsFalse(crossPart.Matches(Both));
    }

    [DataTestMethod]
    [DataRow(ArtifactLocationState.Retired)] [DataRow(ArtifactLocationState.Quarantined)]
    [DataRow(ArtifactLocationState.Missing)] [DataRow(ArtifactLocationState.Modified)]
    [DataRow(ArtifactLocationState.StorageUnavailable)]
    public async Task NonAvailableArtifactsCannotBeReusedAsDownloadedFacts(ArtifactLocationState state)
    {
        var item = await Seed(); var artifact = await Artifact(item); artifact.State = state;
        SetTargets(item, Download("p", artifact)); Prompt(item);
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }

    [TestMethod] public async Task OldRunBindingsAndMismatchedOperationKindsAreNotCurrentEvidence()
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact)); Prompt(item);
        var binding = await _db.ArtifactOperationBindings.SingleAsync(); binding.OwnerId = "99999";
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        binding.OwnerId = item.Run.Id.ToString();
        (await _db.ArtifactOperations.SingleAsync()).Kind = "files.publish";
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }

    [TestMethod] public async Task ExactCurrentContinuationCanBorrowVerifiedPublishedInput()
    {
        var item = await Seed(); var artifact = await Artifact(item, kind: "publishedFiles", slot: "p:artifact.continuation");
        SetNodes(item, Node("current", Process, "p", new {targetId = "p", continueFrom = new
            {artifactId = artifact.Id, operationId = artifact.OperationId, fingerprint = artifact.Fingerprint}}));
        SetTargets(item, new CloudDownloadTarget {Id = "p", ProcessingWaitReason = "DiskSpaceLimit"}); Prompt(item);
        Assert.IsTrue((await Facts(item)).Matches(Both));
        artifact.Fingerprint = new string('b', 64);
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }

    [TestMethod] public async Task NewAttemptOrPendingSignalSuppressesAnOldStoragePrompt()
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact)); Prompt(item);
        item.Run.PendingSignalJson = "{}"; Assert.IsFalse((await Facts(item)).Matches(Both)); item.Run.PendingSignalJson = null;
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = item.Run.Id, NodeId = "current", Kind = Process, StepIndex = 0,
            Attempt = 2, Status = "succeeded", StartedAt = DateTime.UtcNow});
        Assert.IsFalse((await Facts(item)).Matches(Both));
    }

    [DataTestMethod]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE")] [DataRow("LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED")] [DataRow("PIKPAK_STORAGE_QUOTA_EXCEEDED")]
    public async Task NonCapacityFailuresDoNotMatchSpaceFact(string reason)
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact, reason)); Prompt(item, reason: reason);
        var facts = await Facts(item); Assert.IsTrue(facts.Matches(["downloadedPending"])); Assert.IsFalse(facts.Matches(["spaceInsufficient"]));
    }

    [TestMethod] public async Task CleanupOnlyAndFutureNetworkNodesCannotBeLocalPendingProcessing()
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact)); Prompt(item);
        SetNodes(item, Node("current", Process, "p"), Node("future", "cloud.download"));
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        item.Node.Kind = "download.cleanupArtifacts";
        SetNodes(item, Node("current", item.Node.Kind));
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }

    [TestMethod] public async Task InvalidPrivatePayloadAndMalformedManifestsRemainUnknown()
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact)); Prompt(item);
        foreach (var manifest in new[] {"not JSON", "{\"entries\":\"bad\"}", "{\"entries\":[\"bad\"]}",
                     "{\"entries\":null}", new string(' ', 1048577) + "{}"})
        {
            artifact.ManifestJson = manifest; Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
        foreach (var payload in new[] {"not JSON", "null", "{\"type\":null,\"item\":{\"targets\":[null]}}"})
        {
            item.Run.CurrentItemJson = payload; Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
    }

    [TestMethod] public async Task UnsafeDuplicateOrMalformedManifestEntriesDoNotProduceReadyFacts()
    {
        var item = await Seed(); var artifact = await Artifact(item); SetTargets(item, Download("p", artifact)); Prompt(item);
        foreach (var entries in new ArtifactManifestEntry[][]
                 {
                     new[] {new ArtifactManifestEntry("../file", 1, new string('a', 64))},
                     [new("/file", 1, new string('a', 64))], [new("folder\\file", 1, new string('a', 64))],
                     [new("folder/../file", 1, new string('a', 64))], [new("file", 1, new string('z', 64))],
                     [new("file", 1, new string('a', 64)), new("file", 2, new string('b', 64))]
                 })
        {
            artifact.Fingerprint = ArtifactManifestFiles.Fingerprint(entries);
            artifact.ManifestJson = Serialize(new ArtifactManifest(entries, artifact.Fingerprint));
            Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
    }

    [TestMethod] public async Task NoRegisteredArtifactOrMissingCurrentOwnershipRemainsUnknown()
    {
        var item = await Seed(); SetTargets(item, new CloudDownloadTarget {Id = "p", DownloadTaskId = 123, Files = ["/any/file"], ProcessingWaitReason = "DiskSpaceLimit"}); Prompt(item);
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        var artifact = await Artifact(item); SetTargets(item, Download("p", artifact));
        (await _db.Set<WorkflowRunSourceDbModel>().SingleAsync()).Revision = "0";
        Assert.IsFalse((await Facts(item)).Matches(Both));
    }

    [TestMethod] public async Task NonObjectConfigAndInvalidBooleanCannotHideRequiredParts()
    {
        var item = await Seed(Publish); var artifact = await Artifact(item, kind: "processedFiles");
        SetTargets(item, Processed("p", artifact)); Prompt(item);
        foreach (var config in new[] {"[]", "42", "\"text\"", "null", "bad JSON"})
        {
            SetNodes(item, new {nodeId = "current", kind = Publish, configJson = config});
            Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
        }
        SetNodes(item, Node("current", Publish));
        item.Task.PlanJson = Serialize(new {parts = new[] {new {id = "p"}, new {id = "q"}}});
        item.Run.CurrentItemJson = Serialize(new {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name,
            item = new {targets = new object[] {Processed("p", artifact), new {id = "q", excluded = 1}}}});
        Assert.IsFalse((await Facts(item)).Matches(["downloadedPending"]));
    }
}
