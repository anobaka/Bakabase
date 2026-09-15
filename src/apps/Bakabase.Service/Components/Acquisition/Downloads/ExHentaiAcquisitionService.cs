using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Downloads;

/// <summary>The existing queue remains responsible for platform authentication and request pacing.</summary>
public interface IExHentaiAcquisitionQueue
{
    Task<DownloadTaskDbModel> BuildAsync(string sourceKey, string? name, string directory, CancellationToken ct);
    Task StartAsync(int downloadTaskId, CancellationToken ct);
    Task StopAsync(int downloadTaskId, CancellationToken ct);
}

public sealed class ExHentaiAcquisitionQueue(ExHentaiDownloaderHelper helper, DownloadTaskService downloads)
    : IExHentaiAcquisitionQueue
{
    public async Task<DownloadTaskDbModel> BuildAsync(string sourceKey, string? name, string directory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tasks = await helper.BuildTasks(new DownloadTaskAddInputModel
        {
            ThirdPartyId = ThirdPartyId.ExHentai,
            Type = (int) ExHentaiDownloadTaskType.SingleWork,
            Keys = [$"https://exhentai.org/g/{sourceKey}/"],
            Names = string.IsNullOrWhiteSpace(name) ? null : [name],
            DownloadPath = directory,
            Options = """{"downloadResultWorkflowId":null}"""
        });
        if (tasks.Length != 1) throw new InvalidOperationException("The ExHentai downloader refused this gallery.");
        var task = tasks[0].ToDbModel()!;
        task.DownloadPath = directory;
        task.Status = DownloadTaskDbModelStatus.Disabled;
        return task;
    }

    public async Task StartAsync(int downloadTaskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Targeted starts must produce a structured result even when an older torrent is cached.
        var response = await downloads.Start(t => t.Id == downloadTaskId, targeted: true);
        if (response.Code != (int) ResponseCode.Success && response.Code != (int) ResponseCode.Conflict)
            throw new InvalidOperationException(response.Message ?? "The ExHentai download could not start.");
    }

    public async Task StopAsync(int downloadTaskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await downloads.Stop(t => t.Id == downloadTaskId);
    }
}

public record ExHentaiAcquisitionState(int DownloadTaskId, DownloadResultDbModel? Result = null, string? Error = null);

/// <summary>
/// Correlates a platform task with its acquisition before that task may run. Results belong to the
/// original run; the independent result dispatcher must never claim an owned task.
/// </summary>
public sealed class ExHentaiAcquisitionService(BakabaseDbContext db, IExHentaiAcquisitionQueue queue)
{
    public async Task<ExHentaiAcquisitionState> StartAsync(int acquisitionTaskId, int workflowRunId,
        int resourceId, string sourceKey, string? name, string workingDirectory, CancellationToken ct)
    {
        await EnsureActive(acquisitionTaskId, resourceId, ct);
        var owner = await db.Set<DownloadResultOwnerDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.AcquisitionTaskId == acquisitionTaskId, ct);
        if (owner == null)
        {
            var download = await queue.BuildAsync(sourceKey, name,
                Path.Combine(workingDirectory, "platform"), ct);
            // Neither the automatic queue nor result dispatcher can see a runnable, unowned row.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Set<DownloadTaskDbModel>().Add(download);
            await db.SaveChangesAsync(ct);
            owner = new DownloadResultOwnerDbModel
            {
                DownloadTaskId = download.Id, AcquisitionTaskId = acquisitionTaskId,
                WorkflowRunId = workflowRunId, ResourceId = resourceId
            };
            db.Set<DownloadResultOwnerDbModel>().Add(owner);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        if (owner.ResourceId != resourceId || owner.WorkflowRunId != workflowRunId)
            throw new InvalidOperationException("The platform download belongs to a different acquisition run.");
        var task = await db.Set<DownloadTaskDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == owner.DownloadTaskId, ct);
        // Execute is called again on an explicit retry. Resume only observes and never restarts a stopped task.
        if (task?.Status is DownloadTaskDbModelStatus.Disabled or DownloadTaskDbModelStatus.Failed)
        {
            await EnsureActive(acquisitionTaskId, resourceId, ct);
            await queue.StartAsync(owner.DownloadTaskId, ct);
        }
        return await GetAsync(acquisitionTaskId, resourceId, ct)
               ?? new ExHentaiAcquisitionState(owner.DownloadTaskId, Error: "The platform download disappeared.");
    }

    public async Task<ExHentaiAcquisitionState?> GetAsync(int acquisitionTaskId, int resourceId,
        CancellationToken ct)
    {
        var owner = await db.Set<DownloadResultOwnerDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.AcquisitionTaskId == acquisitionTaskId, ct);
        if (owner == null) return null;
        if (owner.ResourceId != resourceId)
            return new(owner.DownloadTaskId, Error: "The platform result belongs to a different resource.");
        var result = await db.Set<DownloadResultDbModel>().AsNoTracking()
            .Where(r => r.DownloadTaskId == owner.DownloadTaskId).OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
        if (result != null) return new(owner.DownloadTaskId, result);
        var task = await db.Set<DownloadTaskDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == owner.DownloadTaskId, ct);
        return new(owner.DownloadTaskId, Error: task?.Status switch
        {
            null => "The platform download was deleted.",
            DownloadTaskDbModelStatus.Failed => task.Message ?? "The ExHentai download failed.",
            DownloadTaskDbModelStatus.Disabled => "The ExHentai download was stopped.",
            DownloadTaskDbModelStatus.Complete => "The platform download completed without a usable result.",
            _ => null
        });
    }

    public async Task StopAbandonedAsync(CancellationToken ct)
    {
        var owned = await (from owner in db.Set<DownloadResultOwnerDbModel>()
            join task in db.Set<DownloadTaskDbModel>() on owner.DownloadTaskId equals task.Id
            where task.Status == DownloadTaskDbModelStatus.InProgress
            select owner).AsNoTracking().ToListAsync(ct);
        foreach (var owner in owned)
        {
            var parent = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == owner.AcquisitionTaskId, ct);
            if (parent == null || parent.Status is AcquisitionStatus.Cancelled or AcquisitionStatus.Failed)
                await queue.StopAsync(owner.DownloadTaskId, ct);
        }
    }

    private async Task EnsureActive(int taskId, int resourceId, CancellationToken ct)
    {
        var parent = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == taskId && a.ResourceId == resourceId, ct);
        if (parent == null || parent.Status is AcquisitionStatus.Cancelled or AcquisitionStatus.Completed)
            throw new InvalidOperationException("The acquisition is no longer active.");
    }
}
