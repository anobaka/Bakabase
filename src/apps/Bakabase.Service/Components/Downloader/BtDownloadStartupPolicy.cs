using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Bittorrent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Reconciles durable transfer intent with the source before the BT scheduler starts.</summary>
public sealed class BtDownloadStartupPolicy(IServiceScopeFactory scopes)
{
    public async Task<bool> CanResumeAsync(BtDownloadJob job, CancellationToken ct)
    {
        // A workflow owns the continuation. Its adapter explicitly resumes the same transfer only
        // after workflow recovery has validated its cursor and enabled definition.
        if (job.SourceKind == "workflow" || job.WorkflowRunId != null) return false;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        if (await db.ManagedDownloadTasks.AnyAsync(t => t.Id == "bt:" + job.Id && t.UserStopped, ct)) return false;
        if (job.SourceKind == "manual") return true;
        if (job.DownloadResultId is not { } resultId) return false;
        var result = await db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resultId, ct);
        if (result == null || !result.RequiresContentsDownload) return false;
        var source = await db.Set<DownloadTaskDbModel>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == result.DownloadTaskId, ct);
        return source != null && source.Status != DownloadTaskDbModelStatus.Disabled &&
               DownloadTaskExecutionLifecycle.CanContinue(source, result) &&
               !await db.DownloadResultOwners.AnyAsync(o => o.DownloadTaskId == source.Id, ct) &&
               !await db.DownloadResults.AnyAsync(r => r.DownloadTaskId == source.Id && r.SourceKey == result.SourceKey && r.Id > result.Id, ct);
    }
}
