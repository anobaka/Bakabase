using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Microsoft.Data.Sqlite;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record MetadataSyncActivity(long Id, string ServerId, string? Name, string? LibraryEpoch,
    DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc, string State, string Phase,
    long ProcessedCount, long ResourceCount, string? ErrorCode, string? Error);
public sealed record MetadataSyncActivityPage(IReadOnlyList<MetadataSyncActivity> Entries, long? NextBeforeId);

/// <summary>Run summaries only, never resource paths, payloads, or one log per resource.
/// 5,000 rows / 30 days, SQL keyset pagination and a 2 MiB SQLite cache. An unfinished run is
/// marked interrupted after process restart; its actual data checkpoint remains in the replica.</summary>
public sealed class MetadataSyncActivityStore(IFederationDataDirectory directory)
{
    public const int MaxEntries = 5000;
    private readonly SemaphoreSlim _gate = new(1);
    private bool _initialized;

    public async Task<long> BeginAsync(string serverId, string? name, string? epoch, string phase, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            await using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO activity(server_id,name,epoch,started,updated,state,phase,processed,resources) VALUES($server,$name,$epoch,$now,$now,'Running',$phase,0,0); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$server", serverId);
            command.Parameters.AddWithValue("$name", Limit(name, 256));
            command.Parameters.AddWithValue("$epoch", (object?)epoch ?? DBNull.Value);
            command.Parameters.AddWithValue("$phase", phase);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var id = (long)(await command.ExecuteScalarAsync(ct))!;
            await PruneAsync(db, ct);
            return id;
        }
        finally { _gate.Release(); }
    }

    public async Task FinishAsync(long id, string state, string phase, long processed, long resources,
        string? errorCode, string? error, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            await using var command = db.CreateCommand();
            command.CommandText = "UPDATE activity SET updated=$now,state=$state,phase=$phase,processed=$processed,resources=$resources,error_code=$code,error=$error WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$phase", phase);
            command.Parameters.AddWithValue("$processed", processed);
            command.Parameters.AddWithValue("$resources", resources);
            command.Parameters.AddWithValue("$code", Limit(errorCode, 128));
            command.Parameters.AddWithValue("$error", Limit(error, 2048));
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<MetadataSyncActivityPage> ReadAsync(string? serverId, long? beforeId, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 100);
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT id,server_id,name,epoch,started,updated,state,phase,processed,resources,error_code,error FROM activity WHERE ($server IS NULL OR server_id=$server) AND ($before IS NULL OR id<$before) ORDER BY id DESC LIMIT $take";
            command.Parameters.AddWithValue("$server", (object?)serverId ?? DBNull.Value);
            command.Parameters.AddWithValue("$before", (object?)beforeId ?? DBNull.Value);
            command.Parameters.AddWithValue("$take", take + 1);
            var rows = new List<MetadataSyncActivity>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            string? Optional(int n) => reader.IsDBNull(n) ? null : reader.GetString(n);
            while (await reader.ReadAsync(ct))
                rows.Add(new(reader.GetInt64(0), reader.GetString(1), Optional(2), Optional(3),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                    reader.GetString(6), reader.GetString(7), reader.GetInt64(8), reader.GetInt64(9), Optional(10), Optional(11)));
            var more = rows.Count > take;
            if (more) rows.RemoveAt(take);
            return new(rows, more ? rows[^1].Id : null);
        }
        finally { _gate.Release(); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var path = Path.Combine(directory.Ensure(), "metadata-replicas");
        Directory.CreateDirectory(path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.Combine(path, "sync-activity.db"), Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA cache_size=-2048; PRAGMA journal_mode=WAL; PRAGMA journal_size_limit=1048576;";
            await command.ExecuteNonQueryAsync(ct);
            if (!_initialized)
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS activity(id INTEGER PRIMARY KEY AUTOINCREMENT, server_id TEXT NOT NULL,
                        name TEXT, epoch TEXT, started INTEGER NOT NULL, updated INTEGER NOT NULL, state TEXT NOT NULL,
                        phase TEXT NOT NULL, processed INTEGER NOT NULL, resources INTEGER NOT NULL, error_code TEXT, error TEXT);
                    CREATE INDEX IF NOT EXISTS activity_server ON activity(server_id,id);
                    UPDATE activity SET state='Interrupted' WHERE state='Running';
                    """;
                await command.ExecuteNonQueryAsync(ct);
                await PruneAsync(connection, ct);
                _initialized = true;
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task PruneAsync(SqliteConnection db, CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM activity WHERE updated<$cutoff OR id IN (SELECT id FROM activity ORDER BY id DESC LIMIT -1 OFFSET $max);";
        command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$max", MaxEntries);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static object Limit(string? value, int maximum) => value == null ? DBNull.Value : value.Length > maximum ? value[..maximum] : value;
}
