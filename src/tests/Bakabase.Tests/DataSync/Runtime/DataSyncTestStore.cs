using System.Data.Common;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The harness's real data sync tables, seeded and read by the test, each call in a scope of its own. What the test
/// writes here is not counted in <see cref="Writes"/>: every other write to the database is.
/// </summary>
internal sealed class DataSyncTestStore(IServiceProvider services, DataSyncWriteCounter counter)
{
    /// <summary>Every write to the database since the harness started, the test's own seeding left out.</summary>
    public int Writes => counter.Count;

    private T Db<T>(Func<BakabaseDbContext, T> body)
    {
        using var scope = services.CreateScope();
        using var _ = counter.Suspend();
        return body(scope.ServiceProvider.GetRequiredService<BakabaseDbContext>());
    }

    private void Write(Action<BakabaseDbContext> body) => Db(db =>
    {
        body(db);
        db.SaveChanges();
        return 0;
    });

    private T Store<T>(Func<IDataSyncStore, Task<T>> body)
    {
        using var scope = services.CreateScope();
        using var _ = counter.Suspend();
        return body(scope.ServiceProvider.GetRequiredService<IDataSyncStore>()).GetAwaiter().GetResult();
    }

    private DateTime Now => services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    // ---- links -------------------------------------------------------------------------------------------------

    public DataSyncLinkDbModel Add(DataSyncLinkDbModel link) => Db(db =>
    {
        var row = link with { };
        db.DataSyncLinks.Add(row);
        db.SaveChanges();
        return row with { };
    });

    public DataSyncLinkDbModel? Get(int id) => Db(db => db.DataSyncLinks.AsNoTracking().SingleOrDefault(l => l.Id == id));

    public IReadOnlyList<DataSyncLinkDbModel> All() =>
        Db(db => db.DataSyncLinks.AsNoTracking().OrderBy(l => l.Id).ToList());

    public void Edit(int id, Action<DataSyncLinkDbModel> edit) => Write(db => edit(db.DataSyncLinks.Single(l => l.Id == id)));

    /// <summary>The links deleted (reset) since the harness started: every id the table handed out that is gone.</summary>
    public List<int> Deleted => Db(db =>
    {
        var issued = db.Database.SqlQueryRaw<int>(
            "SELECT seq AS Value FROM sqlite_sequence WHERE name = 'DataSyncLinks'").ToList().SingleOrDefault();
        var live = db.DataSyncLinks.Select(l => l.Id).ToHashSet();
        return Enumerable.Range(1, issued).Where(id => !live.Contains(id)).ToList();
    });

    // ---- the local state row -------------------------------------------------------------------------------------

    /// <summary>The local state row; set to null to take it away (a device that never ran Refresh).</summary>
    public DataSyncLocalStateDbModel? LocalState
    {
        get => Db(db => db.DataSyncLocalStates.AsNoTracking().SingleOrDefault());
        set => Write(db =>
        {
            var row = db.DataSyncLocalStates.SingleOrDefault();
            if (value is null)
            {
                if (row is not null) db.Remove(row);
            }
            else if (row is null) db.Add(value with { });
            else db.Entry(row).CurrentValues.SetValues(value);
        });
    }

    public void EditLocalState(Action<DataSyncLocalStateDbModel> edit) =>
        Write(db => edit(db.DataSyncLocalStates.Single()));

    // ---- inbox, entities, bases, readers, history ----------------------------------------------------------------

    public DataSyncInboxItemDbModel AddItem(DataSyncInboxItemDbModel item) => Db(db =>
    {
        var row = item with { Id = 0 };
        db.DataSyncInboxItems.Add(row);
        db.SaveChanges();
        return row with { };
    });

    public DataSyncInboxItemDbModel Item(long id) => Db(db => db.DataSyncInboxItems.AsNoTracking().Single(i => i.Id == id));

    public IReadOnlyList<DataSyncInboxItemDbModel> Items =>
        Db(db => db.DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).ToList());

    /// <summary>Closes the open items that match, as a closure of the store would.</summary>
    public void CloseWhere(Func<DataSyncInboxItemDbModel, bool> match, DataSyncInboxClosure closure) => Write(db =>
    {
        foreach (var item in db.DataSyncInboxItems.Where(i => i.ClosedAtUtc == null).AsEnumerable().Where(match))
        {
            item.ClosedAtUtc = Now;
            item.Closure = closure;
        }
    });

    /// <summary>Opens or closes plain conflicts of <paramref name="linkId"/> until it has <paramref name="count"/> open.</summary>
    public void SetOpenItems(int linkId, int count)
    {
        var link = Get(linkId)!;
        var open = Items.Count(i => i.LinkId == linkId && i.ClosedAtUtc is null);
        if (open > count)
        {
            var closing = Items.Where(i => i.LinkId == linkId && i.ClosedAtUtc is null).Take(open - count)
                .Select(i => i.Id).ToHashSet();
            CloseWhere(i => closing.Contains(i.Id), DataSyncInboxClosure.ResolvedHere);
            return;
        }

        Write(db => db.DataSyncInboxItems.AddRange(Enumerable.Range(0, count - open).Select(_ =>
            new DataSyncInboxItemDbModel
            {
                LinkId = linkId, PeerNodeId = link.PeerNodeId, Kind = "customProperty",
                SyncKey = Guid.NewGuid().ToString("N"), Type = DataSyncInboxItemType.FieldConflict,
                Origin = DataSyncInboxItemOrigin.Merger, SubjectPath = "name", PayloadJson = "{}",
                Token = Guid.NewGuid().ToString("N")[..20], CreatedAtUtc = Now, UpdatedAtUtc = Now,
            })));
    }

    public DataSyncEntityDbModel AddEntity(DataSyncEntityDbModel entity) => Db(db =>
    {
        var row = entity with { };
        db.DataSyncEntities.Add(row);
        db.SaveChanges();
        return row with { };
    });

    public IReadOnlyList<DataSyncEntityDbModel> Entities =>
        Db(db => db.DataSyncEntities.AsNoTracking().OrderBy(e => e.Id).ToList());

    /// <summary>Stores the bases as the merge writes them (the kind's codec hashes their records).</summary>
    public void SetBases(int linkId, params DataSyncPeerBase[] bases) => Store(async s =>
    {
        await s.UpsertBasesAsync(linkId, bases.Select(b => new DataSyncBaseUpdate(b.Kind, b.Key, b.State, b.Exclusion,
            b.Record, b.ChildMap, b.Pending, false)), default);
        return 0;
    });

    public IReadOnlyList<DataSyncPeerBase> Bases(int linkId, string kind) => Store(s => s.GetBasesAsync(linkId, kind, default));

    public IReadOnlyList<DataSyncReaderDbModel> Readers =>
        Db(db => db.DataSyncReaders.AsNoTracking().OrderBy(r => r.NodeId).ToList());

    public void AddReader(DataSyncReaderDbModel reader) => Write(db => db.DataSyncReaders.Add(reader with { }));

    public IReadOnlyList<DataSyncApplyLogDbModel> History =>
        Db(db => db.DataSyncApplyLogs.AsNoTracking().OrderBy(l => l.Id).ToList());

    public int AddHistory(DataSyncApplyLogDbModel log) => Db(db =>
    {
        var row = log with { };
        db.DataSyncApplyLogs.Add(row);
        db.SaveChanges();
        return row.Id;
    });
}

/// <summary>
/// Counts the commands that write the database (<c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>): what "a GET never
/// writes" and "a Busy answer changed nothing" are checked against.
/// </summary>
internal sealed class DataSyncWriteCounter : DbCommandInterceptor
{
    private static readonly Regex WriteCommand = new(@"(^|;)\s*(INSERT|UPDATE|DELETE|REPLACE)\b", RegexOptions.Compiled);

    private readonly AsyncLocal<bool> _suspended = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    /// <summary>The test's own seeding is not counted.</summary>
    public IDisposable Suspend()
    {
        _suspended.Value = true;
        return new Resume(_suspended);
    }

    private sealed class Resume(AsyncLocal<bool> suspended) : IDisposable
    {
        public void Dispose() => suspended.Value = false;
    }

    private void Note(DbCommand command)
    {
        if (!_suspended.Value && WriteCommand.IsMatch(command.CommandText)) Interlocked.Increment(ref _count);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Note(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Note(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Note(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        Note(command);
        return ValueTask.FromResult(result);
    }
}
