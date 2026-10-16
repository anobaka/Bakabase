using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StateJson = System.Text.Json.JsonSerializer;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

/// <summary>
/// Rebuildable owner/epoch-isolated metadata. A snapshot is staged beside the last complete
/// generation; its cursor and page commit together, and readers never see a half-imported library.
/// </summary>
public sealed class MetadataReplicaStore(string directory)
{
    public const int MaxBatchResources = 512;
    public const int MaxBatchBytes = 8 * 1024 * 1024;
    public const int MaxResourceBytes = 4 * 1024 * 1024;

    public async Task<ReplicaCheckpoint> BeginSnapshotAsync(ReplicaIdentity identity, string generation,
        long sequence = 0, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ValidateToken(generation);
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction();
        var state = await ReadStateAsync(connection, transaction, cancellationToken);
        if (state.Checkpoint is {Complete: false} checkpoint && checkpoint.Generation == generation)
            return checkpoint;
        if (state.ActiveGeneration == generation)
            throw new ArgumentException("A snapshot needs a new generation.", nameof(generation));
        var next = new ReplicaCheckpoint(generation, sequence, cursor, false);
        // A superseded unfinished generation is disposable; the active one remains searchable.
        await ExecuteAsync(connection, transaction,
            "DELETE FROM resources WHERE generation <> coalesce($active,''); DELETE FROM postings WHERE generation <> coalesce($active,''); DELETE FROM catalog WHERE generation <> coalesce($active,'');",
            cancellationToken, ("$active", state.ActiveGeneration));
        await ExecuteAsync(connection, transaction, "UPDATE state SET checkpoint=$checkpoint WHERE id=1",
            cancellationToken, ("$checkpoint", StateJson.Serialize(next)));
        transaction.Commit();
        return next;
    }

    public async Task ApplyBatchAsync(ReplicaIdentity identity, ReplicaBatch batch,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(batch);
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction();
        var state = await ReadStateAsync(connection, transaction, cancellationToken);
        // A retry after a lost HTTP response must not apply the page twice.
        if (state.Checkpoint == batch.Next) return;
        if (state.Checkpoint != batch.Expected) throw new ReplicaCheckpointConflictException();
        var generation = batch.Next.Generation;
        foreach (var id in batch.Deletes)
            await ExecuteAsync(connection, transaction,
                "DELETE FROM postings WHERE generation=$g AND resource_id=$id; DELETE FROM resources WHERE generation=$g AND id=$id;",
                cancellationToken, ("$g", generation), ("$id", id));
        await using var resourceCommand = connection.CreateCommand();
        resourceCommand.Transaction = transaction;
        resourceCommand.CommandText = """
            INSERT INTO resources(generation,id,revision,json,posting_bytes,path,file_name,created,file_created,file_modified,played,tags,health)
            VALUES($g,$id,$rev,$json,$bytes,$path,$name,$created,$fc,$fm,$played,$tags,$health)
            ON CONFLICT(generation,id) DO UPDATE SET revision=excluded.revision,json=excluded.json,
            posting_bytes=excluded.posting_bytes,path=excluded.path,file_name=excluded.file_name,
            created=excluded.created,file_created=excluded.file_created,file_modified=excluded.file_modified,
            played=excluded.played,tags=excluded.tags,health=excluded.health;
            """;
        foreach (var name in new[] {"$g", "$id", "$rev", "$json", "$bytes", "$path", "$name", "$created", "$fc", "$fm", "$played", "$tags", "$health"})
            resourceCommand.Parameters.AddWithValue(name, DBNull.Value);
        await using var postingCommand = connection.CreateCommand();
        postingCommand.Transaction = transaction;
        postingCommand.CommandText = "INSERT INTO postings(generation,resource_id,ordinal,pool,property_id,value_key,range_type,range_value) VALUES($g,$id,$ordinal,$pool,$property,$value,$type,$range)";
        foreach (var name in new[] {"$g", "$id", "$ordinal", "$pool", "$property", "$value", "$type", "$range"})
            postingCommand.Parameters.AddWithValue(name, DBNull.Value);
        foreach (var resource in batch.Upserts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Checkpoint order is authoritative. A stale per-resource image is never accepted.
            await using var revision = connection.CreateCommand();
            revision.Transaction = transaction;
            revision.CommandText = "SELECT revision FROM resources WHERE generation=$g AND id=$id";
            revision.Parameters.AddWithValue("$g", generation);
            revision.Parameters.AddWithValue("$id", resource.ResourceId);
            var previous = await revision.ExecuteScalarAsync(cancellationToken);
            if (previous is long previousRevision && previousRevision > resource.Revision)
                throw new ReplicaCheckpointConflictException();
            var s = resource.Sort;
            Set(resourceCommand, ("$g", generation), ("$id", resource.ResourceId), ("$rev", resource.Revision),
                ("$json", resource.ResourceJson), ("$bytes", PostingBytes(resource)), ("$path", s.Path), ("$name", s.FileName),
                ("$created", s.CreateDt.Ticks), ("$fc", s.FileCreateDt.Ticks), ("$fm", s.FileModifyDt.Ticks),
                ("$played", s.PlayedAt?.Ticks), ("$tags", (int)s.Tags),
                ("$health", s.HealthScore?.ToString(CultureInfo.InvariantCulture)));
            await resourceCommand.ExecuteNonQueryAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, "DELETE FROM postings WHERE generation=$g AND resource_id=$id",
                cancellationToken, ("$g", generation), ("$id", resource.ResourceId));
            for (var ordinal = 0; ordinal < resource.Postings.Count; ordinal++)
            {
                var p = resource.Postings[ordinal];
                Set(postingCommand, ("$g", generation), ("$id", resource.ResourceId), ("$ordinal", ordinal),
                    ("$pool", (int)p.Pool), ("$property", p.PropertyId), ("$value", p.ValueKey),
                    ("$type", p.RangeValue?.Type), ("$range", p.RangeValue?.Value));
                await postingCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        foreach (var item in batch.CatalogDeletes)
            await ExecuteAsync(connection, transaction, "DELETE FROM catalog WHERE generation=$g AND kind=$kind AND key=$key",
                cancellationToken, ("$g", generation), ("$kind", item.Kind), ("$key", item.Key));
        foreach (var item in batch.CatalogUpserts)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO catalog(generation,kind,key,json) VALUES($g,$kind,$key,$json) ON CONFLICT(generation,kind,key) DO UPDATE SET json=excluded.json",
                cancellationToken, ("$g", generation), ("$kind", item.Kind), ("$key", item.Key), ("$json", item.Json));
        await ExecuteAsync(connection, transaction,
            "UPDATE state SET checkpoint=$checkpoint,active_generation=$active,active_sequence=$sequence WHERE id=1",
            cancellationToken, ("$checkpoint", StateJson.Serialize(batch.Next)),
            ("$active", batch.Next.Complete ? generation : state.ActiveGeneration),
            ("$sequence", batch.Next.Complete ? batch.Next.Sequence : state.ActiveSequence));
        if (batch.Next.Complete)
            await ExecuteAsync(connection, transaction,
                "DELETE FROM resources WHERE generation<>$g; DELETE FROM postings WHERE generation<>$g; DELETE FROM catalog WHERE generation<>$g;",
                cancellationToken, ("$g", generation));
        transaction.Commit();
    }

    public async Task<ReplicaStatus> GetStatusAsync(ReplicaIdentity identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await ReadStateAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM resources WHERE generation=$g";
        command.Parameters.AddWithValue("$g", (object?)state.ActiveGeneration ?? DBNull.Value);
        var activeCount = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        command.Parameters["$g"].Value = (object?)state.Checkpoint?.Generation ?? DBNull.Value;
        return state with {ResourceCount = activeCount, StagedResourceCount = (long)(await command.ExecuteScalarAsync(cancellationToken))!};
    }

    /// <summary>Counts only, including the staged snapshot before its first complete publication.</summary>
    public async Task<IReadOnlyDictionary<string, long>> GetCatalogCountsAsync(ReplicaIdentity identity,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await ReadStateAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT kind,count(*) FROM catalog WHERE generation=$g GROUP BY kind";
        command.Parameters.AddWithValue("$g", (object?)state.ActiveGeneration ?? state.Checkpoint?.Generation ?? (object)DBNull.Value);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    public async Task<IReadOnlyList<string>> GetByIdsAsync(ReplicaIdentity identity, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default) =>
        (await GetRowsByIdsAsync(identity, ids, cancellationToken)).Select(r => r.ResourceJson).ToArray();

    public async Task<IReadOnlyList<ReplicaResourceRow>> GetRowsByIdsAsync(ReplicaIdentity identity, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count > MaxBatchResources || ids.Any(id => id <= 0)) throw new ArgumentException("Invalid resource IDs.");
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await RequireActiveAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision,json FROM resources WHERE generation=$g AND id=$id";
        command.Parameters.AddWithValue("$g", state.ActiveGeneration!);
        command.Parameters.AddWithValue("$id", 0);
        var rows = new List<ReplicaResourceRow>();
        long bytes = 0;
        foreach (var id in ids)
        {
            command.Parameters["$id"].Value = id;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(1);
                bytes += Encoding.UTF8.GetByteCount(json);
                if (bytes > MaxBatchBytes) throw new ReplicaBudgetExceededException("Resource response exceeds its byte budget; request fewer IDs.");
                rows.Add(new ReplicaResourceRow(id, reader.GetInt64(0), json));
            }
        }
        return rows;
    }

    public async Task<ReplicaSyncState> GetSyncStateAsync(ReplicaIdentity identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM sync_state WHERE id=1";
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? StateJson.Deserialize<ReplicaSyncState>(json) ?? new ReplicaSyncState() : new ReplicaSyncState();
    }

    public async Task SetSyncStateAsync(ReplicaIdentity identity, ReplicaSyncState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        await ExecuteAsync(connection, null,
            "INSERT INTO sync_state(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
            cancellationToken, ("$json", StateJson.Serialize(state)));
    }

    public async Task UpdateSyncStateAsync(ReplicaIdentity identity, Func<ReplicaSyncState, ReplicaSyncState> update,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT json FROM sync_state WHERE id=1";
        var state = await command.ExecuteScalarAsync(cancellationToken) is string json
            ? StateJson.Deserialize<ReplicaSyncState>(json) ?? new ReplicaSyncState() : new ReplicaSyncState();
        await ExecuteAsync(connection, transaction,
            "INSERT INTO sync_state(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
            cancellationToken, ("$json", StateJson.Serialize(update(state))));
        transaction.Commit();
    }

    internal async Task<string> ReadActiveGenerationAsync(ReplicaIdentity identity, CancellationToken ct)
    {
        await using var connection = await OpenAsync(identity, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        return (await RequireActiveAsync(connection, transaction, ct)).ActiveGeneration!;
    }

    internal sealed record PropertyDisplay(string? Name, int Type, int Order);

    // A returned resource page only needs these small definition fields. Keep large option
    // trees in SQLite, and never load an unrelated owner's or the whole catalog's definitions.
    internal async Task<IReadOnlyDictionary<string, PropertyDisplay>> ReadPropertyDisplayAsync(
        ReplicaIdentity identity, string generation, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count > 32768) throw new ReplicaBudgetExceededException("Resource property definitions exceed the query budget.");
        await using var connection = await OpenAsync(identity, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await RequireActiveAsync(connection, transaction, ct);
        if (state.ActiveGeneration != generation) throw new ReplicaCheckpointConflictException();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT json_extract(json,'$.name'),json_extract(json,'$.type'),COALESCE(json_extract(json,'$.order'),0) FROM catalog WHERE generation=$g AND kind='properties' AND key=$key";
        command.Parameters.AddWithValue("$g", state.ActiveGeneration!);
        command.Parameters.AddWithValue("$key", "");
        var result = new Dictionary<string, PropertyDisplay>();
        long bytes = 0;
        foreach (var key in keys.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            command.Parameters["$key"].Value = key;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(1)) continue;
            var name = reader.IsDBNull(0) ? null : reader.GetString(0);
            bytes += 64L + (key.Length + (name?.Length ?? 0)) * 2L;
            if (bytes > MaxBatchBytes) throw new ReplicaBudgetExceededException("Resource property definitions exceed the query budget.");
            result[key] = new(name, reader.GetInt32(1), reader.GetInt32(2));
        }
        return result;
    }

    public async Task<IReadOnlyList<ReplicaCatalog>> ReadCatalogAsync(ReplicaIdentity identity, string kind,
        string? afterKey = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxBatchResources) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await RequireActiveAsync(connection, transaction, cancellationToken);
        return await ReadCatalogPageAsync(connection, transaction, state.ActiveGeneration!, kind, afterKey, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<ReplicaCatalog>> ReadCatalogAllAsync(ReplicaIdentity identity, string kind,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(identity, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = await RequireActiveAsync(connection, transaction, cancellationToken);
        var result = new List<ReplicaCatalog>();
        string? after = null;
        long bytes = 0;
        while (true)
        {
            var page = await ReadCatalogPageAsync(connection, transaction, state.ActiveGeneration!, kind, after, 100, cancellationToken);
            if (page.Count == 0) return result;
            foreach (var item in page)
            {
                bytes += Encoding.UTF8.GetByteCount(item.Json) + (item.Key.Length + item.Kind.Length) * 2L;
                if (bytes > MaxBatchBytes) throw new ReplicaBudgetExceededException("Catalog response exceeds its byte budget.");
                result.Add(item);
            }
            after = page[^1].Key;
        }
    }

    internal static async Task<IReadOnlyList<ReplicaCatalog>> ReadCatalogPageAsync(SqliteConnection connection,
        SqliteTransaction transaction, string generation, string kind, string? afterKey, int limit, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT key,json FROM catalog WHERE generation=$g AND kind=$kind AND key>$after ORDER BY key LIMIT $limit";
        command.Parameters.AddWithValue("$g", generation);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$after", afterKey ?? "");
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<ReplicaCatalog>();
        long bytes = 0;
        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(1);
            bytes += Encoding.UTF8.GetByteCount(json);
            if (bytes > MaxBatchBytes) throw new ReplicaBudgetExceededException("Catalog page exceeds its byte budget; request a smaller page.");
            rows.Add(new ReplicaCatalog(kind, reader.GetString(0), json));
        }
        return rows;
    }

    internal async Task<SqliteConnection> OpenAsync(ReplicaIdentity identity, CancellationToken ct)
    {
        ValidateToken(identity.ServerId);
        ValidateToken(identity.LibraryEpoch);
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StateJson.Serialize(identity))));
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, hash + ".sqlite"), Pooling = false,
            Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            connection.CreateCollation("replica_filename", (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a, b));
            connection.CreateCollation("replica_decimal", (a, b) => decimal.Parse(a, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(b, CultureInfo.InvariantCulture)));
            await ExecuteAsync(connection, null, """
                PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA cache_size=-8192; PRAGMA mmap_size=0; PRAGMA temp_store=FILE;
                CREATE TABLE IF NOT EXISTS state(id INTEGER PRIMARY KEY CHECK(id=1), identity TEXT NOT NULL, checkpoint TEXT, active_generation TEXT, active_sequence INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS sync_state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS resources(generation TEXT NOT NULL,id INTEGER NOT NULL,revision INTEGER NOT NULL,json TEXT NOT NULL,posting_bytes INTEGER NOT NULL,path TEXT,file_name TEXT,created INTEGER NOT NULL,file_created INTEGER NOT NULL,file_modified INTEGER NOT NULL,played INTEGER,tags INTEGER NOT NULL,health TEXT,PRIMARY KEY(generation,id)) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS postings(generation TEXT NOT NULL,resource_id INTEGER NOT NULL,ordinal INTEGER NOT NULL,pool INTEGER NOT NULL,property_id INTEGER NOT NULL,value_key TEXT,range_type TEXT,range_value TEXT,PRIMARY KEY(generation,resource_id,ordinal)) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS catalog(generation TEXT NOT NULL,kind TEXT NOT NULL,key TEXT NOT NULL,json TEXT NOT NULL,PRIMARY KEY(generation,kind,key)) WITHOUT ROWID;
                INSERT OR IGNORE INTO state(id,identity) VALUES(1,$identity);
                """, ct, ("$identity", StateJson.Serialize(identity)));
            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT identity FROM state WHERE id=1";
            if ((string?)await check.ExecuteScalarAsync(ct) != StateJson.Serialize(identity))
                throw new InvalidDataException("Metadata replica identity mismatch.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    internal static async Task<ReplicaStatus> ReadStateAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT checkpoint,active_generation,active_sequence FROM state WHERE id=1";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Metadata replica state is missing.");
        return new ReplicaStatus(reader.IsDBNull(0) ? null : StateJson.Deserialize<ReplicaCheckpoint>(reader.GetString(0)),
            reader.IsDBNull(1) ? null : reader.GetString(1), 0, reader.GetInt64(2));
    }

    internal static async Task<ReplicaStatus> RequireActiveAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        var state = await ReadStateAsync(connection, transaction, ct);
        if (state.ActiveGeneration == null) throw new ReplicaNotReadyException();
        return state;
    }

    internal static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Set(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters) command.Parameters[name].Value = value ?? DBNull.Value;
    }

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) throw new ArgumentException("Invalid replica identity or generation.");
    }

    internal static long PostingBytes(ReplicaResource resource) => resource.Postings.Sum(p =>
        96L + (p.ValueKey?.Length ?? 0) * 2L + (p.RangeValue?.Value.Length ?? 0) * 2L);

    private static void ValidateJson(string json, int? resourceId = null)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                (resourceId != null && (!document.RootElement.TryGetProperty("id", out var id) ||
                    id.ValueKind != System.Text.Json.JsonValueKind.Number || !id.TryGetInt32(out var actual) || actual != resourceId)))
                throw new ArgumentException("Invalid metadata object or resource identity.");
        }
        catch (System.Text.Json.JsonException ex) { throw new ArgumentException("Invalid metadata JSON.", ex); }
    }

    private static void ValidateBatch(ReplicaBatch batch)
    {
        if (batch.Expected == null || batch.Next == null || batch.Upserts == null || batch.Deletes == null ||
            batch.CatalogUpserts == null || batch.CatalogDeletes == null || batch.Upserts.Any(r => r == null) ||
            batch.CatalogUpserts.Any(c => c == null) || batch.CatalogDeletes.Any(c => c == null))
            throw new ArgumentException("Incomplete metadata batch.");
        ValidateToken(batch.Next.Generation);
        if (batch.Expected.CatalogRevision < 0 || batch.Next.CatalogRevision < batch.Expected.CatalogRevision ||
            batch.Expected.Sequence < 0 || batch.Expected.Generation != batch.Next.Generation || batch.Next.Sequence < batch.Expected.Sequence ||
            (batch.Expected.Complete && !batch.Next.Complete) ||
            (batch.Next == batch.Expected && (batch.Upserts.Count != 0 || batch.Deletes.Count != 0 ||
                                             batch.CatalogUpserts.Count != 0 || batch.CatalogDeletes.Count != 0)))
            throw new ArgumentException("Invalid replica checkpoint transition.");
        if (batch.Upserts.Count > MaxBatchResources || batch.Deletes.Count > MaxBatchResources ||
            batch.CatalogUpserts.Count > MaxBatchResources || batch.CatalogDeletes.Count > MaxBatchResources)
            throw new ReplicaBudgetExceededException("Metadata batch exceeds its item budget.");
        if (batch.Upserts.Select(r => r.ResourceId).Distinct().Count() != batch.Upserts.Count ||
            batch.Upserts.Any(r => batch.Deletes.Contains(r.ResourceId)) || batch.Deletes.Any(id => id <= 0))
            throw new ArgumentException("Invalid or duplicate resource IDs.");
        long bytes = 0;
        foreach (var resource in batch.Upserts)
        {
            if (resource.ResourceId <= 0 || resource.Revision < 0 || string.IsNullOrEmpty(resource.ResourceJson) ||
                resource.Sort == null || resource.Postings == null || resource.Postings.Any(p => p == null ||
                    (p.RangeValue != null && (p.RangeValue.Type == null || p.RangeValue.Value == null))))
                throw new ArgumentException("Invalid resource metadata.");
            var size = Encoding.UTF8.GetByteCount(resource.ResourceJson) + PostingBytes(resource);
            if (size > MaxResourceBytes) throw new ReplicaBudgetExceededException("A resource exceeds the metadata byte budget.");
            ValidateJson(resource.ResourceJson, resource.ResourceId);
            bytes += size;
            foreach (var posting in resource.Postings)
            {
                if (!Enum.IsDefined(posting.Pool) || posting.PropertyId <= 0 || (posting.ValueKey == null && posting.RangeValue == null))
                    throw new ArgumentException("Invalid property posting.");
                try { _ = posting.RangeValue?.ToComparable(); }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                { throw new ArgumentException("Invalid property range.", ex); }
            }
        }
        foreach (var catalog in batch.CatalogUpserts)
        {
            ValidateToken(catalog.Kind);
            ValidateToken(catalog.Key);
            if (catalog.Json == null) throw new ArgumentException("Invalid catalog metadata.");
            ValidateJson(catalog.Json);
        }
        foreach (var catalog in batch.CatalogDeletes)
        {
            ValidateToken(catalog.Kind);
            ValidateToken(catalog.Key);
        }
        bytes += batch.CatalogUpserts.Sum(c => (long)Encoding.UTF8.GetByteCount(c.Json) + c.Key.Length * 2L + c.Kind.Length * 2L);
        if (bytes > MaxBatchBytes) throw new ReplicaBudgetExceededException("Metadata batch exceeds its byte budget.");
    }
}
