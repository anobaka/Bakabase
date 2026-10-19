using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Bittorrent.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Initialization happens after AppData is ready; shutdown drains transfer writers.</summary>
public sealed class BtDownloadLifetime(IBtDownloadManager downloads) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => downloads.ShutdownAsync(ct);
}
