using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Release runs after durable native completion, independent of both downloader slots and acquisition workflows.</summary>
public sealed class CloudDownloadOwnedCopyWorker(IServiceScopeFactory scopes, ILogger<CloudDownloadOwnedCopyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRetirement = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var copies = scope.ServiceProvider.GetRequiredService<CloudDownloadOwnedCopyService>();
                if (DateTime.UtcNow >= nextRetirement)
                {
                    await copies.RetireDeletedOwnersAsync(stoppingToken);
                    nextRetirement = DateTime.UtcNow.AddHours(1);
                }
                if (await copies.ProcessNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
            catch (Exception error) {logger.LogWarning(error, "Could not check pending temporary cloud copies");}
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
