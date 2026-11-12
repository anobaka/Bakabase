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
