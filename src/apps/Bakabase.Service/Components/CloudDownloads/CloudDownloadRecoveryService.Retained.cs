using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed partial class CloudDownloadRecoveryService
{
    private async Task<CloudDownloadRecoveryCheck> CheckRetainedAsync(int runId, CloudDownloadTarget target,
        string outputRoot, DownloadTaskDbModel download, ICloudRetainedDownloadRecoveryProvider? provider,
        string? failure, bool revalidate, CancellationToken ct, ICloudExistingDownloadVerificationProvider? verifier = null,
        bool includeStopped = false, bool requireRetainedFiles = false, NativeVerificationScope? nativeVerification = null)
    {
        CloudDownloadRecoveryCheck Block(string code, string? problem = null, bool candidate = false) =>
            new(false, code, problem ?? failure, candidate);
        var source = target.SelectedSource!;
        // Retained input is an opt-in continuation of one existing transfer/download,
        // never a source correction, adoption of completed content or a new generation.
        if (target.RemoteEntry == null || source.AccessCodeRevisions is {Count: > 0} ||
            target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null ||
            target.ProcessingOperationId != null || target.PublicationOperationId != null || target.CleanupReceiptId != null ||
            target.Files.Count != 0 || target.ProcessedFiles.Count != 0 || target.PublishedFiles.Count != 0 ||
            target.ProcessingStepArtifacts.Count != 0 || download.CompletedAt != null ||
            !string.IsNullOrEmpty(download.Checkpoint) || download.Progress < 0 || download.Progress > 100 ||
            await db.DownloadResults.AsNoTracking().AnyAsync(r => r.DownloadTaskId == download.Id, ct) ||
            await db.DownloadResultOwners.AsNoTracking().AnyAsync(r => r.DownloadTaskId == download.Id, ct))
            return Block("RECOVERY_DOWNLOAD_HAS_OUTPUT");
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, workspaces);
        if (target.DownloadOperationId != null && target.DownloadOperationId != intent)
            return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        var owner = runId.ToString();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == intent ||
            b.OwnerKind == "workflow" && b.OwnerId == owner).ToListAsync(ct);
        var downloadSlot = CloudOperationService.OperationSlot(target, "download");
        if (bindings.Count(b => b.OperationId == intent) != 1 ||
            !bindings.Any(b => b.OperationId == intent && b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot == downloadSlot))
            return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
        if (operation?.Kind != "download" || operation.DownloadTaskId != download.Id || operation.WorkflowRunId != runId ||
            operation.TargetId != target.Id || operation.State is not ("started" or "starting" or "queued") ||
            operation.ReceiptJson != null || await db.ArtifactOperations.AsNoTracking().AnyAsync(o => o.Id == intent, ct) ||
            await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.DownloadTaskId == download.Id && o.Id != intent, ct))
            return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        if (verifier != null && download.Status == DownloadTaskDbModelStatus.Disabled && operation.State != "started")
            return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web);
        var configured = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options ?? "", JsonSerializerOptions.Web);
        static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        // Ownership metadata refines the logical output layout only. It cannot change
        // the remote payload, frozen transport request, or permission to reuse that download.
        static CloudRemoteEntry? Payload(CloudRemoteEntry? entry) => entry == null ? null : entry with {OwnedContainer = null};
        static CloudDownloadRequest PayloadRequest(CloudDownloadRequest value) => value with {RemoteEntry = Payload(value.RemoteEntry)};
        if (request == null || configured == null || request.OperationId != intent || request.RemoteEntry == null ||
            request.AccountId != (target.AccountId ?? source.AccountId) || request.AccountId != target.RemoteEntry.AccountId ||
            request.RemoteEntry.ProviderId != source.ProviderId || download.ThirdPartyId != CloudSites.Site(source.ProviderId) ||
            Json(request.Source) != Json(source) || Json(Payload(request.RemoteEntry)) != Json(Payload(target.RemoteEntry)) ||
            Json(PayloadRequest(request)) != Json(PayloadRequest(configured)) ||
            string.IsNullOrWhiteSpace(request.RemoteEntry.ManifestJson) ||
            !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(CloudDownloadDestinations.ExecutionDirectory(request)), Path.GetFullPath(download.DownloadPath)))
            return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        // A shared destination or operation requires explicit coordination, even when
        // the other task is currently failed. Never restart beneath another owner.
        if (await db.DownloadTasks.AsNoTracking().AnyAsync(d => d.Id != download.Id && d.DownloadPath == download.DownloadPath, ct))
            return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        var transferSlot = CloudOperationService.OperationSlot(target, "transfer");
        var transferBindings = bindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner &&
            (b.Slot == transferSlot || b.Slot.StartsWith(target.Id + ":transfer:", StringComparison.Ordinal))).ToArray();
        if (transferBindings.Length != 1 || transferBindings[0].Slot != transferSlot)
            return Block("RECOVERY_TRANSFER_REVIEW_REQUIRED");
        var transferId = transferBindings[0].OperationId;
        if (await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == transferId &&
                (b.OwnerKind != "workflow" || b.OwnerId != owner || b.Slot != transferSlot), ct))
            return Block("RECOVERY_TRANSFER_REVIEW_REQUIRED");
        var transfer = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == transferId, ct);
        if (transfer == null || transfer.Kind != (target.ReviewedTransferId == null ? "transfer" : CloudOperationService.ReplayKind) || transfer.State != "succeeded" || transfer.WorkflowRunId != runId ||
            transfer.TargetId != target.Id || transfer.ReceiptJson == null || transfer.DownloadTaskId != null)
            return Block("RECOVERY_TRANSFER_REVIEW_REQUIRED");
        var transferRequest = JsonSerializer.Deserialize<CloudTransferRequest>(transfer.RequestJson, JsonSerializerOptions.Web);
        var receipt = JsonSerializer.Deserialize<CloudTransferReceipt>(transfer.ReceiptJson, JsonSerializerOptions.Web);
        if (transferRequest == null || receipt == null || transferRequest.OperationId != transferId || receipt.OperationId != transferId ||
            transferRequest.AccountId != request.AccountId || Json(transferRequest.Source) != Json(source) ||
            Json(Payload(receipt.Entry)) != Json(Payload(request.RemoteEntry)))
            return Block("RECOVERY_TRANSFER_REVIEW_REQUIRED");
        // The receipt pins the provider's actual remote entry. A provider may create
        // an owned child directory beneath the requested root; those paths are not
        // required to be equal by the transfer contract.
        var executionRoot = CloudDownloadDestinations.ExecutionRoot(request, outputRoot);
        storage.EnsurePathAllowed(executionRoot); storage.EnsurePathAllowed(download.DownloadPath);
        FileProcessingFiles.Within(download.DownloadPath, executionRoot);
        if (!Directory.Exists(executionRoot)) return Block("RECOVERY_STORAGE_UNAVAILABLE");
        for (var directory = new DirectoryInfo(download.DownloadPath); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        if (requireRetainedFiles && !File.Exists(download.DownloadPath) && !HasRetainedFiles(executionRoot, download.DownloadPath, ct))
            return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        var executionRequest = CloudDownloadDestinations.ExecutionRequest(request);
        if (nativeVerification.HasValue)
        {
            var otherDestinations = await db.DownloadTasks.AsNoTracking().Where(d => d.Id != download.Id)
                .Select(d => d.DownloadPath).Distinct().ToListAsync(ct);
            if (otherDestinations.Any(path => CloudDownloadDestinationService.Overlaps(path, download.DownloadPath)))
                return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            var inventory = nativeVerification == NativeVerificationScope.InspectInventory ? await verifier!.InspectExistingFileInventoryAsync(executionRequest, ct) : null;
            if (inventory is {Available: false}) return Block("RECOVERY_SOURCE_STILL_BLOCKED", inventory.Problem) with {Inventory = inventory};
            var identity = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                runId, target, outputRoot, operation.Id, operation.RequestJson, operation.State,
                transferId, transferRequest = transfer.RequestJson, transfer.ReceiptJson,
                native = new {download.Id, download.Key, download.Options, download.DownloadPath, download.ThirdPartyId,
                    download.Status, download.Message, download.Progress, download.DownloadStatusUpdateDt}
            }, JsonSerializerOptions.Web));
            return new(false, "RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", Candidate: true, VerifyExistingOnly: true)
                {VerificationIdentity = Convert.ToHexString(identity), Inventory = inventory};
        }
        var inspection = verifier == null ? await provider!.InspectRetainedDownloadAsync(executionRequest, revalidate, ct)
            : await verifier.InspectExistingDownloadAsync(executionRequest, ct);
        if (!inspection.Available) return Block("RECOVERY_SOURCE_STILL_BLOCKED", inspection.Problem);
        if (verifier != null)
            // This authorizes only running full verification, not successful completion or a restart.
            // The potentially long content read happens in the resumable workflow, outside preview budgets.
            return new(revalidate, "RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", failure, true, true);
        if (!revalidate) return Block("RECOVERY_RECHECK_SOURCE", candidate: true);
        // The remote read can overlap edits. Repeat local identity/ownership/checkpoint
        // checks; the same path runs once more under StartConditionally's action lock.
        var latest = await CheckAsync(runId, target, outputRoot, false, ct, includeStopped);
        return latest is {Candidate: true, VerifyExistingOnly: false}
            ? new(true, "RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", failure, true) : latest;
    }
}
