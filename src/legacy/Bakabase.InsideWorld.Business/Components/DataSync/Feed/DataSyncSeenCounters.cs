using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>
/// The head's <c>SeenCounter</c> (§7.5.1 step 4, §5.6): per actor, the highest counter found in any vector this
/// device stores — entities, tombstones, bases and pending records. A reader compares the value for its own declared
/// actor with its own counter, so a device restored behind its readers' backs learns that others have seen counters
/// it no longer has.
/// </summary>
/// <remarks>
/// <para>
/// It is an in-memory high-water map: rebuilt from the database on first use after a start, and maintained on every
/// save of a data sync context (<see cref="Attach"/>, which every <see cref="DataSyncStore"/> calls for its context),
/// so a head never scans the tables. Both only ever raise a value, so the rebuild and the saves may interleave in any
/// order. A value is never lowered while the process runs, even when the vector that carried it is deleted later
/// (a reset link's bases): it is still a counter this device has seen.
/// </para>
/// <para>
/// A save is observed when it is written, not when its transaction commits. That cannot overstate anything: every
/// counter of another device's actor in a vector here was issued by that device and received in one of its records,
/// whether or not the transaction that stored it commits.
/// </para>
/// </remarks>
public sealed class DataSyncSeenCounters
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, long> _highest = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private volatile bool _built;

    public DataSyncSeenCounters(IServiceScopeFactory scopes, ILogger<DataSyncSeenCounters>? logger = null)
    {
        _scopes = scopes;
        _logger = logger ?? (ILogger) NullLogger.Instance;
    }

    /// <summary>
    /// The highest counter of <paramref name="actorId"/> in any stored vector; null when no vector names it or no
    /// actor was declared.
    /// </summary>
    public async Task<long?> GetAsync(string? actorId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(actorId)) return null;
        await EnsureBuiltAsync(ct);
        return _highest.TryGetValue(actorId, out var counter) ? counter : null;
    }

    /// <summary>Raises the high-water mark of every actor of <paramref name="vv"/>.</summary>
    public void Observe(DataSyncVersionVector vv)
    {
        ArgumentNullException.ThrowIfNull(vv);
        foreach (var (actor, counter) in vv.Counters)
        {
            _highest.AddOrUpdate(actor, counter, (_, known) => Math.Max(known, counter));
        }
    }

    /// <summary>
    /// Observes every vector <paramref name="db"/> writes to the data sync tables from now on: new and changed entity
    /// vectors, base vectors and pending records. Never throws from the save.
    /// </summary>
    public void Attach(DbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        List<DataSyncVersionVector>? saving = null;
        db.SavingChanges += (_, _) => saving = Collect(db);
        db.SavedChanges += (_, _) =>
        {
            var saved = saving;
            saving = null;
            if (saved is null) return;
            foreach (var vv in saved) Observe(vv);
        };
        db.SaveChangesFailed += (_, _) => saving = null;
    }

    /// <summary>Forgets what was built, so the next read scans the database again (a start, in tests).</summary>
    internal void Reset()
    {
        _built = false;
        _highest.Clear();
    }

    private List<DataSyncVersionVector>? Collect(DbContext db)
    {
        List<DataSyncVersionVector>? vectors = null;
        try
        {
            foreach (var entry in db.ChangeTracker.Entries<DataSyncEntityDbModel>())
            {
                if (IsWritten(entry, e => e.VvJson)) Add(ref vectors, entry.Entity.VvJson);
            }

            foreach (var entry in db.ChangeTracker.Entries<DataSyncPeerBaseDbModel>())
            {
                if (IsWritten(entry, b => b.VvJson)) Add(ref vectors, entry.Entity.VvJson);
                if (IsWritten(entry, b => b.PendingRecordJson) && entry.Entity.PendingRecordJson is { } pending &&
                    DataSyncStoredJson.ReadRecordVv(pending) is { } pendingVv)
                {
                    (vectors ??= []).Add(pendingVv);
                }
            }
        }
        catch (Exception e)
        {
            // A vector that does not parse fails its own readers loudly; observing is never a reason to fail a save.
            _logger.LogWarning(e, "Data sync could not observe the vectors of a save.");
        }

        return vectors;
    }

    private static bool IsWritten<T>(EntityEntry<T> entry, System.Linq.Expressions.Expression<Func<T, string?>> property)
        where T : class =>
        entry.State == EntityState.Added || (entry.State == EntityState.Modified && entry.Property(property).IsModified);

    private static void Add(ref List<DataSyncVersionVector>? vectors, string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        (vectors ??= []).Add(DataSyncVersionVector.ParseStored(json));
    }

    private async Task EnsureBuiltAsync(CancellationToken ct)
    {
        if (_built) return;
        await _buildLock.WaitAsync(ct);
        try
        {
            if (_built) return;
            await using var scope = _scopes.CreateAsyncScope();
            var (vectors, pendingRecords) =
                await scope.ServiceProvider.GetRequiredService<DataSyncStore>().ReadStoredVectorsAsync(ct);
            var unreadable = 0;
            foreach (var json in vectors)
            {
                if (TryParse(() => DataSyncVersionVector.ParseStored(json)) is { } vv) Observe(vv);
                else unreadable++;
            }

            foreach (var json in pendingRecords)
            {
                if (TryParse(() => DataSyncStoredJson.ReadRecordVv(json)) is { } vv) Observe(vv);
                else unreadable++;
            }

            if (unreadable > 0)
                _logger.LogWarning("Data sync skipped {Count} stored vectors it could not read.", unreadable);
            _built = true;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private static DataSyncVersionVector? TryParse(Func<DataSyncVersionVector?> parse)
    {
        try
        {
            return parse();
        }
        catch (System.IO.InvalidDataException)
        {
            return null;
        }
    }
}
