using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record CloudDownloadDestinationPreview(bool CanRevise, string ReasonCode, string? Fingerprint = null,
    int? DownloadTaskId = null, string? OperationId = null, string? PreviousDirectory = null,
    string? NewDirectory = null, string? NewRoot = null);
public sealed record CloudDownloadDestinationRevisionReceipt(int DownloadTaskId, string OperationId,
    CloudDownloadDestinationRevision Revision, bool AlreadyApplied = false);
public interface ICloudDownloadDestinationService
{
    Task<CloudDownloadDestinationPreview> PreviewAsync(int runId, CloudDownloadTarget target,
        string semanticOutputRoot, string newRoot, string newDirectory, CancellationToken ct);
    Task<CloudDownloadDestinationRevisionReceipt> ApplyAsync(int runId, CloudDownloadTarget target,
        string semanticOutputRoot, string newRoot, string newDirectory, string expectedFingerprint,
        string revisionId, CancellationToken ct, Func<CancellationToken, Task>? validateOwner = null);
}

/// <summary>Changes only an idle, provably empty physical destination. The caller must hold its owning
/// workflow/task action lock; this service additionally serializes with cloud enqueue and downloader start.
/// No filesystem write, transfer, download, or restart is performed by this action.</summary>
public sealed class CloudDownloadDestinationService(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    IUserStoragePolicy storage, DownloaderManager manager, IStorageWriteGuard writes,
    FileProcessingWorkspaceResolver? workspaces = null) : ICloudDownloadDestinationService
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    private static readonly StringComparison Paths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<CloudDownloadDestinationPreview> PreviewAsync(int runId, CloudDownloadTarget target,
        string semanticOutputRoot, string newRoot, string newDirectory, CancellationToken ct)
    {
        try
        {
            var found = await InspectAsync(runId, target, semanticOutputRoot, newRoot, newDirectory, ct);
            return new(true, "DOWNLOAD_DESTINATION_READY", found.Fingerprint, found.Download.Id,
                found.Operation.Id, found.Download.DownloadPath, found.NewDirectory, found.NewRoot);
        }
        catch (DestinationBlocked error) { return new(false, error.Code); }
        catch (LocalStorageException error) { return new(false, error.Code); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return new(false, "DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE"); }
    }

    public Task<CloudDownloadDestinationRevisionReceipt> ApplyAsync(int runId, CloudDownloadTarget target,
        string semanticOutputRoot, string newRoot, string newDirectory, string expectedFingerprint,
        string revisionId, CancellationToken ct, Func<CancellationToken, Task>? validateOwner = null)
    {
        if (target.DownloadTaskId is not { } downloadId || !Guid.TryParseExact(revisionId, "N", out _))
            throw new InvalidOperationException("DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, semanticOutputRoot, workspaces);
        // Match enqueue's operation -> task lock order; never hold the task lock while waiting on enqueue.
        return CloudOperationService.WithOperationLockAsync(intent, () => manager.WithTaskActionLock(downloadId, async () =>
        {
            var runGate = WorkflowRunMutationGate.For(runId);
            await runGate.WaitAsync(ct);
            try
            {
            ct.ThrowIfCancellationRequested();
            if (validateOwner != null) await validateOwner(ct);
            var previous = await ReadAppliedAsync(runId, target.Id, intent, downloadId, revisionId, expectedFingerprint, newRoot, newDirectory, ct);
            if (previous != null) return previous;
            var found = await InspectAsync(runId, target, semanticOutputRoot, newRoot, newDirectory, ct);
            if (found.Fingerprint != expectedFingerprint)
                throw new InvalidOperationException("DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
            var revision = new CloudDownloadDestinationRevision(revisionId, found.Download.DownloadPath,
                found.NewDirectory, found.NewRoot, DateTime.UtcNow, expectedFingerprint);
            var revised = found.Request with {DestinationRevisions = [.. found.Request.DestinationRevisions ?? [], revision]};
            CloudDownloadDestinations.Validate(revised);
            var json = JsonSerializer.Serialize(revised, Json);
            if (validateOwner != null) await validateOwner(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // CAS protects edits outside the owner action locks and process restart/repeated submissions.
            var changed = await db.DownloadTasks.Where(d => d.Id == downloadId && d.Options == found.Download.Options &&
                d.DownloadPath == found.Download.DownloadPath && d.Status == found.Download.Status &&
                d.Checkpoint == found.Download.Checkpoint && d.Progress == 0 && d.CompletedAt == null &&
                d.Message == found.Download.Message).ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.DownloadPath, found.NewDirectory).SetProperty(d => d.Options, json), ct);
            if (changed != 1) throw new InvalidOperationException("DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
            changed = await db.CloudDownloadOperations.Where(o => o.Id == intent && o.RequestJson == found.Operation.RequestJson &&
                o.State == found.Operation.State && o.DownloadTaskId == downloadId && o.ReceiptJson == null)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.RequestJson, json).SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
            if (changed != 1) throw new InvalidOperationException("DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
            await transaction.CommitAsync(ct);
            manager.Forget(downloadId);
            return new CloudDownloadDestinationRevisionReceipt(downloadId, intent, revision);
            }
            finally {runGate.Release();}
        }), ct);
    }

    private async Task<CloudDownloadDestinationRevisionReceipt?> ReadAppliedAsync(int runId, string targetId, string intent, int downloadId,
        string revisionId, string expectedFingerprint, string newRoot, string newDirectory, CancellationToken ct)
    {
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
        if (operation == null) return null;
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, Json);
        if (request?.DestinationRevisions == null) return null;
        CloudDownloadDestinations.Validate(request);
        var prior = request.DestinationRevisions.SingleOrDefault(r => r.RevisionId == revisionId);
        if (prior == null) return null;
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == downloadId, ct);
        Require(operation.Kind == "download" && operation.WorkflowRunId == runId && operation.TargetId == targetId &&
            operation.DownloadTaskId == downloadId && request.OperationId == intent &&
            download != null && Serialize(JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options ?? "", Json)) == Serialize(request) &&
            SamePath(download.DownloadPath, CloudDownloadDestinations.ExecutionDirectory(request)) &&
            prior.PreviousFingerprint == expectedFingerprint && SamePath(prior.Directory, newDirectory) && SamePath(prior.RootDirectory, newRoot),
            "DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
        return new(downloadId, intent, prior, true);
    }

    private sealed record Inspection(DownloadTaskDbModel Download, CloudOperationDbModel Operation,
        CloudDownloadRequest Request, string NewRoot, string NewDirectory, string Fingerprint);
    private sealed class DestinationBlocked(string code) : InvalidOperationException(code) { public string Code => code; }
    private static void Require(bool condition, string code) { if (!condition) throw new DestinationBlocked(code); }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static CloudRemoteEntry? Payload(CloudRemoteEntry? entry) => entry == null ? null : entry with {OwnedContainer = null};
    private static bool SamePath(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), Paths);

    private async Task<Inspection> InspectAsync(int runId, CloudDownloadTarget target, string semanticRoot,
        string newRoot, string newDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Require(Path.IsPathFullyQualified(semanticRoot) && Path.IsPathFullyQualified(newRoot) &&
                Path.IsPathFullyQualified(newDirectory), "DOWNLOAD_DESTINATION_PATH_INVALID");
        Require(target.DownloadTaskId != null && !target.Excluded && target.SelectedSource != null &&
                target.TransportTaskId == null && target.AcquisitionNonce == null, "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        var source = target.SelectedSource!;
        var provider = DownloadProviderRouting.Require(providers, source) as ICloudDownloadDestinationRevisionProvider;
        Require(provider != null, "DOWNLOAD_DESTINATION_PROVIDER_UNSUPPORTED");
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == target.DownloadTaskId, ct);
        Require(download != null, "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        Require(download!.Status is DownloadTaskDbModelStatus.Failed or DownloadTaskDbModelStatus.Disabled &&
                manager[download.Id]?.IsOccupyingDownloadTaskSource() != true, "DOWNLOAD_DESTINATION_ACTIVE");
        Require(download.Progress == 0 && download.CompletedAt == null && string.IsNullOrEmpty(download.Checkpoint) &&
                target.Files.Count == 0 && target.ProcessedFiles.Count == 0 && target.PublishedFiles.Count == 0 &&
                target.DownloadArtifactId == null && target.ProcessedArtifactId == null && target.PublishedArtifactId == null &&
                target.ProcessingOperationId == null && target.PublicationOperationId == null && target.CleanupReceiptId == null &&
                target.ProcessingStepArtifacts.Count == 0 &&
                !await db.DownloadResults.AsNoTracking().AnyAsync(d => d.DownloadTaskId == download.Id, ct) &&
                !await db.DownloadResultOwners.AsNoTracking().AnyAsync(d => d.DownloadTaskId == download.Id, ct),
            "DOWNLOAD_DESTINATION_HAS_OUTPUT");
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, semanticRoot, workspaces);
        // Older worksets omitted this hint. The persisted operation and exclusive binding
        // below remain authoritative; a present but conflicting hint must still fail closed.
        Require(target.DownloadOperationId == null || target.DownloadOperationId == intent,
            "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
        Require(operation is {Kind: "download", State: "started" or "starting" or "queued", ReceiptJson: null} &&
                operation.DownloadTaskId == download.Id && operation.WorkflowRunId == runId && operation.TargetId == target.Id &&
                !await db.ArtifactOperations.AsNoTracking().AnyAsync(a => a.Id == intent, ct) &&
                !await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.DownloadTaskId == download.Id && o.Id != intent, ct),
            "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        Require(run != null && run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted &&
                run.PendingSignalJson == null, "DOWNLOAD_DESTINATION_ACTIVE");
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation!.RequestJson, Json);
        var configured = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options ?? "", Json);
        Require(request != null && configured != null && request.OperationId == intent &&
                Serialize(request) == Serialize(configured) && Serialize(request.Source) == Serialize(source) &&
                request.AccountId == (target.AccountId ?? source.AccountId) &&
                Serialize(Payload(request.RemoteEntry)) == Serialize(Payload(target.RemoteEntry)) &&
                download.ThirdPartyId == CloudSites.Site(source.ProviderId), "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        CloudDownloadDestinations.Validate(request!);
        Require(request!.DestinationRevisions == null || request.DestinationRevisions.Count < 32, "DOWNLOAD_DESTINATION_HISTORY_LIMIT");
        var currentDirectory = CloudDownloadDestinations.ExecutionDirectory(request);
        var currentRoot = CloudDownloadDestinations.ExecutionRoot(request, semanticRoot);
        Require(SamePath(currentDirectory, download.DownloadPath), "DOWNLOAD_DESTINATION_IDENTITY_CHANGED");
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == intent ||
            b.OwnerKind == "workflow" && b.OwnerId == runId.ToString()).ToListAsync(ct);
        Require(bindings.Count(b => b.OperationId == intent) == 1 && bindings.Any(b => b.OperationId == intent &&
            b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() && b.Slot == CloudOperationService.OperationSlot(target, "download")),
            "DOWNLOAD_DESTINATION_SHARED");
        var transferEvidence = await ValidateTransferAsync(runId, target, request, bindings, ct);
        newRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newRoot));
        newDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newDirectory));
        Require(!SamePath(currentDirectory, newDirectory), "DOWNLOAD_DESTINATION_UNCHANGED");
        EnsureEmptyDestination(currentRoot, currentDirectory);
        EnsureEmptyDestination(newRoot, newDirectory);
        var paths = await db.DownloadTasks.AsNoTracking().Where(d => d.Id != download.Id).Select(d => d.DownloadPath).ToListAsync(ct);
        Require(!paths.Any(p => Overlaps(p, newDirectory) || Overlaps(p, currentDirectory)), "DOWNLOAD_DESTINATION_SHARED");
        var artifacts = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired)
            .Select(a => new ArtifactPlacement(a.Id, a.State, a.Managed, a.RootPath, a.RootIsAppData,
                a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData, a.Fingerprint)).ToListAsync(ct);
        foreach (var artifact in artifacts)
        {
            Require(artifact.QuarantineSourceRootPath == null || artifact.QuarantineSourceRootIsAppData ||
                !Overlaps(artifact.QuarantineSourceRootPath, newDirectory) && !Overlaps(artifact.QuarantineSourceRootPath, currentDirectory),
                "DOWNLOAD_DESTINATION_SHARED");
            if (artifact.RootIsAppData) continue;
            var intersecting = new[] {currentDirectory, newDirectory}.Where(path => Overlaps(artifact.RootPath, path)).ToArray();
            if (intersecting.Length == 0) continue;
            Require(!await ArtifactOccupiesAsync(artifact, intersecting, ct), "DOWNLOAD_DESTINATION_SHARED");
        }
        // Caller-owned acquisition settings supply the reserve through the existing write-policy provider.
        await writes.CheckAsync(newDirectory, new("cloudOperation", intent), ct);
        var readiness = await provider!.InspectDestinationRevisionAsync(CloudDownloadDestinations.ExecutionRequest(request), newDirectory, ct);
        Require(readiness.Available, readiness.Problem?.StartsWith("DOWNLOAD_DESTINATION_", StringComparison.Ordinal) == true
            ? readiness.Problem : "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        // Re-read local facts after provider I/O; apply also repeats this entire inspection under action locks.
        EnsureEmptyDestination(currentRoot, currentDirectory); EnsureEmptyDestination(newRoot, newDirectory);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(new
        {runId, target, semanticRoot, newRoot, newDirectory, download, operation, transferEvidence,
            RunStatus = run!.Status, run.CurrentItemJson, run.PlanSnapshotJson, run.PendingSignalJson,
            Bindings = bindings.OrderBy(b => b.Id).ToArray()})))).ToLowerInvariant();
        return new(download, operation, request, newRoot, newDirectory, fingerprint);
    }

    private sealed record ArtifactPlacement(string Id, ArtifactLocationState State, bool Managed, string RootPath,
        bool RootIsAppData, string? QuarantineSourceRootPath, bool QuarantineSourceRootIsAppData, string Fingerprint);

    private async Task<bool> ArtifactOccupiesAsync(ArtifactPlacement artifact, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        // A managed root or any uncertain/quarantined location remains an exclusive fence.
        // An available unmanaged artifact may instead describe exact files beneath a shared
        // ancestor (for example a collection assembled from separate existing directories).
        if (artifact.Managed || artifact.State != ArtifactLocationState.Available ||
            string.IsNullOrWhiteSpace(artifact.RootPath) || !Path.IsPathFullyQualified(artifact.RootPath)) return true;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootPath));
        if (destinations.Any(path => !Path.GetFullPath(path).StartsWith(Path.EndsInDirectorySeparator(root)
                ? root : root + Path.DirectorySeparatorChar, Paths))) return true;
        // Fetch only overlapping manifests, never all payload inventories for a batch preview.
        var json = await db.Artifacts.AsNoTracking().Where(a => a.Id == artifact.Id && a.RootPath == artifact.RootPath &&
                a.RootIsAppData == artifact.RootIsAppData && a.State == artifact.State && a.Managed == artifact.Managed &&
                a.Fingerprint == artifact.Fingerprint && a.QuarantineSourceRootPath == artifact.QuarantineSourceRootPath &&
                a.QuarantineSourceRootIsAppData == artifact.QuarantineSourceRootIsAppData)
            .Select(a => a.ManifestJson).SingleOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(json) || json.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("The shared-root artifact manifest cannot be verified.");
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(json, Json);
        if (manifest?.Entries is not {Count: > 0 and <= 100000} || manifest.Fingerprint != artifact.Fingerprint ||
            manifest.Entries.Any(e => e == null || e.Sha256 is not {Length: 64} || e.Sha256.Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("The shared-root artifact manifest is invalid.");
        // ResolveFiles checks fingerprint, traversal, duplicates and explicit directory safety
        // using metadata only. It does not read files or infer ownership of unlisted siblings.
        var members = ArtifactManifestFiles.ResolveFiles(root, manifest)
            .Concat(ArtifactManifestFiles.ResolveDirectories(root, manifest));
        return members.Any(member => destinations.Any(path => Overlaps(member, path)));
    }

    private async Task<object?> ValidateTransferAsync(int runId, CloudDownloadTarget target, CloudDownloadRequest request,
        IReadOnlyList<ArtifactOperationBindingDbModel> bindings, CancellationToken ct)
    {
        var slot = CloudOperationService.OperationSlot(target, "transfer");
        var transfers = bindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() &&
            (b.Slot == slot || b.Slot.StartsWith(target.Id + ":transfer:", StringComparison.Ordinal))).ToArray();
        if (request.RemoteEntry == null)
        {
            Require(transfers.Length == 0 && !await db.CloudDownloadOperations.AnyAsync(o => o.WorkflowRunId == runId &&
                o.TargetId == target.Id && o.Kind == "transfer", ct), "DOWNLOAD_DESTINATION_TRANSFER_REVIEW_REQUIRED");
            return null;
        }
        Require(transfers.Length == 1 && transfers[0].Slot == slot, "DOWNLOAD_DESTINATION_TRANSFER_REVIEW_REQUIRED");
        var id = transfers[0].OperationId;
        Require(!await db.ArtifactOperationBindings.AnyAsync(b => b.OperationId == id &&
            (b.OwnerKind != "workflow" || b.OwnerId != runId.ToString() || b.Slot != slot), ct), "DOWNLOAD_DESTINATION_SHARED");
        var transfer = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        Require(transfer is {Kind: "transfer", State: "succeeded", DownloadTaskId: null, ReceiptJson: not null} &&
            transfer.WorkflowRunId == runId && transfer.TargetId == target.Id, "DOWNLOAD_DESTINATION_TRANSFER_REVIEW_REQUIRED");
        var receipt = JsonSerializer.Deserialize<CloudTransferReceipt>(transfer!.ReceiptJson!, Json);
        var original = JsonSerializer.Deserialize<CloudTransferRequest>(transfer.RequestJson, Json);
        Require(receipt != null && original != null && receipt.OperationId == id && original.OperationId == id &&
            original.AccountId == request.AccountId && Serialize(original.Source) == Serialize(request.Source) &&
            Serialize(Payload(receipt.Entry)) == Serialize(Payload(request.RemoteEntry)), "DOWNLOAD_DESTINATION_TRANSFER_REVIEW_REQUIRED");
        return transfer;
    }

    private void EnsureEmptyDestination(string root, string directory)
    {
        storage.EnsurePathAllowed(root); storage.EnsurePathAllowed(directory);
        Require(!SamePath(root, directory), "DOWNLOAD_DESTINATION_PATH_INVALID");
        FileProcessingFiles.Within(directory, root); ArtifactManifestFiles.RejectLinks(root);
        Require(Directory.Exists(root), "DOWNLOAD_DESTINATION_STORAGE_UNAVAILABLE");
        // Actually enumerate the selected root; Directory.Exists alone swallows some access/I/O failures.
        using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) _ = entries.MoveNext();
        ArtifactManifestFiles.RejectLinks(directory);
        for (var parent = Path.GetDirectoryName(directory); parent != null && !SamePath(parent, root); parent = Path.GetDirectoryName(parent))
        {
            try {Require(File.GetAttributes(parent).HasFlag(FileAttributes.Directory), "DOWNLOAD_DESTINATION_PATH_INVALID");}
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
        }
        try
        {
            var attributes = File.GetAttributes(directory);
            Require(attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint),
                "DOWNLOAD_DESTINATION_HAS_OUTPUT");
            using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            Require(!entries.MoveNext(), "DOWNLOAD_DESTINATION_HAS_OUTPUT");
        }
        catch (DirectoryNotFoundException) { }
        catch (FileNotFoundException) { }
    }

    private static bool Overlaps(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || !Path.IsPathFullyQualified(left)) return true;
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)); var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return a.Equals(b, Paths) || a.StartsWith(b + Path.DirectorySeparatorChar, Paths) || b.StartsWith(a + Path.DirectorySeparatorChar, Paths);
    }
}
