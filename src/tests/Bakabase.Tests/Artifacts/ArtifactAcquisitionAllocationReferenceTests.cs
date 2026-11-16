using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactAcquisitionAllocationReferenceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "allocation-only"));
    private static readonly string Fingerprint = new('a', 64);
    private static readonly HashSet<(int TaskId, int RunId, int Revision)> Owners = [(7, 11, 1), (7, 12, 2), (7, 13, 3)];
    private static AcquisitionPlan Plan(int previousRun = 11) => new() {Title = "Fixture", OutputDirectory = Path.Combine(Root, "outputs"),
        StoragePolicy = new() {ProcessingWorkspaceDirectory = Root}, Parts = [new() {Id = "part",
            ContinueFrom = new(7, previousRun, "artifact", "operation", Fingerprint)}]};
    private static List<AcquisitionPlanRevision> History(AcquisitionPlan adopted) => [
        new(1, Plan() with {Parts = [new() {Id = "part"}]}, [], DateTime.UtcNow.AddHours(-2)),
        new(2, adopted, [], DateTime.UtcNow.AddHours(-1), "adopt", 11),
        new(3, Plan(12), [], DateTime.UtcNow, "later", 12)];

    [TestMethod]
    [DataRow("valid")]
    [DataRow("unknown-field")]
    [DataRow("unbound")]
    [DataRow("wrong-history-owner")]
    [DataRow("wrong-policy")]
    [DataRow("duplicate-history")]
    public void Historical_queue_allocation_requires_the_original_owned_revision_and_policy(string variant)
    {
        var history = History(Plan());
        if (variant == "wrong-history-owner") history[2] = history[2] with {PreviousRunId = 99};
        if (variant == "wrong-policy") history[1] = history[1] with {Plan = Plan() with {StoragePolicy = new() {ProcessingWorkspaceDirectory = Path.Combine(Root, "other")}}};
        if (variant == "duplicate-history") history.Add(history[1]);
        var queue = JsonSerializer.SerializeToNode(new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"),
            Version = Fingerprint, StorageStamp = Fingerprint, Signal = "{}", RecoveryScope = "workflow", PlanRevision = 2,
            WorkflowRunId = 12, WorkingDirectories = [Root], DeliveryDirectories = [Root], Status = "cancelled"}, WorkflowJson.Options)!;
        if (variant == "unknown-field") queue["unknown"] = Root;
        using var document = JsonDocument.Parse(new JsonObject { ["localRecoveryQueueHistory"] = new JsonArray(queue) }.ToJsonString());
        var context = new ArtifactAcquisitionAllocationReferences.TaskContext(7, 13, 3,
            JsonSerializer.Serialize(Plan(12), WorkflowJson.Options), JsonSerializer.Serialize(history, WorkflowJson.Options));
        var values = ArtifactAcquisitionAllocationReferences.QueueValues(document.RootElement, context,
            variant == "unbound" ? new HashSet<(int, int, int)>() : Owners, default);
        Assert.AreEqual(variant == "valid" ? 1 : 0, values.Count);
        var entry = document.RootElement.GetProperty("localRecoveryQueueHistory")[0];
        Assert.IsFalse(values.Contains(entry.GetProperty("deliveryDirectories")[0]));
    }

    [TestMethod]
    [DataRow("current")]
    [DataRow("historical")]
    [DataRow("unknown-directory")]
    [DataRow("wrong-task")]
    [DataRow("borrowed-directory")]
    [DataRow("download")]
    public void Checkpoint_queue_keeps_concrete_paths_protected_beside_the_policy_root(string variant)
    {
        var stage = Path.Combine(Root, "task-seven");
        var plan = Plan() with {StageDirectories = new() {TaskId = variant == "wrong-task" ? 8 : 7,
            Parts = new() {["part"] = new() {Processing = variant == "borrowed-directory"
                ? new(stage, "borrowed", "userDirectoryRevision")
                : new(stage, "exclusive", "taskAllocation") {BindingId = Guid.NewGuid().ToString("N"),
                    Allocation = new("acquisition", "7", "processing-part", Root, stage)}}}}};
        var directory = variant == "unknown-directory" ? Path.Combine(Root, "foreign") : stage;
        var value = new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"), Version = Fingerprint,
            StorageStamp = Fingerprint, Signal = "{}", RecoveryScope = "workflow", PlanRevision = 2, WorkflowRunId = 12,
            ActionCategory = variant == "download" ? "originalDownload" : "localProcessing",
            WorkingDirectories = [Root, directory], DeliveryDirectories = [Root], Status = "completed"};
        var historical = variant == "historical";
        var key = historical ? "localRecoveryQueueHistory" : "localRecoveryQueue";
        var queue = JsonSerializer.SerializeToNode(value, WorkflowJson.Options);
        using var document = JsonDocument.Parse(new JsonObject {[key] = historical ? new JsonArray(queue) : queue}.ToJsonString());
        var task = new ArtifactAcquisitionAllocationReferences.TaskContext(7, historical ? 13 : 12, historical ? 3 : 2,
            JsonSerializer.Serialize(historical ? Plan(12) : plan, WorkflowJson.Options), JsonSerializer.Serialize(History(plan), WorkflowJson.Options));
        var roles = ArtifactAcquisitionAllocationReferences.QueueValues(document.RootElement, task, Owners, default);
        var raw = historical ? document.RootElement.GetProperty(key)[0] : document.RootElement.GetProperty(key);
        Assert.AreEqual(variant is "current" or "historical", roles.Contains(raw.GetProperty("workingDirectories")[0]));
        Assert.IsFalse(roles.Contains(raw.GetProperty("workingDirectories")[1]));
        Assert.IsFalse(roles.Contains(raw.GetProperty("deliveryDirectories")[0]));
    }

    [TestMethod]
    [DataRow("ready")]
    [DataRow("superseded-before-start")]
    [DataRow("existing-run")]
    [DataRow("missing-owner")]
    [DataRow("superseded-after-run")]
    [DataRow("changed-save")]
    [DataRow("wrong-input")]
    [DataRow("wrong-source-root")]
    [DataRow("missing-evidence")]
    public void Unstarted_adoption_requires_frozen_download_evidence_and_keeps_concrete_paths(string variant)
    {
        var adopted = Plan();
        var history = History(adopted);
        var manifest = new ArtifactManifest([new("file.7z", 123, Fingerprint)], Fingerprint);
        var evidence = new AcquisitionPlanArtifactEvidence("part", variant == "wrong-input" ? "other" : "artifact",
            "operation", "download", variant == "wrong-source-root" ? Path.Combine(Root, "other") : Root, Fingerprint, manifest);
        history[1] = history[1] with {Reason = AcquisitionLocalFilesRecoveryReceipt.RevisionReason,
            ArtifactEvidence = variant == "missing-evidence" ? [] : [evidence]};
        var historical = variant.StartsWith("superseded-", StringComparison.Ordinal);
        if (!historical) history.RemoveAt(2);
        else if (variant == "superseded-before-start") history[2] = history[2] with {PreviousRunId = null};
        var active = variant == "changed-save" ? adopted with {OutputDirectory = Path.Combine(Root, "changed")} : history.Last().Plan;
        var context = new ArtifactAcquisitionAllocationReferences.TaskContext(7, historical ? 13 : variant == "existing-run" ? 12 : null,
            historical ? 3 : 2, JsonSerializer.Serialize(active, WorkflowJson.Options), JsonSerializer.Serialize(history, WorkflowJson.Options));
        var preview = new AcquisitionLocalFilesRecoveryPart("part", 21, 22, Fingerprint, 1, Root, Root, Root, Root, Root, "artifact");
        var receipt = new AcquisitionLocalFilesRecoveryReceipt(11, 1, Fingerprint,
            new(new() {ProcessingWorkspaceDirectory = Root}, new()), [new("part", 21, 22, "intent", Fingerprint, Fingerprint, "artifact", "operation")],
            new([preview]), 2);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new {localFilesRecovery = receipt}, WorkflowJson.Options));
        var owners = Owners.Where(o => o.Revision != 2 && (variant != "missing-owner" || o.RunId != 11)).ToHashSet();
        var values = ArtifactAcquisitionAllocationReferences.QueueValues(document.RootElement, context, owners, default);
        var part = document.RootElement.GetProperty("localFilesRecovery").GetProperty("preview").GetProperty("parts")[0];
        Assert.AreEqual(variant is "ready" or "superseded-before-start", values.Contains(part.GetProperty("workingDirectory")));
        Assert.IsFalse(values.Contains(part.GetProperty("sourceDirectory")));
        Assert.IsFalse(values.Contains(part.GetProperty("deliveryDirectory")));
    }

    [TestMethod]
    [DataRow("current")]
    [DataRow("historical")]
    [DataRow("unbound")]
    [DataRow("wrong-history-owner")]
    [DataRow("wrong-task-input")]
    [DataRow("wrong-fingerprint")]
    [DataRow("wrong-part")]
    [DataRow("wrong-policy")]
    [DataRow("unknown-field")]
    [DataRow("prepared-only")]
    [DataRow("legacy-allocation")]
    [DataRow("previous-concrete-destination")]
    [DataRow("different-original-parts")]
    [DataRow("delivery-allocation")]
    [DataRow("delivery-unknown-field")]
    public void Local_adoption_allocations_require_matching_receipt_inputs_policy_and_owned_revision(string variant)
    {
        var adopted = Plan();
        if (variant == "wrong-task-input") adopted = adopted with {Parts = [adopted.Parts[0] with {ContinueFrom = adopted.Parts[0].ContinueFrom! with {TaskId = 8}}]};
        if (variant == "wrong-policy") adopted = adopted with {StoragePolicy = new() {ProcessingWorkspaceDirectory = Path.Combine(Root, "other")}};
        var history = History(adopted);
        if (variant == "wrong-history-owner") history[1] = history[1] with {PreviousRunId = 99};
        var preview = new AcquisitionLocalFilesRecoveryPart("part", 21, 22, Fingerprint, 1, Root, Root, Root, Root, Root, "artifact");
        if (variant is "legacy-allocation" or "previous-concrete-destination" or "different-original-parts")
            preview = preview with {OriginalDeliveryDirectory = history[0].Plan.OutputDirectory};
        if (variant == "previous-concrete-destination")
            history[0] = history[0] with {Plan = history[0].Plan with {DeliveryLayout = new() {TaskId = 7,
                PartDirectories = new() {["part"] = history[0].Plan.OutputDirectory}}}};
        if (variant == "different-original-parts")
            history[0] = history[0] with {Plan = history[0].Plan with {Parts = [new() {Id = "other"}]}};
        if (variant == "wrong-fingerprint") preview = preview with {Fingerprint = new('b', 64)};
        if (variant == "wrong-part") preview = preview with {PartId = "other"};
        var receipt = new AcquisitionLocalFilesRecoveryReceipt(11, 1, Fingerprint,
            new(new() {ProcessingWorkspaceDirectory = Root}, new() {RootDirectory = variant.StartsWith("delivery-", StringComparison.Ordinal) ? Root : ""}),
            [new("part", 21, 22, "intent", Fingerprint, Fingerprint, "artifact", "operation")], new([preview]),
            variant == "prepared-only" ? null : 2);
        var node = JsonSerializer.SerializeToNode(receipt, WorkflowJson.Options)!;
        if (variant == "unknown-field") node["preview"]!["parts"]![0]!["unknown"] = Root;
        if (variant == "delivery-unknown-field") node["storage"]!["delivery"]!["unknown"] = Root;
        using var document = JsonDocument.Parse(new JsonObject { ["localFilesRecovery"] = node }.ToJsonString());
        var historical = variant == "historical";
        if (!historical) history.RemoveAt(2);
        var context = new ArtifactAcquisitionAllocationReferences.TaskContext(7, historical ? 13 : 12, historical ? 3 : 2,
            JsonSerializer.Serialize(historical ? Plan(12) : adopted, WorkflowJson.Options), JsonSerializer.Serialize(history, WorkflowJson.Options));
        var values = ArtifactAcquisitionAllocationReferences.QueueValues(document.RootElement, context,
            variant == "unbound" ? new HashSet<(int, int, int)>() : Owners, default);
        var valid = variant is "current" or "historical" or "legacy-allocation" or "previous-concrete-destination" or "different-original-parts" or "delivery-allocation";
        Assert.AreEqual(variant is "legacy-allocation" or "delivery-allocation" ? 2 : valid ? 1 : 0, values.Count);
        if (variant == "delivery-allocation") Assert.IsTrue(values.Contains(document.RootElement.GetProperty("localFilesRecovery")
            .GetProperty("storage").GetProperty("delivery").GetProperty("rootDirectory")));
        var part = document.RootElement.GetProperty("localFilesRecovery").GetProperty("preview").GetProperty("parts")[0];
        Assert.AreEqual(variant == "legacy-allocation", values.Contains(part.GetProperty("originalDeliveryDirectory")));
        foreach (var concrete in new[] {"sourceDirectory", "originalWorkingDirectory", "deliveryDirectory"})
            Assert.IsFalse(values.Contains(part.GetProperty(concrete)), concrete);
        if (valid)
        {
            Assert.IsTrue(values.Contains(part.GetProperty("workingDirectory")));
            var child = new ArtifactCleanupPathReferences([Path.Combine(Root, "candidate")], [], default);
            Assert.IsFalse(child.Refers(Root, ancestors: false));
            Assert.IsTrue(child.Refers(Root), "Actual inputs at the same value retain ancestor protection.");
            Assert.IsTrue(new ArtifactCleanupPathReferences([Root], [], default).Refers(Root, ancestors: false));
            Assert.IsTrue(new ArtifactCleanupPathReferences([Root], [], default).Refers(Path.Combine(Root, "child"), ancestors: false));
        }
    }
}
