using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// Headless setup for a NAS or container, where nobody may be at a window (spec §7.8):
/// <c>BAKABASE_DATASYNC_SHARING=true</c> turns definitions sharing on at <b>every</b> start, together with remote access
/// with pairing required — but only when remote access is Disabled, so a Docker install's Unrestricted mode is never
/// touched (§7.1.3). Turning sharing off in the UI therefore lasts only until the next restart while it is set.
/// </summary>
public sealed class DataSyncSharingAnnouncer(IServiceScopeFactory scopes, ILogger<DataSyncSharingAnnouncer> logger)
    : IHostedService
{
    public const string SharingVariable = "BAKABASE_DATASYNC_SHARING";

    /// <summary>Where the variable is read from; the process environment unless a test says otherwise.</summary>
    internal Func<string, string?> ReadVariable { get; init; } = Environment.GetEnvironmentVariable;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!bool.TryParse(ReadVariable(SharingVariable), out var enable) || !enable) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var grants = scope.ServiceProvider.GetService<IDataSyncGrantService>();
            if (grants is null)
            {
                Console.WriteLine($"{SharingVariable} is set, but this build cannot share definitions.");
                return;
            }

            await grants.SetSharingEnabledAsync(true, enablePairedRemoteAccess: true, ct);
            Console.WriteLine($"Bakabase definitions sharing enabled by {SharingVariable}.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A damaged sharing state must not stop the whole service from starting.
            logger.LogError(e, "Definitions sharing could not be turned on");
            Console.WriteLine($"Definitions sharing unavailable: {e.Message}");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
