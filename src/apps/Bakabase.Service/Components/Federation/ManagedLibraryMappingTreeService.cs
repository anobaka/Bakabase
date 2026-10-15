using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// A live, paged tree of owned resource directory metadata. SQLite does prefix filtering,
/// grouping and counting; neither resource objects nor a library-sized path trie are retained.
/// It does not enumerate directories, attachments, peers, or the merged federation library.
/// </summary>
public sealed class ManagedLibraryMappingTreeService(BakabaseDbContext db, IUserStoragePolicy storage,
    INodeIdentityProvider identity, ManagedLibraryMappingTreeTokens tokens)
{
    public const int MaximumPageSize = 200;
    public const int MaximumAnchors = 512;
    public const int MaximumResponseBytes = 1024 * 1024;
    public static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(4);
    private AnchorSet? _anchors;
    private const string Collation = "bakabase_managed_mapping_paths";

    public async Task<ManagedLibraryMappingTreePage> ReadAsync(string? parent, string? cursor, int limit,
        CancellationToken ct)
    {
        ValidateRequest(parent, cursor, limit);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ReadDeadline);
        try
        {
            var node = await identity.GetAsync(deadline.Token);
            var directory = parent == null ? null : ManagedLibraryMappingPaths.Normalize(parent)!;
            var after = cursor == null ? "" : tokens.Read(node, directory ?? "", cursor);
            var anchors = await GetAnchorsAsync(deadline.Token);
            var rows = await ReadRowsAsync(directory, after, limit + 1, anchors, deadline.Token);
            var more = rows.Count > limit;
            if (more) rows.RemoveAt(rows.Count - 1);
            var nodes = rows.Select(row => new ManagedLibraryMappingTreeNode(row.Path,
                ManagedLibraryMappingPaths.Name(row.Path), row.Count, row.HasChildren)).ToArray();
            var page = new ManagedLibraryMappingTreePage(node.LibraryEpoch, nodes, more && rows.Count > 0
                ? tokens.Issue(node, directory ?? "", rows[^1].Key) : null);
            if (JsonSerializer.SerializeToUtf8Bytes(page).Length > MaximumResponseBytes)
                throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
            deadline.Token.ThrowIfCancellationRequested();
            var current = await identity.GetAsync(deadline.Token);
            if (current.NodeId != node.NodeId || current.LibraryEpoch != node.LibraryEpoch)
                throw new FederationQueryException("LibraryEpochChanged", 409);
            return page;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FederationQueryException("MappingTreeBusy", 503,
                "This directory tree read exceeded its time budget. Retry a smaller branch.", retryable: true);
        }
    }

    public static void ValidateRequest(string? parent, string? cursor, int limit)
    {
        if (parent != null && ManagedLibraryMappingPaths.Normalize(parent) == null)
            throw new FederationQueryException("InvalidMappingTreeRequest", 422);
        ManagedLibraryMappingTreeTokens.ValidateInput(cursor);
        if (limit is < 1 or > MaximumPageSize) throw new FederationQueryException("InvalidMappingTreeRequest", 422);
    }

    private async Task<AnchorSet> GetAnchorsAsync(CancellationToken ct)
    {
        if (_anchors != null) return _anchors;
        // Native GetRoots reads mount/drive metadata only; this does not visit NAS contents.
        var supplied = storage.GetRoots().Take(MaximumAnchors + 1).ToArray();
        if (supplied.Length > MaximumAnchors) throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
        var roots = supplied.Select(x => ManagedLibraryMappingPaths.Normalize(x.Path)).Where(x => x != null && x != "/")
            .Select(x => x!).Distinct(StringComparer.Ordinal).OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToArray();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        string[] marks;
        try
        {
            using var interrupt = ct.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            marks = await db.PathMarks.AsNoTracking().Where(x => !x.IsDeleted)
                .Select(x => x.Path.Substring(0, ManagedLibraryMappingPaths.MaximumPathChars + 1))
                .Distinct().Take(MaximumAnchors + 1).ToArrayAsync(ct);
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 9 && ct.IsCancellationRequested)
        { throw new OperationCanceledException("The directory anchor read was cancelled.", e, ct); }
        finally { if (!wasOpen) await connection.CloseAsync(); }
        if (marks.Length > MaximumAnchors) throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
        var normalizedMarks = marks.Select(ManagedLibraryMappingPaths.Normalize).Where(x => x != null && x != "/")
            .Select(x => x!).Distinct(StringComparer.Ordinal).OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToArray();
        return _anchors = new(roots, normalizedMarks);
    }

    private async Task<List<Row>> ReadRowsAsync(string? parent, string after, int limit, AnchorSet anchors, CancellationToken ct)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            connection.CreateCollation(Collation, CompareKeys);
            // Microsoft.Data.Sqlite executes sqlite3_step synchronously. A timer-backed linked token
            // must interrupt SQLite itself, not merely check cancellation after a large aggregate.
            using var interrupt = ct.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            using var command = connection.CreateCommand();
            var table = db.Model.FindEntityType(typeof(ResourceDbModel))!.GetTableName()!.Replace("\"", "\"\"");
            if (parent != null && !ManagedLibraryMappingPaths.IsWindows(parent))
                return await ReadPosixChildrenAsync(connection, table, parent, after, limit, ct);
            if (parent == null && await TryReadPosixRootsAsync(connection, table, after, limit, anchors, ct) is { } fastRoots)
                return fastRoots;
            var anchorCases = new StringBuilder("CASE WHEN 0 THEN dir ");
            var index = 0;
            foreach (var anchor in anchors.Roots.Concat(anchors.Marks))
            {
                var parameter = "$a" + index++;
                command.Parameters.AddWithValue(parameter, anchor);
                anchorCases.Append($"WHEN CASE WHEN win=1 THEN ('w' || dir) COLLATE {Collation} = ('w' || {parameter}) COLLATE {Collation} OR ('w' || substr(dir,1,length(rtrim({parameter},'/'))+1)) COLLATE {Collation} = ('w' || rtrim({parameter},'/') || '/') COLLATE {Collation} ELSE dir = {parameter} OR substr(dir,1,length(rtrim({parameter},'/'))+1) = (rtrim({parameter},'/') || '/') END THEN {parameter} ");
            }
            // Unmounted metadata starts at drive/UNC roots or the first two POSIX segments.
            anchorCases.Append("ELSE CASE WHEN win = 1 AND substr(dir,2,1) = ':' THEN substr(dir,1,3) ");
            anchorCases.Append("WHEN instr(substr(dir, CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/')=0 THEN dir ");
            anchorCases.Append("ELSE CASE WHEN instr(substr(dir, instr(substr(dir,CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/') + CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/')=0 THEN dir ");
            anchorCases.Append("ELSE substr(dir,1, instr(substr(dir,CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/') + CASE WHEN substr(dir,1,2)='//' THEN 1 ELSE 0 END + instr(substr(dir, instr(substr(dir,CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/') + CASE WHEN substr(dir,1,2)='//' THEN 3 ELSE 2 END),'/')) END END END");
            // POSIX parent reads use the existing Path index and a strict separator boundary. Windows
            // metadata may contain either slash and casing: those branches use the bounded aggregate
            // and a Windows ordinal collation instead of incorrectly dropping matching resources.
            var prefilter = parent != null && !ManagedLibraryMappingPaths.IsWindows(parent)
                ? " AND (Path = $parent OR Path >= $prefix AND Path < $upper)" : "";
            command.Parameters.AddWithValue("$parent", parent ?? "");
            command.Parameters.AddWithValue("$prefix", parent?.TrimEnd('/') + "/" ?? "");
            command.Parameters.AddWithValue("$upper", parent?.TrimEnd('/') + "0" ?? "");
            command.Parameters.AddWithValue("$after", after);
            command.Parameters.AddWithValue("$limit", limit);
            var childExpression = parent == null ? anchorCases.ToString() :
                "CASE WHEN instr(substr(dir,length(rtrim($parent,'/'))+2),'/')=0 THEN dir " +
                "ELSE rtrim($parent,'/') || '/' || substr(substr(dir,length(rtrim($parent,'/'))+2),1,instr(substr(dir,length(rtrim($parent,'/'))+2),'/')-1) END";
            var parentFilter = parent == null ? "" :
                $"AND win={(ManagedLibraryMappingPaths.IsWindows(parent) ? 1 : 0)} AND CASE WHEN win=1 THEN ('w'||dir) COLLATE {Collation} <> ('w'||$parent) COLLATE {Collation} AND ('w'||substr(dir,1,length(rtrim($parent,'/'))+1)) COLLATE {Collation} = ('w'||rtrim($parent,'/')||'/') COLLATE {Collation} ELSE dir<>$parent AND substr(dir,1,length(rtrim($parent,'/'))+1) = rtrim($parent,'/')||'/' END";
            command.CommandText = $"""
                WITH normalized AS (
                  SELECT IsFile, CASE WHEN substr(Path,2,1)=':' OR substr(Path,1,2)=char(92)||char(92) OR substr(Path,1,2)='//' THEN 1 ELSE 0 END AS win,
                    CASE WHEN substr(Path,2,1)=':' OR substr(Path,1,2)=char(92)||char(92) OR substr(Path,1,2)='//' THEN replace(Path,char(92),'/') ELSE Path END AS p
                  FROM "{table}" WHERE Path IS NOT NULL AND length(Path) BETWEEN 1 AND 4096 {prefilter}
                ), directories AS (
                  SELECT win, CASE WHEN IsFile=1 THEN
                      CASE WHEN length(rtrim(p,replace(p,'/','')))=1 THEN '/'
                        WHEN length(rtrim(rtrim(p,replace(p,'/','')),'/'))=2 AND substr(p,2,1)=':' THEN substr(p,1,2)||'/'
                        ELSE rtrim(rtrim(p,replace(p,'/','')),'/') END
                    ELSE CASE WHEN p='/' THEN '/' WHEN length(rtrim(p,'/'))=2 AND substr(p,2,1)=':' THEN substr(p,1,2)||'/' ELSE rtrim(p,'/') END END AS dir
                  FROM normalized
                  WHERE (substr(p,1,1)='/' OR (win=1 AND p GLOB '[A-Za-z]:/*'))
                    AND instr(p,char(0))=0 AND instr('/'||p||'/','/../')=0 AND instr('/'||p||'/','/./')=0
                    AND substr(p,1,3)<>'///' AND instr(substr(p,3),'//')=0
                ), children AS (
                  SELECT win, dir, {childExpression} AS child FROM directories WHERE dir<>'' {parentFilter}
                ), keyed AS NOT MATERIALIZED (
                  SELECT win, dir, child, (CASE WHEN win=1 THEN 'w' ELSE 'p' END || child) AS k
                  FROM children WHERE child COLLATE BINARY<>''
                ), posix_page AS (
                  -- CASE can inherit COLLATE from its Windows condition even on a POSIX row.
                  -- Explicit BINARY keeps the million-row POSIX aggregate inside SQLite instead
                  -- of marshalling every comparison into the managed Windows callback.
                  SELECT min(child COLLATE BINARY) AS path, min(k COLLATE BINARY) AS key,
                    count(*) AS resource_count, max(dir COLLATE BINARY<>child COLLATE BINARY) AS children
                  FROM keyed WHERE win=0 AND k COLLATE BINARY > $after COLLATE BINARY
                  GROUP BY k COLLATE BINARY ORDER BY k COLLATE BINARY LIMIT $limit
                ), windows_page AS (
                  SELECT min(child COLLATE BINARY) AS path, min(k COLLATE BINARY) AS key, count(*) AS resource_count,
                    max(('w'||dir) COLLATE {Collation} <> ('w'||child) COLLATE {Collation}) AS children
                  FROM keyed WHERE win=1 AND k COLLATE {Collation} > $after COLLATE {Collation}
                  GROUP BY k COLLATE {Collation} ORDER BY k COLLATE {Collation} LIMIT $limit
                )
                SELECT path,key,resource_count,children FROM
                  (SELECT * FROM posix_page UNION ALL SELECT * FROM windows_page)
                ORDER BY key COLLATE {Collation} LIMIT $limit
                """;
            var result = new List<Row>(limit);
            try
            {
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var path = reader.GetString(0);
                    if (ManagedLibraryMappingPaths.Normalize(path) != path)
                        throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
                    result.Add(new(path, reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3) != 0));
                }
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 9 && ct.IsCancellationRequested)
            { throw new OperationCanceledException("The directory metadata read was cancelled.", e, ct); }
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 9 && ct.IsCancellationRequested)
        { throw new OperationCanceledException("The directory metadata read was cancelled.", e, ct); }
        finally { if (!wasOpen) await connection.CloseAsync(); }
    }

    // Keep the common mounted POSIX case on the existing binary Path index. Unlike normalizing
    // every owned path and repeating a CASE for every aggregate expression, these reads only
    // parse the suffix needed for this page. Windows/UNC and uncovered logical roots retain the
    // general metadata parser above, with their different case and separator semantics.
    private const string ValidPosixPath = "length(Path) BETWEEN 1 AND 4096 AND instr(Path,char(0))=0 " +
        "AND instr('/'||Path||'/','/../')=0 AND instr('/'||Path||'/','/./')=0 AND instr(Path,'//')=0";

    private static async Task<List<Row>> ReadPosixChildrenAsync(SqliteConnection connection, string table,
        string parent, string after, int limit, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        var prefix = parent.TrimEnd('/') + "/";
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$upper", parent.TrimEnd('/') + "0");
        command.Parameters.AddWithValue("$after", after);
        command.Parameters.AddWithValue("$afterPath", after.StartsWith('p') ? after[1..] : "");
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = $"""
            WITH tails AS (
              SELECT IsFile, substr(CASE WHEN IsFile=1 THEN Path ELSE rtrim(Path,'/') END,length($prefix)+1) AS tail
              FROM "{table}" WHERE Path >= $prefix COLLATE BINARY AND Path < $upper COLLATE BINARY
                AND Path > $afterPath COLLATE BINARY AND {ValidPosixPath}
            ), segments AS (
              SELECT IsFile,tail,instr(tail,'/') AS split FROM tails
            ), children AS (
              SELECT $prefix || CASE WHEN split=0 THEN tail ELSE substr(tail,1,split-1) END AS child,
                CASE WHEN IsFile=1 THEN instr(substr(tail,split+1),'/')>0 ELSE split>0 END AS has_children
              FROM segments WHERE tail<>'' AND (IsFile<>1 OR split>0)
            )
            SELECT min(child COLLATE BINARY), 'p'||min(child COLLATE BINARY), count(*), max(has_children)
            FROM children WHERE ('p'||child) COLLATE BINARY > $after COLLATE BINARY
            GROUP BY child COLLATE BINARY ORDER BY child COLLATE BINARY LIMIT $limit
            """;
        return await ReadRowsFromCommandAsync(command, ct);
    }

    private static async Task<List<Row>?> TryReadPosixRootsAsync(SqliteConnection connection, string table,
        string after, int limit, AnchorSet anchors, CancellationToken ct)
    {
        // Mark anchors cannot affect rows already covered by a storage root. First try that small
        // mount list; only include marks if they are needed to cover the owned metadata.
        var storageRoots = anchors.Roots.Where(x => !ManagedLibraryMappingPaths.IsWindows(x)).ToArray();
        var allRoots = storageRoots.Concat(anchors.Marks.Where(x => !ManagedLibraryMappingPaths.IsWindows(x)))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var candidates in new[] { storageRoots, allRoots })
        {
            if (candidates.Length is 0 or > 32) continue;
            using var coverage = connection.CreateCommand();
            var memberships = new List<string>(candidates.Length);
            for (var i = 0; i < candidates.Length; i++)
                memberships.Add(AddPosixMembership(coverage, candidates[i], i));
            coverage.CommandText = $"SELECT 1 FROM \"{table}\" WHERE Path IS NOT NULL AND length(Path) BETWEEN 1 AND 4096 AND NOT (" +
                                   string.Join(" OR ", memberships) + ") LIMIT 1";
            if (await coverage.ExecuteScalarAsync(ct) != null) continue;

            var result = new List<Row>();
            for (var i = 0; i < candidates.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var key = "p" + candidates[i];
                if (CompareKeys(key, after) <= 0) continue;
                using var count = connection.CreateCommand();
                var own = AddPosixMembership(count, candidates[i], i);
                var preceding = new List<string>();
                for (var j = 0; j < i; j++)
                    if (ManagedLibraryMappingPaths.Within(candidates[j], candidates[i]) ||
                        ManagedLibraryMappingPaths.Within(candidates[i], candidates[j]))
                        preceding.Add(AddPosixMembership(count, candidates[j], j));
                var exclusion = preceding.Count == 0 ? "" : " AND NOT (" + string.Join(" OR ", preceding) + ")";
                count.CommandText = $"""
                    SELECT $r{i}, 'p'||$r{i}, count(*), coalesce(max(CASE WHEN IsFile=1
                      THEN instr(substr(Path,length($p{i})+1),'/')>0 ELSE rtrim(Path,'/')<>$r{i} COLLATE BINARY END),0)
                    FROM "{table}" WHERE {own} {exclusion} AND {ValidPosixPath} HAVING count(*)>0
                    """;
                result.AddRange(await ReadRowsFromCommandAsync(count, ct));
            }
            return result.OrderBy(x => x.Key, Comparer<string>.Create(CompareKeys)).Take(limit).ToList();
        }
        return null;
    }

    private static string AddPosixMembership(SqliteCommand command, string root, int index)
    {
        command.Parameters.AddWithValue("$r" + index, root);
        command.Parameters.AddWithValue("$p" + index, root.TrimEnd('/') + "/");
        command.Parameters.AddWithValue("$u" + index, root.TrimEnd('/') + "0");
        return $"((Path=$r{index} COLLATE BINARY AND IsFile<>1) OR (Path >= $p{index} COLLATE BINARY AND Path < $u{index} COLLATE BINARY))";
    }

    private static async Task<List<Row>> ReadRowsFromCommandAsync(SqliteCommand command, CancellationToken ct)
    {
        var result = new List<Row>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var path = reader.GetString(0);
            if (ManagedLibraryMappingPaths.Normalize(path) != path)
                throw new FederationQueryException("MappingTreeMetadataTooLarge", 503);
            result.Add(new(path, reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3) != 0));
        }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    private static int CompareKeys(string? left, string? right) => left?.StartsWith('w') == true && right?.StartsWith('w') == true
        ? StringComparer.OrdinalIgnoreCase.Compare(left, right) : left == null || right == null
            ? StringComparer.Ordinal.Compare(left, right)
            // SQLite BINARY compares UTF-8 bytes, unlike .NET Ordinal's UTF-16 code units.
            // Keeping the merge/cursor order identical matters for private-use vs astral names.
            : Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));
    private sealed record AnchorSet(string[] Roots, string[] Marks);
    private sealed record Row(string Path, string Key, long Count, bool HasChildren);
}
