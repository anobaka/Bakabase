using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Downloader;

public sealed class HttpDownloadStartupPolicy(IServiceScopeFactory scopes)
{
    public async Task<bool> CanResumeAsync(HttpDownloadJob job, CancellationToken ct)
    {
        if (job.SourceKind != "manual" || job.WorkflowRunId != null || job.DownloadTaskId != null) return false;
        await using var scope = scopes.CreateAsyncScope();
        return !await scope.ServiceProvider.GetRequiredService<IDownloadTaskRegistry>()
            .IsUserStoppedAsync("http:" + job.Id, ct);
    }
}
