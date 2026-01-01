using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components
{
    /// <summary>
    /// Background service that periodically checks for expired path marks
    /// and marks them as pending for re-synchronization.
    /// </summary>
    public class PathMarkExpirationCheckJob : BackgroundService
    {
        private readonly ILogger<PathMarkExpirationCheckJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);

        public PathMarkExpirationCheckJob(
            ILogger<PathMarkExpirationCheckJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PathMarkExpirationCheckJob started. Check interval: {Interval} seconds", _checkInterval.TotalSeconds);

            // Wait a bit before starting to allow the application to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckExpiredMarks(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Expected when shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while checking for expired path marks");
                }

                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("PathMarkExpirationCheckJob stopped");
        }

        private async Task CheckExpiredMarks(CancellationToken ct)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var pathMarkService = scope.ServiceProvider.GetRequiredService<IPathMarkService>();
            var pathMarkSyncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
            var resourceOptions = scope.ServiceProvider.GetRequiredService<IBOptions<ResourceOptions>>();

            // Get all synced marks that have expiration configured
            var allMarks = await pathMarkService.GetBySyncStatus(PathMarkSyncStatus.Synced);
            var now = DateTime.UtcNow;
            var expiredMarkIds = new List<int>();

            foreach (var mark in allMarks)
            {
                ct.ThrowIfCancellationRequested();

                // Skip marks without expiration
                if (!mark.ExpiresInSeconds.HasValue || mark.ExpiresInSeconds.Value <= 0)
                    continue;

                // Skip marks that haven't been synced yet
                if (!mark.SyncedAt.HasValue)
                    continue;

                // Check if expired: SyncedAt + ExpiresInSeconds < Now
                var expirationTime = mark.SyncedAt.Value.AddSeconds(mark.ExpiresInSeconds.Value);
                if (expirationTime < now)
                {
                    _logger.LogDebug("Mark {MarkId} on path {Path} has expired (synced at {SyncedAt}, expires after {ExpiresInSeconds}s)",
                        mark.Id, mark.Path, mark.SyncedAt, mark.ExpiresInSeconds);
                    expiredMarkIds.Add(mark.Id);
                }
            }

            if (expiredMarkIds.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Found {Count} expired path marks, marking as pending", expiredMarkIds.Count);

            // Mark expired marks as pending
            foreach (var markId in expiredMarkIds)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await pathMarkService.MarkAsPending(markId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to mark expired mark {MarkId} as pending", markId);
                }
            }

            // If SyncMarksImmediately is enabled, trigger sync
            if (resourceOptions.Value.SynchronizationOptions?.SyncMarksImmediately == true)
            {
                _logger.LogInformation("SyncMarksImmediately is enabled, triggering sync for {Count} expired marks", expiredMarkIds.Count);
                try
                {
                    await pathMarkSyncService.EnqueueSync(expiredMarkIds.ToArray());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start immediate sync for expired marks");
                }
            }
        }
    }
}
