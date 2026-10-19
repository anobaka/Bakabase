using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Startup runs after AppData initialization; shutdown drains HTTP transfer writers.</summary>
public sealed class HttpDownloadLifetime(IHttpDownloadManager downloads) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => downloads.ShutdownAsync(ct);
}
