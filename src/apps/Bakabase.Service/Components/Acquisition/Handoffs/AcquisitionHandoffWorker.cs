using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

public sealed class AcquisitionHandoffWorker(IServiceScopeFactory scopes,
    ILogger<AcquisitionHandoffWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<PostAcquisitionHandoffService>().ProcessNextAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
            catch (Exception) {logger.LogWarning("Acquisition handoff maintenance is temporarily unavailable.");}
            try {await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);}
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
        }
    }
}
