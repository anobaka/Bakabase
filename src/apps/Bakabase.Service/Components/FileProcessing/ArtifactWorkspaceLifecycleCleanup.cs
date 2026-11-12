using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Bounded serial compensation for completion cleanup skipped by an active consumer.
/// Durable registrations and manifests are the queue; no workflow-specific callback is required.</summary>
public sealed class ArtifactWorkspaceLifecycleCleanup(IServiceScopeFactory scopes,
    ILogger<ArtifactWorkspaceLifecycleCleanup> logger) : BackgroundService
{
    private readonly TimeProvider clock = TimeProvider.System;
    private string? afterId;
    private string? afterPublicationId;

    internal ArtifactWorkspaceLifecycleCleanup(IServiceScopeFactory scopes,
        ILogger<ArtifactWorkspaceLifecycleCleanup> logger, TimeProvider clock) : this(scopes, logger)
    {
        this.clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastIdlePass = clock.GetTimestamp();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Defer the first pass too: startup must finish resuming existing jobs first.
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                // Durable completion intents take priority over the historical keyset sweep.
                // They are visible only after the workflow's successful commit.
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var cleanup = scope.ServiceProvider.GetService<PublishedArtifactAncestorCleanup>();
                    if (cleanup != null)
                        foreach (var id in await cleanup.GetRequestedIdsAsync(stoppingToken))
                            await cleanup.ReclaimRequestedAsync(id, stoppingToken);
                }
                if (clock.GetElapsedTime(lastIdlePass) >= TimeSpan.FromMinutes(5))
                {
                    await RunIdlePassAsync(stoppingToken);
                    lastIdlePass = clock.GetTimestamp();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning(error, "Could not reclaim completed file-processing snapshots"); }
        }
    }

    internal async Task RunIdlePassAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var ids = await maintenance.GetCandidateIdsAsync(afterId, 2, stoppingToken);
        if (ids.Count == 0) afterId = null;
        var startedAt = clock.GetTimestamp();
        foreach (var id in ids)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(startedAt) >= TimeSpan.FromMinutes(5)) return;
            // Per-artifact consumer and registration fences protect live readers. Unrelated
            // downloads must not starve cleanup indefinitely and fill the shared storage.
            // Only one candidate hashes at a time; a large verification may finish, while
            // the budget prevents starting another. Shutdown remains cancellable.
            afterId = id;
            await maintenance.ReclaimAsync([id], stoppingToken);
        }
        // Registered checkpoints are intentionally outside private-workspace cleanup. Once
        // a publication exists, retire them through the independent lineage/owner proof.
        // Keep one publication and at most two registered inputs per pass to bound hashing.
        var ancestors = scope.ServiceProvider.GetService<PublishedArtifactAncestorCleanup>();
        if (ancestors == null || clock.GetElapsedTime(startedAt) >= TimeSpan.FromMinutes(5)) return;
        var publications = await ancestors.GetCandidateIdsAsync(afterPublicationId, 1, stoppingToken);
        if (publications.Count == 0) { afterPublicationId = null; return; }
        foreach (var publication in publications)
        {
            afterPublicationId = publication;
            if (!(await ancestors.PreparePublicationOwnersAsync(publication, stoppingToken)).Ready) continue;
            var owners = await ancestors.PublicationOwnersAsync(publication, stoppingToken);
            var candidates = await ancestors.PreviewAsync([publication], owners, stoppingToken);
            var count = 0;
            foreach (var candidate in candidates)
            {
                if (!candidate.Eligible) continue;
                if (count++ >= 2 || clock.GetElapsedTime(startedAt) >= TimeSpan.FromMinutes(5)) break;
                await ancestors.ReclaimAutomaticAsync(candidate, owners, stoppingToken);
            }
        }
    }
}
