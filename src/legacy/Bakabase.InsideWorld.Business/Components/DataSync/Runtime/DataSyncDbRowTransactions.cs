using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// <see cref="IDataSyncRowTransactions"/> over the scope's <see cref="BakabaseDbContext"/>: EF's
/// <c>BeginTransactionAsync</c> issues <c>BEGIN IMMEDIATE</c> on SQLite, so the write lock is held from before the
/// row is read (F27). A scope whose context already has a transaction joins it; a scope without a context (a host
/// that composes the runtime over another store) runs without one.
/// </summary>
public sealed class DataSyncDbRowTransactions : IDataSyncRowTransactions
{
    public async Task<IDataSyncRowTransaction> BeginAsync(IServiceProvider scope, CancellationToken ct)
    {
        var db = scope.GetService<BakabaseDbContext>();
        if (db is null || db.Database.CurrentTransaction is not null) return Joined.Instance;
        return new Owned(db, await db.Database.BeginTransactionAsync(ct));
    }

    private sealed class Owned(BakabaseDbContext db, IDbContextTransaction transaction) : IDataSyncRowTransaction
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            // A rolled-back context still tracks what was written in it; nothing in the scope may read it back.
            if (!_committed) db.ChangeTracker.Clear();
        }
    }

    private sealed class Joined : IDataSyncRowTransaction
    {
        public static readonly Joined Instance = new();
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
