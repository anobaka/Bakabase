using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

internal sealed record CloudTransferReplay(CloudDownloadTarget Target, string OutputDirectory,
    string RemoteDirectory, CloudTransferRequest Request);

public sealed partial class CloudOperationService
{
    internal const string ReplayKind = "transfer-replay";

    internal bool CanRecoverTransfer(CloudSource source) =>
        providers.Any(p => p.ProviderId == source.ProviderId && p.SupportsTransfer) &&
        Provider(source) is ICloudTransferRecoveryProvider;

    internal async Task<CloudTransferReplay?> InspectTransferReplayAsync(int runId, CloudDownloadTarget target,
        string outputDirectory, string remoteDirectory, CancellationToken ct)
    {
        if (target.SelectedSource is not {} source || !CanRecoverTransfer(source) || target.ReviewedTransferId != null ||
            target.AccountId is not {Length: > 0} account || target.RemoteEntry is not {} remote ||
            !string.IsNullOrEmpty(source.AccountId) && source.AccountId != account ||
            !Path.IsPathFullyQualified(outputDirectory) || target.DownloadTaskId != null ||
            target.TransportTaskId != null || target.AcquisitionNonce != null || target.Excluded ||
            target.DownloadOperationId != null || target.ProcessingOperationId != null || target.PublicationOperationId != null ||
            target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null ||
            target.DownloadDirectory != null || target.ProcessedDirectory != null || target.PublishedDirectory != null ||
            target.Files.Count != 0 || target.ProcessedFiles.Count != 0 || target.PublishedFiles.Count != 0 ||
            target.ProcessingStepArtifacts.Count != 0 || target.CleanupReceiptId != null) return null;

        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var transferSlot = OperationSlot(target, "transfer");
        var original = TransferRequest(source, account, remoteDirectory);
        if (!await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.Slot == transferSlot && b.OperationId == original.OperationId, ct)) return null;
        var saved = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == original.OperationId, ct);
        if (saved is not {Kind: "transfer", State: "succeeded", DownloadTaskId: null, ReceiptJson: not null} ||
            !MatchesTransferRequest(saved, original, out var frozen)) return null;
        var retained = ReadTransferReceipt(saved, frozen).Entry;
        if (remote != retained && !(remote.OwnedContainer == null && retained with {OwnedContainer = null} == remote)) return null;

        var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputDirectory, services?.GetService<FileProcessingWorkspaceResolver>());
        var replayId = Key(ReplayKind + ".v1", owner, target.Id, frozen.OperationId, saved.ReceiptJson);
        var replaySlot = OperationSlot(target with {ReviewedTransferId = replayId}, "transfer");
        if (await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.Id == intent || o.Kind == "download" &&
            o.WorkflowRunId == runId && o.TargetId == target.Id, ct) ||
            await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == owner &&
                b.Slot.StartsWith(target.Id + ":") && b.Slot != transferSlot && (b.Slot != replaySlot || b.OperationId != replayId), ct)) return null;
        var path = CloudStageDirectoryPath.Download(target) ?? target.DownloadDirectoryScope?.Directory ??
            Path.Combine(outputDirectory, target.StoragePolicy == null ? "cloud-" + intent[..16] :
                Bakabase.Modules.Workflow.Artifacts.ArtifactStoragePolicies.RenderDownload(target.StoragePolicy, target.Title, intent, source.ProviderId));
        var root = target.DownloadDirectoryScope?.StorageRoot ?? outputDirectory;
        storage.EnsurePathAllowed(root);
        storage.EnsurePathAllowed(path);
        FileProcessingFiles.Within(path, root);
        if (!Directory.Exists(root) || File.Exists(path) || CloudDownloadRecoveryService.HasRetainedFiles(root, path, ct)) return null;

        var request = frozen with {OperationId = replayId};
        var previous = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == replayId, ct);
        if (previous != null)
        {
            if (!MatchesReplay(previous, runId, target.Id, request)) return null;
            if (previous.State == "succeeded") _ = ReadTransferReceipt(previous, request);
            else if (previous.State != "planned")
            {
                if (previous.ReceiptJson != null || Provider(source) is not ICloudTransferRecoveryProvider recovery) return null;
                var check = await recovery.InspectTransferRecoveryAsync(request, true, ct);
                if (check.Disposition is not (CloudTransferRecoveryDisposition.Retry or CloudTransferRecoveryDisposition.Reconcile)) return null;
            }
        }
        return new(target, outputDirectory, remoteDirectory, request);
    }

    private static bool MatchesReplay(CloudOperationDbModel replay, int runId, string targetId, CloudTransferRequest request) =>
        replay.Kind == ReplayKind && replay.WorkflowRunId == runId && replay.TargetId == targetId && replay.DownloadTaskId == null &&
        replay.RequestJson == JsonSerializer.Serialize(request, Json);

    internal Task<bool> PrepareTransferReplayAsync(int runId, CloudTransferReplay replay, CancellationToken ct) =>
        WithOperationLockAsync(replay.Request.OperationId, async () =>
        {
            var verified = await InspectTransferReplayAsync(runId, replay.Target, replay.OutputDirectory, replay.RemoteDirectory, ct);
            if (verified?.Request.OperationId != replay.Request.OperationId) return false;
            var effective = verified.Target with {ReviewedTransferId = verified.Request.OperationId};
            if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(),
                    OperationSlot(effective, "transfer"), verified.Request.OperationId, ct)) return false;
            await GetOrCreate(verified.Request.OperationId, runId, replay.Target.Id, ReplayKind, verified.Request, ct);
            return true;
        }, ct);

    internal Task<CloudTransferReceipt> ExecutePreparedTransferReplayAsync(int runId, CloudDownloadTarget target,
        string outputDirectory, string remoteDirectory, string replayId, CancellationToken ct) =>
        WithOperationLockAsync(replayId, async () =>
        {
            var reviewed = await InspectTransferReplayAsync(runId, target, outputDirectory, remoteDirectory, ct);
            if (reviewed?.Request.OperationId != replayId) throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var replay = await db.CloudDownloadOperations.SingleOrDefaultAsync(o => o.Id == replayId, ct);
            var request = reviewed.Request;
            if (replay == null || !MatchesReplay(replay, runId, target.Id, request))
                throw new InvalidOperationException("RECOVERY_TRANSFER_RETRY_CONFIRMATION_REQUIRED");
            if (replay.State == "succeeded") return ReadTransferReceipt(replay, request);
            var provider = Provider(request.Source);
            if (replay.State == "planned") return await ExecuteTransferAsync(replay, request, provider, ct);
            var check = await ((ICloudTransferRecoveryProvider)provider).InspectTransferRecoveryAsync(request, true, ct);
            if (check is {Disposition: CloudTransferRecoveryDisposition.Reconcile, Receipt: not null})
                return await CompleteTransferAsync(replay, request, check.Receipt, ct);
            if (check.Disposition == CloudTransferRecoveryDisposition.Retry)
                return await ExecuteTransferAsync(replay, request, provider, ct);
            throw new InvalidOperationException(TransferRecoveryBlocked);
        }, ct);
}
