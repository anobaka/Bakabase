using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Components;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Connects platform results to the independent BT capability. No workflow is needed.</summary>
public sealed class DownloadResultBtService(BakabaseDbContext db, IBtDownloadManager downloads,
    FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int> galleryCache)
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<BtDownloadJob> EnsureAsync(DownloadResultDbModel result, int? workflowRunId,
        int? workflowDefinitionId, int timeoutMinutes, CancellationToken ct)
    {
        var existing = await downloads.FindByIdempotencyKeyAsync($"download-result:{result.Id}", ct);
        if (existing?.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running or BtDownloadStatus.Stopping)
            return existing;
        await using var stream = File.OpenRead(result.Path);
        var bytes = await TorrentMetadata.ReadBoundedAsync(stream, ct);
        TorrentMetadata.Validate(bytes);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), result.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The saved torrent metadata changed. Acquire it again before downloading.");
        var directory = result.DownloadDirectory;
        if (!result.RequiresContentsDownload)
        {
            // Preserve historical workflow output locations, including their retry pieces.
            var name = string.Concat(result.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c)).Trim();
            if (name.Length > 100) name = name[..100];
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            directory = Path.Combine(directory, $"{name} [{result.Id}]", "torrent-data");
        }
        return await downloads.EnsureQueuedAsync(new BtDownloadRequest
        {
            Name = result.Name, Metadata = bytes, OutputDirectory = existing?.OutputDirectory ?? directory,
            Timeout = TimeSpan.FromMinutes(timeoutMinutes), IdempotencyKey = $"download-result:{result.Id}",
            SourceKind = "download-result", SourceId = result.Id.ToString(),
            DownloadResultId = result.Id, DownloadTaskId = result.DownloadTaskId,
            WorkflowRunId = existing == null ? workflowRunId : existing.WorkflowRunId,
            WorkflowDefinitionId = existing == null ? workflowDefinitionId : existing.WorkflowDefinitionId
        }, ct);
    }

    public async Task BindAsync(int resultId, BtDownloadJob job, CancellationToken ct)
    {
        var state = await db.DownloadResultProcessing.FindAsync([resultId], ct);
        if (state == null)
        {
            state = new DownloadResultProcessingDbModel {DownloadResultId = resultId};
            db.DownloadResultProcessing.Add(state);
        }
        state.BtDownloadId = job.Id;
        state.BtDispatchError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var rows = await db.DownloadResults.AsNoTracking().Where(r => r.RequiresContentsDownload &&
                r.Kind == DownloadResultKind.TorrentMetadata &&
                !db.DownloadResultOwners.Any(o => o.DownloadTaskId == r.DownloadTaskId) &&
                db.Set<DownloadTaskDbModel>().Any(t => t.Id == r.DownloadTaskId && t.Status != DownloadTaskDbModelStatus.Disabled) &&
                !db.DownloadResults.Any(newer => newer.DownloadTaskId == r.DownloadTaskId && newer.SourceKey == r.SourceKey && newer.Id > r.Id))
                .ToListAsync(ct);
            var taskIds = rows.Select(r => r.DownloadTaskId).Distinct().ToArray();
            var sourceTasks = await db.Set<DownloadTaskDbModel>().AsNoTracking().Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);
            foreach (var result in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (!DownloadTaskExecutionLifecycle.CanContinue(sourceTasks[result.DownloadTaskId], result)) continue;
                try
                {
                    var job = await downloads.FindByIdempotencyKeyAsync($"download-result:{result.Id}", ct)
                              ?? await EnsureAsync(result, null, null, 240, ct);
                    await BindAsync(result.Id, job, ct);
                    if (job.Status == BtDownloadStatus.Completed) await RecordCompletionAsync(result, job, ct);
                }
                catch (OperationCanceledException) {throw;}
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
                {
                    var state = await db.DownloadResultProcessing.FindAsync([result.Id], ct);
                    if (state == null) db.DownloadResultProcessing.Add(state = new() {DownloadResultId = result.Id});
                    state.BtDispatchError = ex.Message;
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        finally {Gate.Release();}
    }

    public async Task RecordCompletionAsync(DownloadResultDbModel result, BtDownloadJob job, CancellationToken ct)
    {
        var state = await db.DownloadResultProcessing.FindAsync([result.Id], ct)
            ?? throw new InvalidOperationException("The BT result has no execution binding.");
        // Placement can have moved the files. Never overwrite an already recorded content location.
        if (state.ContentsReadyAt.HasValue &&
            DownloadResultContents.Read(state.ContentsDirectory, state.ContentsFilesJson) != null) return;
        var json = JsonSerializer.Serialize(job.Files);
        if (DownloadResultContents.Read(job.OutputDirectory, json) == null)
            throw new IOException("Completed BT files are no longer available at their output directory.");
        state.ContentsDirectory = job.OutputDirectory;
        state.ContentsFilesJson = json;
        state.ContentsReadyAt = DateTime.UtcNow;
        if (result.ThirdPartyId == ThirdPartyId.ExHentai)
        {
            var parts = result.SourceKey.Split('/');
            if (parts.Length == 2 && long.TryParse(parts[0], out var galleryId))
            {
                var gallery = await db.Set<ExHentaiGalleryDbModel>().SingleOrDefaultAsync(g =>
                    g.GalleryId == galleryId && g.GalleryToken == parts[1], ct);
                if (gallery != null)
                {
                    gallery.LocalPath = job.OutputDirectory;
                    gallery.IsDownloaded = true;
                    gallery.UpdatedAt = DateTime.Now;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        galleryCache.ClearCache();
    }
}
