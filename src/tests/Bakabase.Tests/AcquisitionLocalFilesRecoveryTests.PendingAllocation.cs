using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    [DataRow("working")]
    [DataRow("delivery-root")]
    [DataRow("concrete-source")]
    [DataRow("concrete-destination")]
    [DataRow("exact-allocation")]
    [DataRow("changed-working")]
    [DataRow("changed-checkpoint")]
    [DataRow("changed-download")]
    [DataRow("missing-download-evidence")]
    [DataRow("changed-source")]
    [DataRow("foreign-run")]
    [DataRow("unknown-field")]
    [DataRow("cleanup")]
    [DataRow("working", true)]
    [DataRow("delivery-root", true)]
    [DataRow("concrete-source", true)]
    [DataRow("concrete-destination", true)]
    [DataRow("unknown-field", true)]
    [DataRow("changed-checkpoint", true)]
    public async Task A_sibling_stage_only_treats_verified_pending_adoption_capacity_as_allocation(string variant, bool publication = false)
    {
        var f = await LocalFilesFixture();
        var previous = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var root = _root;
        _root = Directory.CreateDirectory(Path.Combine(root, "other-task")).FullName;
        var other = await LocalFilesFixture();
        _root = root;
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var prepared = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        Assert.AreEqual("prepared", prepared.LocalFiles!.Single().Status);

        // Restore the exact transitional state between recording the downloads and committing the new revision.
        // The verified downloads, bindings, original workflow checkpoint and byte evidence all remain durable.
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.Id == f.TaskId);
        var receipt = AcquisitionLocalFilesRecoveryReceipt.Read(task)! with {ResultingRevision = null};
        var part = receipt.Preview.Parts.Single();
        var sourceRoot = variant switch
        {
            "delivery-root" => Path.Combine(receipt.Storage.Delivery.RootDirectory, "unrelated"),
            "concrete-source" => part.SourceDirectory,
            "concrete-destination" => part.DeliveryDirectory,
            "exact-allocation" => part.WorkingDirectory,
            _ => Path.Combine(part.WorkingDirectory, "unrelated")
        };
        var builder = JsonNode.Parse(previous.BuilderStateJson!)!.AsObject();
        var raw = JsonSerializer.SerializeToNode(receipt, WorkflowJson.Options)!;
        var member = raw["preview"]!["parts"]![0]!;
        if (variant == "changed-working") member["workingDirectory"] = Path.GetDirectoryName(part.WorkingDirectory);
        if (variant == "changed-checkpoint") raw["checkpoint"] = new string('A', 64);
        if (variant == "changed-download") raw["parts"]![0]!["downloadEvidence"] = new string('B', 64);
        if (variant == "missing-download-evidence") raw["parts"]![0]!["downloadEvidence"] = null;
        if (variant == "changed-source") member["sourceDirectory"] = Path.Combine(root, "other-source");
        if (variant == "foreign-run") raw["previousRunId"] = other.RunId;
        if (variant == "unknown-field") member["unrecognized"] = part.WorkingDirectory;
        builder["localFilesRecovery"] = raw;
        task.BuilderStateJson = builder.ToJsonString(WorkflowJson.Options);
        task.WorkflowRunId = previous.WorkflowRunId; task.PlanRevision = previous.PlanRevision;
        task.PlanJson = previous.PlanJson; task.PlanHistoryJson = previous.PlanHistoryJson;
        task.Status = previous.Status; task.Phase = previous.Phase;
        if (publication)
        {
            var owner = await _db.AcquisitionTasks.SingleAsync(t => t.Id == other.TaskId);
            owner.DeletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();

        ArtifactManifestEntry[] entries = [new("product", 1, new string('a', 64))];
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        var source = new ArtifactRecord("stage-result", "op", publication ? "publishedFiles" : "processedFiles", sourceRoot,
            [Path.Combine(sourceRoot, "product")], new(entries, fingerprint), fingerprint,
            ArtifactLocationState.Available, true, [], null);
        var reasons = new List<string>();
        var guard = new ArtifactCleanupReferenceGuard(_db, referenceFound: reasons.Add);
        var blocked = await guard.HasReferencesAsync(source, [other.RunId], default,
            directoryOwnerTaskId: other.TaskId, directoryOwnerRunId: variant == "cleanup" || publication ? null : other.RunId,
            privatePaths: publication ? source.Files : null, deletedDirectoryOwner: publication, publicationStageFiles: publication);
        Assert.AreEqual(variant is not ("working" or "delivery-root"), blocked, string.Join(", ", reasons));
    }
}
