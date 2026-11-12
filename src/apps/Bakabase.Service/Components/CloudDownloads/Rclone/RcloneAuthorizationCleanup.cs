using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>Remove unfinished authorization credentials after expiry, including across host restarts.</summary>
internal sealed class RcloneAuthorizationCleanup(RcloneAuthorizationService authorization) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            try { await authorization.CleanupExpiredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) when (error is System.IO.IOException or RcloneRemoteException) { }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        } while (!stoppingToken.IsCancellationRequested);
    }
}
