using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>The site's download owns its local result. Remote copy ownership and release have their own durable outcome.</summary>
public sealed class CloudDownloadOwnedCopyService(BakabaseDbContext db, IUserStoragePolicy storage, IServiceProvider services,
    ILogger<CloudDownloadOwnedCopyService> logger)
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1)).ToArray();
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    internal static bool IsShare(CloudDownloadRequest? request) => request is {Source.Kind: "share", RemoteEntry: null}
        && !string.IsNullOrWhiteSpace(request.OperationId);
    private static string Operation(CloudDownloadRequest request) => CloudOperationService.Key(request.Source.ProviderId,
        request.Source.MethodId ?? "", request.OperationId);
    private static string Source(CloudDownloadRequest request) => CloudOperationService.Key(CloudSourceIdentity.Fingerprint(request.Source),
        request.AccountId ?? request.Source.AccountId ?? "");
    private static SemaphoreSlim Gate(string key) => Gates[Convert.ToByte(key[..2], 16) % Gates.Length];
    private static string ReceiptKey(DownloadResultDbModel r) => CloudOperationService.Key(r.Id.ToString(), r.DownloadTaskId.ToString(),
        r.DeduplicationKey, r.Fingerprint, r.DownloadDirectory, r.FilesJson);
    private static string PublicReason(string? value, string fallback) => value is {Length: > 0 and <= 100} &&
        value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? value : fallback;

    internal async Task PrepareAsync(int id, CloudDownloadRequest request, ICloudOwnedCopyProvider provider, CancellationToken ct)
    {
        if (!IsShare(request)) return;
        var operation = Operation(request);
        var gate = Gate(operation); await gate.WaitAsync(ct);
        try
        {
            var row = await db.CloudDownloadOwnedCopies.SingleOrDefaultAsync(o => o.DownloadTaskId == id, ct);
            if (row == null && await db.CloudDownloadOwnedCopies.AsNoTracking().AnyAsync(o => o.OperationKey == operation, ct))
                throw new CloudProviderException("CLOUD_OWNED_COPY_IN_USE");
            if (row != null && (row.OperationKey != operation || row.SourceIdentity != Source(request)))
                throw new CloudProviderException("CLOUD_OWNED_COPY_DOWNLOAD_CHANGED");
            if (row?.State is "released" or "trashed" or "pending" or "retryable" or "reviewRequired")
                throw new CloudProviderException("CLOUD_OWNED_COPY_OWNERSHIP_CHANGED");
            if (!await provider.IsOwnedCopyEnabledAsync(request, ct))
            {
                if (row != null) await SaveAsync(row, "retained", null, ct);
                return;
            }
            if (row == null)
            {
                row = new() {DownloadTaskId = id, OperationKey = operation, SourceIdentity = Source(request)};
                db.CloudDownloadOwnedCopies.Add(row);
                // Never submit a restore for an operation already claimed by another native download.
                await db.SaveChangesAsync(ct);
            }
            CloudOwnedCopyReceipt? copy;
            try {copy = await provider.PrepareOwnedCopyAsync(request, ct);}
            catch (CloudProviderException error)
            {
                await SaveAsync(row, "waitingDownload", PublicReason(error.Message, "CLOUD_OWNED_COPY_OWNERSHIP_CHANGED"), CancellationToken.None);
                throw;
            }
            if (copy == null)
            {
                if (row.CopyJson == null) db.CloudDownloadOwnedCopies.Remove(row);
                else {row.State = "retained"; row.ReasonCode = null; row.UpdatedAt = DateTime.UtcNow;}
                await db.SaveChangesAsync(ct);
                return;
            }
            if (new[] {copy.AccountId, copy.AccountVersion, copy.ContainerId, copy.ManifestFingerprint}.Any(string.IsNullOrWhiteSpace) ||
                row.CopyJson != null && JsonSerializer.Deserialize<CloudOwnedCopyReceipt>(row.CopyJson, Json) != copy)
                throw new CloudProviderException("CLOUD_OWNED_COPY_OWNERSHIP_CHANGED");
            row.CopyJson = JsonSerializer.Serialize(copy, Json);
            await SaveAsync(row, "waitingDownload", null, ct);
        }
        finally {gate.Release();}
    }

    // Recording the native download result is already committed. Failing to queue its independent cloud
    // cleanup cannot fail the download; the worker can recover a single matching new result after restart.
    internal async Task DownloadVerifiedAsync(int id, CloudDownloadRequest request, DownloadResultDbModel result)
    {
        try
        {
            var row = await db.CloudDownloadOwnedCopies.SingleOrDefaultAsync(o => o.DownloadTaskId == id, CancellationToken.None);
            if (row is not {CopyJson: not null, State: "waitingDownload"} || row.OperationKey != Operation(request) ||
                row.SourceIdentity != Source(request) || result.DownloadTaskId != id || result.Kind != DownloadResultKind.LocalFiles) return;
            row.ResultId = result.Id; row.ResultKey = ReceiptKey(result);
            await SaveAsync(row, "pending", null, CancellationToken.None);
        }
        catch (Exception error)
        {logger.LogWarning("Could not enqueue temporary cloud copy cleanup for download {TaskId} ({Failure})", id, error.GetType().Name);}
    }

    internal static CloudDownloadOwnedCopyView View(CloudDownloadOwnedCopyDbModel row) => new(row.DownloadTaskId,
        row.State, row.ReasonCode, DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc), row.State == "retryable");

    public async Task<CloudDownloadOwnedCopyView> GetAsync(int id, CancellationToken ct) =>
        await db.CloudDownloadOwnedCopies.AsNoTracking().SingleOrDefaultAsync(o => o.DownloadTaskId == id &&
            db.DownloadTasks.Any(t => t.Id == id), ct) is { } row
            ? View(row) : new(id, "notApplicable", null, null, false);

    public async Task<CloudDownloadOwnedCopyView> RetryAsync(int id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.CloudDownloadOwnedCopies.Where(o => o.DownloadTaskId == id && o.State == "retryable" &&
                db.DownloadTasks.Any(t => t.Id == id && t.Status == DownloadTaskDbModelStatus.Complete))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, "pending").SetProperty(o => o.UpdatedAt, now)
                .SetProperty(o => o.ReasonCode, (string?)null), ct);
        return await GetAsync(id, ct);
    }

    internal async Task RetireDeletedOwnersAsync(CancellationToken ct)
    {
        // Preserve the operation identity so another native task cannot claim an old transfer journal.
        // Deleted tasks are never submitted for release. Drop their now-unusable proof and local receipt binding.
        var orphaned = db.CloudDownloadOwnedCopies.Where(o => o.State != "retired" &&
            !db.DownloadTasks.Any(t => t.Id == o.DownloadTaskId));
        if (await orphaned.AnyAsync(ct))
            await orphaned.ExecuteUpdateAsync(s => s.SetProperty(o => o.State, "retired").SetProperty(o => o.SourceIdentity, "")
                .SetProperty(o => o.CopyJson, (string?)null).SetProperty(o => o.ResultKey, (string?)null)
                .SetProperty(o => o.ResultId, (int?)null).SetProperty(o => o.ReasonCode, (string?)null)
                .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
    }

    internal async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var id = await (from copy in db.CloudDownloadOwnedCopies.AsNoTracking()
            join native in db.DownloadTasks on copy.DownloadTaskId equals native.Id
            where native.Status == DownloadTaskDbModelStatus.Complete && copy.CopyJson != null &&
                (copy.State == "pending" || copy.State == "waitingDownload")
            orderby copy.UpdatedAt, copy.DownloadTaskId
            select (int?)copy.DownloadTaskId).FirstOrDefaultAsync(ct);
        if (!id.HasValue) return false;
        return await services.GetRequiredService<DownloaderManager>().WithTaskActionLock(id.Value, async () =>
        {
            await ReleaseAsync(id.Value, ct);
            return true;
        });
    }

    private async Task ReleaseAsync(int id, CancellationToken ct)
    {
        var observed = await db.CloudDownloadOwnedCopies.AsNoTracking().SingleOrDefaultAsync(o => o.DownloadTaskId == id, ct);
        if (observed == null) return;
        var gate = Gate(observed.OperationKey); await gate.WaitAsync(ct);
        try
        {
            var row = await db.CloudDownloadOwnedCopies.SingleAsync(o => o.DownloadTaskId == id, ct);
            await db.Entry(row).ReloadAsync(ct);
            if (row.State is not ("pending" or "waitingDownload") || row.CopyJson == null) return;
            var native = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (native?.Status != DownloadTaskDbModelStatus.Complete) return;
            try
            {
                var saved = JsonSerializer.Deserialize<CloudDownloadRequest>(native.Options ?? "null", Json);
                if (!IsShare(saved)) {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_DOWNLOAD_CHANGED", ct); return;}
                var request = CloudDownloadDestinations.ExecutionRequest(saved!);
                if (Operation(request) != row.OperationKey || Source(request) != row.SourceIdentity ||
                    CloudSites.Site(request.Source.ProviderId) != native.ThirdPartyId ||
                    !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(request.OutputDirectory), Path.GetFullPath(native.DownloadPath)))
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_DOWNLOAD_CHANGED", ct); return;}
                var copy = JsonSerializer.Deserialize<CloudOwnedCopyReceipt>(row.CopyJson, Json)!;
                var result = await FindResultAsync(row, native, ct);
                if (result == null)
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_RECEIPT_CHANGED", ct); return;}
                // Retain the recovered result binding even if this attempt cannot reach the provider.
                if (row.ResultId == null)
                {
                    row.ResultId = result.Id; row.ResultKey = ReceiptKey(result);
                    await SaveAsync(row, "pending", null, ct);
                }
                var provider = DownloadProviderRouting.Find(services.GetServices<ICloudDownloadProvider>(), request.Source);
                if (provider is not ICloudOwnedCopyProvider owner)
                {await SaveAsync(row, "retryable", "CLOUD_OWNED_COPY_PROVIDER_UNAVAILABLE", ct); return;}
                var files = JsonSerializer.Deserialize<string[]>(result.FilesJson) ?? [];
                storage.EnsurePathAllowed(native.DownloadPath);
                if (!CloudDownloadPayloadRoot.TryValidate(native.DownloadPath, result.DownloadDirectory, files, out var root))
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_LOCAL_CHANGED", ct); return;}
                foreach (var file in files) storage.EnsurePathAllowed(file);
                var verified = new List<CloudVerifiedLocalFile>(files.Length);
                ArtifactManifest manifest;
                try
                {
                    manifest = await ArtifactManifestFiles.CaptureWithFileObservationsAsync(root, files, (path, entry, written, generation) =>
                        verified.Add(new(path, entry.Length, entry.Sha256) {LastWriteTimeUtc = written, ContentGeneration = generation}), ct);
                }
                catch (IOException)
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_LOCAL_CHANGED", ct); return;}
                if (result.Fingerprint != manifest.Fingerprint)
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_LOCAL_CHANGED", ct); return;}
                var local = new CloudVerifiedLocalDownload(id, result.Id, native.DownloadPath, files, manifest.Fingerprint)
                {
                    VerifiedFiles = verified
                };
                // Re-read the owner and receipt after potentially lengthy file verification. The adapter
                // checks the live account policy and its durable remote identity immediately before release.
                if (!await db.DownloadTasks.AsNoTracking().AnyAsync(t => t.Id == id && t.Status == DownloadTaskDbModelStatus.Complete &&
                        t.Options == native.Options && t.DownloadPath == native.DownloadPath, ct) ||
                    !await db.DownloadResults.AsNoTracking().AnyAsync(r => r.Id == result.Id && r.Fingerprint == result.Fingerprint &&
                        r.DownloadDirectory == result.DownloadDirectory && r.FilesJson == result.FilesJson, ct))
                {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_RECEIPT_CHANGED", ct); return;}
                var release = await owner.ReleaseOwnedCopyAsync(request, copy, local, ct);
                if (release.Status is not ("retained" or "released" or "trashed" or "retryable" or "reviewRequired"))
                {await SaveAsync(row, "retryable", "CLOUD_OWNED_COPY_RELEASE_FAILED", ct); return;}
                await SaveAsync(row, release.Status, release.ReasonCode == null ? null :
                    PublicReason(release.ReasonCode, "CLOUD_OWNED_COPY_RELEASE_FAILED"), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {throw;}
            catch (Exception error) when (error is InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {await SaveAsync(row, "reviewRequired", "CLOUD_OWNED_COPY_LOCAL_CHANGED", ct);}
            catch (Exception error)
            {
                logger.LogWarning("Could not release temporary cloud copy for download {TaskId} ({Failure})", id, error.GetType().Name);
                await SaveAsync(row, "retryable", "CLOUD_OWNED_COPY_RELEASE_FAILED", ct);
            }
        }
        finally {gate.Release();}
    }

    private async Task<DownloadResultDbModel?> FindResultAsync(CloudDownloadOwnedCopyDbModel row, DownloadTaskDbModel native,
        CancellationToken ct)
    {
        var query = db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == native.Id &&
            r.ThirdPartyId == native.ThirdPartyId && r.SourceKey == native.Key && r.Kind == DownloadResultKind.LocalFiles);
        if (row.ResultId is { } id)
        {
            var bound = await query.SingleOrDefaultAsync(r => r.Id == id, ct);
            return bound != null && ReceiptKey(bound) == row.ResultKey ? bound : null;
        }
        // A crash can land between the native receipt commit and linking it here. An earlier
        // download is never eligible, and several candidate receipts require manual review.
        var fresh = await query.Where(r => r.CreatedAt >= row.UpdatedAt).Take(2).ToListAsync(ct);
        return fresh.Count == 1 ? fresh[0] : null;
    }

    private async Task SaveAsync(CloudDownloadOwnedCopyDbModel row, string state, string? reason, CancellationToken ct)
    {
        row.State = state; row.ReasonCode = reason; row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
