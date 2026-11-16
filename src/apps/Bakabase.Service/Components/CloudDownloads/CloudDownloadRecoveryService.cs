using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record CloudDownloadRecoveryCheck(bool CanRetry, string ReasonCode, string? FailureCode = null,
    bool Candidate = false, bool VerifyExistingOnly = false);

/// <summary>Validates only the original failed task. No enqueue, transfer, mutation, new generation,
/// artifact adoption or payload access occurs here. Reused again immediately before an explicit retry.</summary>
public sealed partial class CloudDownloadRecoveryService(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces = null)
{
    public Task<CloudDownloadRecoveryCheck> CheckAsync(int runId, CloudDownloadTarget target,
        string outputRoot, bool revalidate, CancellationToken ct, bool includeStopped = false) =>
        CheckCoreAsync(runId, target, outputRoot, revalidate, forSourceRevision: false, ct, includeStopped);

    /// <summary>Checks the retained execution identity and absence of local outputs without
    /// authorizing another attempt against the old source. The caller must separately prove
    /// provider-side abandonment safety and exclusive ownership under the action lock.</summary>
    public Task<CloudDownloadRecoveryCheck> CheckNoOutputForSourceRevisionAsync(int runId, CloudDownloadTarget target,
        string outputRoot, CancellationToken ct) =>
        CheckCoreAsync(runId, target, outputRoot, revalidate: false, forSourceRevision: true, ct);

    private async Task<CloudDownloadRecoveryCheck> CheckCoreAsync(int runId, CloudDownloadTarget target,
        string outputRoot, bool revalidate, bool forSourceRevision, CancellationToken ct, bool includeStopped = false)
    {
        string? failure = null;
        CloudDownloadRecoveryCheck Block(string code, bool candidate = false) => new(false, code, failure, candidate);
        try
        {
            if (target.Excluded || target.SelectedSource is not {} source || target.DownloadTaskId is not {} id ||
                target.TransportTaskId != null || target.AcquisitionNonce != null)
                return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (download == null) return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            var stopped = includeStopped && download.Status == DownloadTaskDbModelStatus.Disabled;
            if (!stopped && download.Status != DownloadTaskDbModelStatus.Failed)
                return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            var provider = DownloadProviderRouting.Require(providers, source);
            var capability = provider as ICloudDownloadRecoveryProvider;
            // A manual stop can clear the transport message. Only an explicit request to resume
            // the original download may use the failure saved on its pinned workflow target.
            var message = stopped && string.IsNullOrWhiteSpace(download.Message) ? target.Problem : download.Message;
            var codes = WorkflowSourceRunProblems.Extract(message).Distinct().ToArray();
            if (!stopped && !forSourceRevision && provider is ICloudExistingDownloadVerificationProvider verifier &&
                (IsCompletionRegistrationFailure(download.Message) ||
                 codes.Length == 1 && verifier.CanVerifyExistingDownloadFailure(codes[0])))
                // Verification-only takes precedence over retained transport recovery. A partial
                // tree or a failed checksum must never silently turn this action into a download.
                return await CheckRetainedAsync(runId, target, outputRoot, download, null,
                    codes.Length == 1 ? codes[0] : "RECOVERY_MANIFEST_CHANGED", revalidate, ct, verifier);
            if (!forSourceRevision && provider is ICloudRetainedDownloadRecoveryProvider retained)
            {
                // Older storage-policy failures predate machine codes. Only the exact local
                // message for this persisted destination is eligible; quoted provider errors,
                // other paths and unrecognized text must not authorize a retry.
                var retainedFailure = codes.Length == 1 ? codes[0] :
                    codes.Length == 0 && IsExactLegacyStorageFailure(message, download.DownloadPath, download.Options)
                        ? "LOCAL_STORAGE_UNAVAILABLE" : null;
                if (retainedFailure != null && retained.CanRevalidateRetainedDownloadFailure(retainedFailure))
                    return await CheckRetainedAsync(runId, target, outputRoot, download, retained, retainedFailure, revalidate, ct, includeStopped: stopped);
            }
            if (stopped || codes.Length != 1 || (forSourceRevision
                    ? provider is not ICloudDownloadAbandonmentProvider
                    : capability == null || !capability.CanRevalidateDownloadFailure(codes[0])))
                return Block("RECOVERY_MANUAL_REVIEW");
            failure = codes[0];
            if (target.RemoteEntry != null || target.DownloadArtifactId != null || target.ProcessedArtifactId != null ||
                target.PublishedArtifactId != null || target.ProcessingOperationId != null || target.PublicationOperationId != null ||
                target.CleanupReceiptId != null || target.Files.Count != 0 || target.ProcessedFiles.Count != 0 ||
                target.PublishedFiles.Count != 0 || target.ProcessingStepArtifacts.Count != 0 ||
                download.CompletedAt != null || download.Progress != 0 || !string.IsNullOrEmpty(download.Checkpoint) ||
                await db.DownloadResults.AsNoTracking().AnyAsync(r => r.DownloadTaskId == id, ct) ||
                await db.DownloadResultOwners.AsNoTracking().AnyAsync(r => r.DownloadTaskId == id, ct))
                return Block("RECOVERY_DOWNLOAD_HAS_OUTPUT");
            var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, workspaces);
            if (target.DownloadOperationId != null && target.DownloadOperationId != intent)
                return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var owner = runId.ToString();
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
                b.OwnerId == owner).ToListAsync(ct);
            if (bindings.Any(b => b.Slot == target.Id + ":transfer" || b.Slot.StartsWith(target.Id + ":transfer:", StringComparison.Ordinal)) ||
                await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.WorkflowRunId == runId && o.TargetId == target.Id && o.Kind == "transfer", ct))
                return Block("RECOVERY_TRANSFER_REVIEW_REQUIRED");
            var downloadSlot = CloudOperationService.OperationSlot(target, "download");
            if (!bindings.Any(b => b.Slot == downloadSlot && b.OperationId == intent) ||
                await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == intent &&
                    (b.OwnerKind != "workflow" || b.OwnerId != owner || b.Slot != downloadSlot), ct))
                return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
            if (operation?.Kind != "download" || operation.DownloadTaskId != id || operation.WorkflowRunId != runId ||
                operation.TargetId != target.Id || operation.State is not ("started" or "starting" or "queued") ||
                await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.DownloadTaskId == id && o.Id != intent, ct) ||
                operation.ReceiptJson != null || await db.ArtifactOperations.AsNoTracking().AnyAsync(a => a.Id == intent, ct))
                return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web);
            var configured = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options ?? "", JsonSerializerOptions.Web);
            if (request == null || configured == null || request.OperationId != intent || request.RemoteEntry != null ||
                request.AccountId != (target.AccountId ?? source.AccountId) || download.ThirdPartyId != CloudSites.Site(source.ProviderId) ||
                JsonSerializer.Serialize(request.Source, JsonSerializerOptions.Web) != JsonSerializer.Serialize(source, JsonSerializerOptions.Web) ||
                JsonSerializer.Serialize(request, JsonSerializerOptions.Web) != JsonSerializer.Serialize(configured, JsonSerializerOptions.Web) ||
                !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(CloudDownloadDestinations.ExecutionDirectory(request)), Path.GetFullPath(download.DownloadPath)))
                return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var executionRoot = CloudDownloadDestinations.ExecutionRoot(request, outputRoot);
            storage.EnsurePathAllowed(executionRoot);
            storage.EnsurePathAllowed(download.DownloadPath);
            FileProcessingFiles.Within(download.DownloadPath, executionRoot);
            if (!Directory.Exists(executionRoot)) return Block("RECOVERY_STORAGE_UNAVAILABLE");
            // Include hidden and partial files. A failed receipt alone never proves that no bytes were written.
            if (File.Exists(download.DownloadPath) || HasRetainedFiles(executionRoot, download.DownloadPath, ct))
                return Block("RECOVERY_DOWNLOAD_HAS_OUTPUT");
            var executionRequest = CloudDownloadDestinations.ExecutionRequest(request);
            if (!forSourceRevision && capability != null)
            {
                var local = await capability.CheckLocalRecoveryAsync(executionRequest, ct);
                if (!local.Available) return Block("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            }
            if (!revalidate) return Block("RECOVERY_RECHECK_SOURCE", candidate: true);
            var ready = await provider.CheckReadyAsync(request.AccountId, ct);
            if (!ready.Available)
            {
                failure = WorkflowSourceRunProblems.Extract(ready.Problem).FirstOrDefault() ?? failure;
                return Block("RECOVERY_SOURCE_STILL_BLOCKED");
            }
            var current = await capability!.RevalidateDownloadAsync(executionRequest, ct);
            if (!current.Available)
            {
                failure = WorkflowSourceRunProblems.Extract(current.Problem).FirstOrDefault() ?? failure;
                return Block("RECOVERY_SOURCE_STILL_BLOCKED");
            }
            // A network probe can overlap another consumer or user edit. Do not certify a stale
            // local snapshot; execution repeats this local guard under the downloader action lock.
            var latest = await CheckAsync(runId, target, outputRoot, revalidate: false, ct);
            if (!latest.Candidate) return latest;
            return new(true, "RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", failure, true);
        }
        catch (UserStoragePathException) {return Block("RECOVERY_STORAGE_UNAVAILABLE");}
        catch (InvalidDataException) {return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");}
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {return Block("RECOVERY_CHECK_INCOMPLETE");}
    }

    private static bool IsCompletionRegistrationFailure(string? message) => message != null &&
        message.Contains("An artifact file changed while its manifest was being recorded.", StringComparison.Ordinal) &&
        message.Contains("DownloadResultService.RecordFilesAsync", StringComparison.Ordinal) &&
        message.Contains("CloudSiteDownloader", StringComparison.Ordinal);

    private static bool IsExactLegacyStorageFailure(string? message, string currentPath, string? options)
    {
        const string prefix = "Choose a folder inside a mounted storage location. ";
        if (message?.StartsWith(prefix, StringComparison.Ordinal) != true) return false;
        var failedPath = message[prefix.Length..];
        if (!Path.IsPathFullyQualified(failedPath) || failedPath.IndexOfAny(['\r', '\n', '\0']) >= 0) return false;
        if (string.Equals(currentPath, failedPath, StringComparison.Ordinal)) return true;
        if (string.IsNullOrEmpty(options)) return false;
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(options, JsonSerializerOptions.Web);
        // CheckRetainedAsync independently compares this entire request with the bound operation
        // before contacting the provider. Only the validated chain may name a former destination.
        return request?.DestinationRevisions is {Count: > 0} revisions &&
               ArtifactManifestFiles.PathComparer.Equals(CloudDownloadDestinations.ExecutionDirectory(request), currentPath) &&
               revisions.Any(r => string.Equals(failedPath, r.PreviousDirectory, StringComparison.Ordinal));
    }

    private static bool HasRetainedFiles(string executionRoot, string root, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(root);
        if (!Directory.Exists(root))
        {
            // Exists also returns false on access errors and dangling links. Prove the first
            // missing segment against a readable parent before calling the original task empty.
            var missing = root;
            while (Path.GetDirectoryName(missing) is { } parent && !Directory.Exists(parent) &&
                   !ArtifactManifestFiles.PathComparer.Equals(parent, executionRoot))
                missing = parent;
            if (ArtifactManifestFiles.IsMissingFileConfirmed(executionRoot, missing, ct)) return false;
            throw new IOException("The persisted download location could not be inspected.");
        }
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        var count = 0; var entries = 0;
        while (pending.TryPop(out var next))
        {
            ct.ThrowIfCancellationRequested();
            if (++count > 1000 || next.Depth > 32) throw new InvalidOperationException("Directory inspection limit reached.");
            if ((File.GetAttributes(next.Path) & FileAttributes.ReparsePoint) != 0) return true;
            foreach (var path in Directory.EnumerateFileSystemEntries(next.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > 1000) throw new InvalidOperationException("Directory inspection limit reached.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) return true;
                pending.Push((path, next.Depth + 1));
            }
        }
        return false;
    }
}
