using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// Retention (§4.6), run by the <c>DataSync</c> task once a day, under the gate, in its own short transaction: the
/// store's rules (<see cref="DataSyncStore.PruneAsync"/>: unserved tombstones and per-kind floors, closed items, apply
/// logs, readers, retired actors), then the data sync database backups beyond the newest five. The runtime's fetch task
/// calls it once a day as its <see cref="IDataSyncRetention"/>.
/// </summary>
public sealed class DataSyncRetention : IDataSyncRetention
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>The data sync database backups kept (§4.6, §8.10.4).</summary>
    public const int BackupsKept = 5;

    /// <summary><c>{BackupsPath}/data-sync-{yyyyMMdd-HHmmss}.db</c> (§4.7); the name sorts by time.</summary>
    public const string BackupSearchPattern = "data-sync-*.db";

    private readonly DataSyncGate _gate;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataSyncDataDirectory _directory;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private DateTimeOffset? _lastRunAt;

    public DataSyncRetention(DataSyncGate gate, IServiceScopeFactory scopes, IDataSyncDataDirectory directory,
        TimeProvider? time = null, ILogger<DataSyncRetention>? logger = null)
    {
        _gate = gate;
        _scopes = scopes;
        _directory = directory;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger) NullLogger.Instance;
    }

    /// <summary>Runs retention when it did not run in this process within <see cref="Interval"/>. Returns whether it ran.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken ct)
    {
        if (_lastRunAt is { } last && _time.GetUtcNow() - last < Interval) return false;
        await RunAsync(ct);
        return true;
    }

    /// <summary>Retention now. A task body waits for the gate without a limit.</summary>
    public Task RunAsync(CancellationToken ct) => RunAsync(_time.GetUtcNow().UtcDateTime, ct);

    /// <summary>Retention as of <paramref name="nowUtc"/> (the caller's clock). A task body waits for the gate without a limit.</summary>
    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var ranAt = _time.GetUtcNow();
        using (await _gate.EnterAsync(null, ct))
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
            await using var transaction = await store.Db.Database.BeginTransactionAsync(ct);
            await store.PruneAsync(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), ct);
            await transaction.CommitAsync(ct);
        }

        var pruned = PruneBackups(_directory.BackupsPath);
        if (pruned > 0) _logger.LogInformation("Data sync removed {Count} old database backups.", pruned);
        _lastRunAt = ranAt;
    }

    /// <summary>
    /// Deletes every data sync backup in <paramref name="backupsPath"/> but the newest <paramref name="keep"/>; other
    /// files there (the app's own upgrade backups) are never touched. Returns how many were deleted.
    /// </summary>
    public static int PruneBackups(string backupsPath, int keep = BackupsKept)
    {
        ArgumentException.ThrowIfNullOrEmpty(backupsPath);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        if (!Directory.Exists(backupsPath)) return 0;
        var stale = Directory.EnumerateFiles(backupsPath, BackupSearchPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();
        foreach (var file in stale) File.Delete(file);
        return stale.Count;
    }
}
