using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

public partial class DownloadTaskService
{
    /// <summary>Completes only an idle original failed task after provider content proof and a second
    /// local SHA capture. Holds the same lock as start/edit/stop; there is no downloader invocation.</summary>
    public Task VerifyAndCompleteExistingFilesAsync(int id,
        Func<CancellationToken, Task<CloudExistingDownloadVerification>> verify,
        Func<CancellationToken, Task> validate, CancellationToken ct) =>
        DownloaderManager.WithTaskActionLock(id, async () =>
        {
            var db = GetRequiredService<BakabaseDbContext>();
            var task = await db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            if (task.Status != DownloadTaskDbModelStatus.Failed || task.CompletedAt != null ||
                DownloaderManager[id]?.IsOccupyingDownloadTaskSource() == true)
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            await validate(ct);
            var proof = await verify(ct);
            await GetRequiredService<DownloadResultService>().RecordVerifiedExistingFilesAsync(task, proof, validate, ct);
            DownloaderManager.Forget(id);
            return true;
        });
}
