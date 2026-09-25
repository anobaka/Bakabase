using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// One attempt of a runner method (§8.10.2, §8.10.5): its own scope, so every write — the adapters' services and the
/// side tables alike — goes through the scope's one <see cref="BakabaseDbContext"/> and joins its transaction. On a
/// rollback the scope's change tracker is cleared and the caches of every kind the attempt touched are dropped.
/// </summary>
internal sealed class DataSyncApplySession : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private IDbContextTransaction? _transaction;

    private DataSyncApplySession(AsyncServiceScope scope, DataSyncDevice device)
    {
        _scope = scope;
        Services = scope.ServiceProvider;
        Store = Services.GetRequiredService<DataSyncStore>();
        Identity = Services.GetRequiredService<DataSyncIdentityStore>();
        Reader = Services.GetRequiredService<DataSyncLocalStateReader>();
        Refresher = Services.GetRequiredService<DataSyncRefresher>();
        Limits = Services.GetService<DataSyncLimits>() ?? DataSyncLimits.Default;
        Device = device;
    }

    public IServiceProvider Services { get; }
    public DataSyncStore Store { get; }
    public DataSyncIdentityStore Identity { get; }
    public DataSyncLocalStateReader Reader { get; }
    public DataSyncRefresher Refresher { get; }
    public DataSyncLimits Limits { get; }
    public DataSyncDevice Device { get; }
    public BakabaseDbContext Db => Store.Db;
    public DateTime Now => Store.UtcNow;
    public IReadOnlyDictionary<string, IDataSyncKind> Kinds => Store.Kinds;

    public IReadOnlyDictionary<string, IDataSyncKindCodec> Codecs =>
        Kinds.ToDictionary(k => k.Key, k => k.Value.Codec, StringComparer.Ordinal);

    /// <summary>Kinds whose adapters may have written: their caches are dropped on a rollback (§8.10.5).</summary>
    public HashSet<string> TouchedKinds { get; } = new(StringComparer.Ordinal);

    /// <summary>The tracked local state row; loaded by <see cref="LoadStateAsync"/> after Refresh.</summary>
    public DataSyncLocalStateDbModel State { get; private set; } = null!;

    public DataSyncActorId SelfActor => new(State.ActorId);
    public DataSyncEditorRef Self => new(State.NodeId, Device.Name, State.ActorId);

    public bool InTransaction => _transaction is not null;

    public static async Task<DataSyncApplySession> OpenAsync(IServiceScopeFactory scopes, CancellationToken ct)
    {
        var scope = scopes.CreateAsyncScope();
        try
        {
            var device = await scope.ServiceProvider.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
            return new DataSyncApplySession(scope, device);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public IDataSyncKind Adapter(string kind) =>
        Kinds.TryGetValue(kind, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No data sync kind adapter is registered for '{kind}'.");

    /// <summary><c>BEGIN IMMEDIATE</c> (F27): the write lock from the first statement.</summary>
    public async Task BeginAsync(CancellationToken ct)
    {
        if (_transaction is not null) throw new InvalidOperationException("A transaction is already open.");
        _transaction = await Db.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null) throw new InvalidOperationException("No transaction is open.");
        await Db.SaveChangesAsync(ct);
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <summary>Rolls back, forgets what the context tracked and drops the touched kinds' caches (§8.10.5).</summary>
    public async Task RollbackAsync()
    {
        if (_transaction is not null)
        {
            try
            {
                await _transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        Db.ChangeTracker.Clear();
        foreach (var kind in TouchedKinds)
        {
            if (Kinds.TryGetValue(kind, out var adapter)) adapter.ResetCaches();
        }
    }

    /// <summary>The tracked local state row (after Refresh created it, §4.5).</summary>
    public async Task<DataSyncLocalStateDbModel> LoadStateAsync(CancellationToken ct)
    {
        State = await Store.LoadStateAsync(ct) ??
                throw new InvalidOperationException("Data sync has no local state yet: Refresh creates it (§4.5).");
        return State;
    }

    /// <summary>This actor's next counter (§5.6); saved with the row that takes it.</summary>
    public long NextCounter() => State.ActorCounter = checked(State.ActorCounter + 1);

    /// <summary>Current actor → ActorCounter; retired actors → their recorded counters (§5.6).</summary>
    public IReadOnlyDictionary<string, long> OwnActorCounters()
    {
        var counters = new Dictionary<string, long>(
            DataSyncStoredJson.ReadCounters(State.RetiredActorsJson, "RetiredActorsJson"), StringComparer.Ordinal)
        {
            [State.ActorId] = State.ActorCounter,
        };
        return counters;
    }

    public IReadOnlyDictionary<string, long> RetiredActorCounters() =>
        DataSyncStoredJson.ReadCounters(State.RetiredActorsJson, "RetiredActorsJson");

    /// <summary>The live side row of a local entity, tracked.</summary>
    public Task<DataSyncEntityDbModel> LiveRowAsync(string kind, string localKey, CancellationToken ct) =>
        Store.RequireLiveAsync(kind, localKey, ct);

    /// <summary>The row (live or tombstoned) that owns <paramref name="key"/>, tracked.</summary>
    public async Task<DataSyncEntityDbModel?> OwnerAsync(string kind, string key, CancellationToken ct)
    {
        await Store.FlushAsync(ct);
        return await Store.FindOwnerAsync(kind, key, ct);
    }

    /// <summary>Every key of a row: its primary, then its aliases in ordinal order.</summary>
    public async Task<EntityKeys> KeysOfAsync(DataSyncEntityDbModel row, CancellationToken ct)
    {
        await Store.FlushAsync(ct);
        var keys = await Store.KeysOfAsync(row.Kind, row.SyncKey, ct);
        return DataSyncStoredJson.ToEntityKeys(row.SyncKey, keys.Skip(1));
    }

    /// <summary>A link row, tracked.</summary>
    public async Task<DataSyncLinkDbModel?> LinkAsync(int id, CancellationToken ct)
    {
        await Store.FlushAsync(ct);
        return await Db.DataSyncLinks.SingleOrDefaultAsync(l => l.Id == id, ct);
    }

    /// <summary>A base row, tracked.</summary>
    public async Task<DataSyncPeerBaseDbModel?> BaseRowAsync(int linkId, string kind, string key, CancellationToken ct)
    {
        await Store.FlushAsync(ct);
        return await Db.DataSyncPeerBases.SingleOrDefaultAsync(
            b => b.LinkId == linkId && b.Kind == kind && b.SyncKey == key, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await RollbackAsync();
        await _scope.DisposeAsync();
    }
}
