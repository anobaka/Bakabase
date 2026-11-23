using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

public partial class DownloadTaskService
{
    /// <summary>Completes only an idle original failed or explicitly selected stopped task after provider content proof and a second
    /// local SHA capture. Holds the same lock as start/edit/stop; there is no downloader invocation.</summary>
    public Task VerifyAndCompleteExistingFilesAsync(int id,
        Func<CancellationToken, Task<CloudExistingDownloadVerification>> verify,
        Func<CancellationToken, Task> validate, CancellationToken ct, bool allowStopped = false,
        Func<Func<Task>, Task>? guardRegistration = null, int? workflowRunId = null) =>
        DownloaderManager.WithTaskActionLock(id, async () =>
        {
            var db = GetRequiredService<BakabaseDbContext>();
            var task = await db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            if ((task.Status != DownloadTaskDbModelStatus.Failed &&
                 !(allowStopped && task.Status == DownloadTaskDbModelStatus.Disabled)) || task.CompletedAt != null ||
                DownloaderManager[id]?.IsOccupyingDownloadTaskSource() == true)
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            await validate(ct);
            try
            {
                await DownloaderManager.SetFileVerificationAsync(id, new(DownloadFileVerification.Source, workflowRunId, DateTimeOffset.UtcNow));
                var proof = await verify(ct);
                await DownloaderManager.SetFileVerificationAsync(id, new(DownloadFileVerification.Local, workflowRunId, DateTimeOffset.UtcNow));
                await GetRequiredService<DownloadResultService>().RecordVerifiedExistingFilesAsync(task, proof, validate, ct, guardRegistration);
                DownloaderManager.Forget(id);
                return true;
            }
            finally { await DownloaderManager.SetFileVerificationAsync(id, null); }
        });
}
