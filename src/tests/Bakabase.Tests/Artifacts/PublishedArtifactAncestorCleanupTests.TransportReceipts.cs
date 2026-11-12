using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("ownContainer", false)]
    [DataRow("stepCheckpoint", false)]
    [DataRow("missingCheckpoint", true)]
    [DataRow("multiStep", false)]
    [DataRow("foreignIntermediate", true)]
    [DataRow("changedLink", true)]
    [DataRow("differentCheckpointPlan", true)]
    [DataRow("changedReceipt", true)]
    [DataRow("siblingDownload", true)]
    [DataRow("anotherOwner", true)]
    [DataRow("pendingTransport", true)]
    [DataRow("unknownOptionsReferToWorkspace", true)]
    [DataRow("nestedContainer", false)]
    [DataRow("nestedTypedOptions", false)]
    [DataRow("nestedRetired", false)]
    [DataRow("nestedRetiredWrongRoot", true)]
    [DataRow("nestedRetiredWrongManifest", true)]
    [DataRow("nestedRetiredWrongArtifact", true)]
    [DataRow("nestedRetiredUnfinished", true)]
    [DataRow("nestedRetiredMissingReceipt", true)]
    [DataRow("nestedWrongArtifactRoot", true)]
    [DataRow("nestedSiblingDownloadRoot", true)]
    [DataRow("nestedUnknownOptions", true)]
    [DataRow("nestedExactWorkspaceOption", true)]
    [DataRow("nestedExactResultFile", true)]
    public async Task OnlyProvenCompletedInputContainerCanReleaseItsNestedPrivateWorkspace(string scenario, bool blocked)
    {
        var raw = await Artifact("download");
        var metadata = JsonSerializer.Serialize(new DownloadArtifactMetadata(99, null), Json);
        await db.Artifacts.Where(a => a.Id == raw.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, metadata));
        var input = raw;
        if (scenario is "multiStep" or "foreignIntermediate" or "changedLink")
        {
            var middle = Directory.CreateDirectory(Path.Combine(root, "prepared")).FullName;
            var renamed = Path.Combine(middle, "renamed.rar");
            await File.WriteAllTextAsync(renamed, "renamed");
            var predecessor = scenario == "changedLink" ? "unrelated" : raw.Id;
            input = await store.AdoptAsync(new("middle", "middle", "files.process", raw.Fingerprint),
                new(middle, [renamed], "processedFiles", true, [raw.Id],
                    JsonSerializer.Serialize(new FileArtifactProcessingMetadata(predecessor, "{}", null), Json)), "fixture");
            await new ArtifactOperationBindingService(db).BindAsync("workflow", scenario == "foreignIntermediate" ? "2" : "1", "middle", input.OperationId);
        }
        var resolver = new FileProcessingWorkspaceResolver(root);
        var key = ArtifactManifestFiles.Hash("private extraction");
        var boundary = scenario is "stepCheckpoint" or "missingCheckpoint" or "differentCheckpointPlan" or "multiStep" or "foreignIntermediate" or "changedLink";
        var savedPlan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};
        var checkpoint = FileArtifactProcessor.CheckpointKey(input, savedPlan);
        var dir = await resolver.ResolveAsync(boundary ? "checkpoints" : "operations", boundary ? checkpoint : key, 1,
            raw.RootDirectory, raw.RootDirectory, default);
        if (scenario == "differentCheckpointPlan") savedPlan = savedPlan with {Steps = [new() {Id = "extract", Op = "rename", Extension = "7z"}]};
        var processingMetadata = boundary ? JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id, JsonSerializer.Serialize(savedPlan, Json),
            null, CompletedThroughStepId: "extract"), Json) : null;
        var result = Directory.CreateDirectory(Path.Combine(dir, "plan", "result")).FullName;
        var file = Path.Combine(result, "extracted.bin");
        await File.WriteAllTextAsync(file, "kept until authorized");
        var processed = await store.AdoptAsync(new(key, key, "files.process", input.Fingerprint),
            new(result, [file], "processedFiles", true, [input.Id], processingMetadata), "fixture");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:process", processed.OperationId);
        var nested = scenario.StartsWith("nested", StringComparison.Ordinal);
        var downloadDirectory = nested ? root : raw.RootDirectory;
        if (scenario == "nestedSiblingDownloadRoot") downloadDirectory = Path.Combine(root, "unrelated-download");
        string? options = scenario is "unknownOptionsReferToWorkspace" or "nestedUnknownOptions"
            ? JsonSerializer.Serialize(new {manuallySelected = result}) : null;
        if (scenario is "nestedTypedOptions" or "nestedExactWorkspaceOption")
            options = JsonSerializer.Serialize(new CloudDownloadRequest("dispatch",
                new() {Id = "share", ProviderId = "baidu", Url = "https://example.test/share"}, null, null, downloadDirectory)
                {WorkspaceDirectory = scenario == "nestedExactWorkspaceOption" ? file : raw.RootDirectory}, Json);
        db.DownloadTasks.Add(new() {Id = 99, ThirdPartyId = CloudSites.Site("baidu"), DownloadPath = downloadDirectory,
            Status = DownloadTaskDbModelStatus.Complete, Options = options});
        db.DownloadResults.Add(new() {DownloadTaskId = 99, Path = raw.RootDirectory,
            FilesJson = JsonSerializer.Serialize(scenario == "changedReceipt" ? new[] {file} : raw.Files)});
        db.CloudDownloadOperations.Add(new() {Id = "dispatch", Kind = "download", WorkflowRunId = scenario == "anotherOwner" ? 2 : 1,
            DownloadTaskId = 99, State = scenario == "pendingTransport" ? "queued" : "started"});
        if (scenario == "siblingDownload") db.DownloadTasks.Add(new() {Id = 100, DownloadPath = result});
        await db.SaveChangesAsync();
        if (scenario.StartsWith("nestedRetired", StringComparison.Ordinal))
        {
            var publication = await Artifact("publishedFiles", raw);
            var retired = await new ArchiveQuarantineService(store, root, new Storage())
                .DeleteAfterDeliveryAsync(raw.Id, [publication.Id]);
            Assert.IsTrue(retired.Completed, retired.Message);
            var receiptPath = Path.Combine(root, "file-processing", "quarantine-receipts", retired.ReceiptId + ".json");
            if (scenario == "nestedRetiredMissingReceipt") File.Delete(receiptPath);
            if (scenario is "nestedRetiredWrongRoot" or "nestedRetiredWrongManifest" or "nestedRetiredWrongArtifact" or "nestedRetiredUnfinished")
            {
                var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(receiptPath), Json)!;
                if (scenario == "nestedRetiredWrongRoot") receipt.OriginalRoot = "absolute:" + Path.Combine(root, "different-root");
                else if (scenario == "nestedRetiredWrongManifest") receipt.Manifest = receipt.Manifest with {Fingerprint = new string('a', 64)};
                else if (scenario == "nestedRetiredWrongArtifact") receipt.ArtifactId = "unrelated";
                else receipt.Status = "Deleting";
                await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, Json));
            }
        }
        if (scenario == "nestedWrongArtifactRoot")
            await db.Artifacts.Where(a => a.Id == raw.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RootPath, "different-root"));
        if (scenario == "nestedExactResultFile")
        {
            db.DownloadTaskFiles.Add(new() {DownloadTaskId = 99, Path = file});
            await db.SaveChangesAsync();
        }
        if (scenario == "missingCheckpoint") Directory.Delete(dir, true);
        var guard = new ArtifactCleanupReferenceGuard(db, resolver);
        Assert.AreEqual(blocked, await guard.HasReferencesAsync(processed, [1], default));
        Assert.AreEqual(blocked, await guard.HasReferencesAsync(processed, [1], default, [file]));
        Assert.AreEqual(scenario != "missingCheckpoint", File.Exists(file));
        Assert.AreEqual(!scenario.StartsWith("nestedRetired", StringComparison.Ordinal), File.Exists(raw.Files.Single()));
    }

    [DataTestMethod]
    [DataRow("journal_planned")]
    [DataRow("journal_queued")]
    [DataRow("journal_starting")]
    [DataRow("journal_unknown")]
    [DataRow("journal_failed")]
    [DataRow("native_in_progress")]
    [DataRow("native_failed")]
    [DataRow("receipt_missing")]
    [DataRow("receipt_partial")]
    [DataRow("receipt_extra")]
    [DataRow("task_file_extra")]
    [DataRow("wrong_owner")]
    [DataRow("wrong_kind")]
    [DataRow("journal_missing")]
    public async Task Started_dispatch_alone_cannot_exempt_incomplete_or_shared_download_references(string defect)
    {
        var raw = await Artifact("download"); var processed = await Artifact("processedFiles", raw, sourceCleanup: "delete");
        var publication = await Artifact("publishedFiles", processed);
        var extra = Path.Combine(raw.RootDirectory, "unrelated.key");
        var metadata = JsonSerializer.Serialize(new DownloadArtifactMetadata(99, null), Json);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == raw.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, metadata));
        var status = defect switch
        {
            "native_in_progress" => DownloadTaskDbModelStatus.InProgress,
            "native_failed" => DownloadTaskDbModelStatus.Failed,
            _ => DownloadTaskDbModelStatus.Complete
        };
        db.DownloadTasks.Add(new() {Id = 99, DownloadPath = raw.RootDirectory, Status = status});
        db.DownloadTaskFiles.Add(new() {DownloadTaskId = 99, Path = raw.Files[0], Size = raw.Manifest.Entries[0].Length});
        if (defect == "task_file_extra") db.DownloadTaskFiles.Add(new() {DownloadTaskId = 99, Path = extra, Size = 1});
        if (defect != "receipt_missing")
        {
            var files = defect == "receipt_partial" ? Array.Empty<string>() :
                defect == "receipt_extra" ? raw.Files.Append(extra).ToArray() : raw.Files.ToArray();
            db.DownloadResults.Add(new() {Id = 99, DownloadTaskId = 99, Path = raw.RootDirectory, FilesJson = JsonSerializer.Serialize(files)});
        }
        if (defect != "journal_missing")
            db.CloudDownloadOperations.Add(new CloudOperationDbModel {Id = "own-dispatch",
                Kind = defect == "wrong_kind" ? "transfer" : "download", WorkflowRunId = defect == "wrong_owner" ? 2 : 1,
                DownloadTaskId = 99, State = defect.StartsWith("journal_", StringComparison.Ordinal) ? defect[8..] : "started"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var preview = (await Preview(publication)).Single(a => a.ArtifactId == raw.Id);
        Assert.IsFalse(preview.Eligible, defect); Assert.AreEqual("ReferencedPath", preview.Reason, defect);
        Assert.IsTrue(File.Exists(raw.Files[0])); Assert.IsTrue(File.Exists(publication.Files[0]));
        Assert.AreEqual(status, (await db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }
}
