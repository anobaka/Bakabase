using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed class MetadataSourceJournalGate
{
    internal readonly SemaphoreSlim Gate = new(1);
    internal bool Initialized;
}

public sealed record MetadataSourceHead(string Generation, long Revision, long CatalogRevision);
public sealed record MetadataSourceChange(int ResourceId, long Revision);

/// <summary>
/// Durable dirty rows, not a process-local notification queue. A trigger records the last revision of
/// each resource in the same transaction as its data, including raw SQL/bulk writes and deletions.
/// Storage is bounded by resource identities, not the number of edits. No payload is duplicated.
/// </summary>
public sealed class MetadataSourceJournal(BakabaseDbContext db, MetadataSourceJournalGate initialization)
{
    // Definition changes have a catalog revision; only changes that can invalidate existing
    // normalized postings also change the structural generation.
    private static readonly string[] GlobalTables = ["CustomProperties", "AliasesV2", "ExtensionGroups",
        "MediaLibrariesV2", "Collections", "ResourceProfiles", "ResourceScopes", "HealthScoreProfiles", "SourceMetadataMappings"];
    private static readonly string[] ResourceTables = ["CustomPropertyValues", "ReservedPropertyValues",
        "PropertyValueScopePreferences", "MediaLibraryResourceMappings", "CollectionResourceMappings",
        "ResourceSourceLinks", "ResourceExternalIdentities", "ResourceHealthScores", "ResourceCaches",
        "PlayHistories", "Enhancements", "EnhancementRecords", "PropertyMarkEffects", "SteamApps",
        "DLsiteWorks", "ExHentaiGalleries"];

    public async Task EnsureAsync(CancellationToken ct)
    {
        await initialization.Gate.WaitAsync(ct);
        try
        {
            if (initialization.Initialized) return;
            // Installed lazily on the first authorized metadata read. A device that never shares its
            // library does not pay for tracking. DDL + clock activation are one atomic transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO MetadataSourceClocks (Id, Generation, Revision) VALUES (1, {Guid.NewGuid().ToString("N")}, 0)", ct);
            var tables = db.Model.GetEntityTypes().Where(e => e.GetTableName() != null)
                .ToDictionary(e => e.GetTableName()!, e => e.GetProperties().Select(p => p.Name).ToHashSet());
            await InstallAsync("ResourcesV2", "Id", ct);
            foreach (var table in ResourceTables)
                if (tables.TryGetValue(table, out var columns) && columns.Contains("ResourceId"))
                    await InstallAsync(table, "ResourceId", ct);
            foreach (var table in GlobalTables)
                if (tables.ContainsKey(table)) await InstallAsync(table, null, ct);
            await transaction.CommitAsync(ct);
            initialization.Initialized = true;
        }
        finally { initialization.Gate.Release(); }
    }

    private async Task InstallAsync(string table, string? column, CancellationToken ct)
    {
        // Identifiers come only from the static table list and EF model, never from a request.
        foreach (var operation in new[] {"INSERT", "UPDATE", "DELETE"})
        {
            var records = operation == "UPDATE" && column != null
                ? new[] {$"OLD.\"{column}\"", $"NEW.\"{column}\""}
                : new[] {column == null ? "0" : $"{(operation == "DELETE" ? "OLD" : "NEW")}.\"{column}\""};
            var changes = string.Join("\n", records.Select(id =>
                $"UPDATE MetadataSourceClocks SET Revision = Revision + 1 WHERE Id = 1;\nINSERT INTO MetadataSourceChanges (ResourceId, Revision) SELECT COALESCE({id}, 0), Revision FROM MetadataSourceClocks WHERE Id = 1 ON CONFLICT(ResourceId) DO UPDATE SET Revision = excluded.Revision;"));
            if (column == null)
            {
                // -1 is a structural watermark, 0 is the catalog watermark. Append-only
                // reference options leave every existing ID/value intact, so enhancement can
                // add tags continuously without restarting a large resource snapshot.
                var benign = table == "CustomProperties" && operation == "UPDATE"
                    ? CustomPropertyCatalogOnly : "0";
                changes += $"\nINSERT INTO MetadataSourceChanges (ResourceId, Revision) SELECT -1, Revision FROM MetadataSourceClocks WHERE Id=1 AND NOT COALESCE(({benign}),0) ON CONFLICT(ResourceId) DO UPDATE SET Revision=excluded.Revision;";
            }
            await db.Database.ExecuteSqlRawAsync($"""
                DROP TRIGGER IF EXISTS "metadata_v1_{table}_{operation}";
                DROP TRIGGER IF EXISTS "metadata_v2_{table}_{operation}";
                CREATE TRIGGER "metadata_v2_{table}_{operation}" AFTER {operation} ON "{table}"
                BEGIN
                    {changes}
                END;
                """, ct);
        }
    }

    // Keep this conservative: type changes, removed/edited reference entries, defaults,
    // ignore-case behavior and unknown option shapes still rebuild. Both JSON casings are
    // supported because stored settings predate the current web serializer.
    private static string CustomPropertyCatalogOnly => $"""
        OLD.Id=NEW.Id AND OLD.Type=NEW.Type
        AND (OLD.Name IS NEW.Name OR NOT EXISTS (
          SELECT 1 FROM ResourceProfiles WHERE (SourceDeviceId IS NULL OR SourceDeviceId='')
          AND (instr(NameTemplate,char(123)||OLD.Name||char(125))>0 OR
               instr(NameTemplate,char(123)||NEW.Name||char(125))>0)))
        AND (OLD.Options IS NEW.Options OR
          (CASE WHEN OLD.Type IN (3,4,16) AND json_valid(OLD.Options) AND json_valid(NEW.Options) THEN
           json_remove(OLD.Options,'$.Choices','$.choices','$.Tags','$.tags') =
               json_remove(NEW.Options,'$.Choices','$.choices','$.Tags','$.tags')
           AND NOT EXISTS (
             SELECT value FROM json_each(COALESCE(json_extract(OLD.Options,'$.Choices'),json_extract(OLD.Options,'$.choices'),json_extract(OLD.Options,'$.Tags'),json_extract(OLD.Options,'$.tags'),'[]'))
             EXCEPT
             SELECT value FROM json_each(COALESCE(json_extract(NEW.Options,'$.Choices'),json_extract(NEW.Options,'$.choices'),json_extract(NEW.Options,'$.Tags'),json_extract(NEW.Options,'$.tags'),'[]'))
           ) ELSE 0 END))
        """;

    public async Task<MetadataSourceHead> HeadAsync(CancellationToken ct)
    {
        await EnsureAsync(ct);
        // One statement gives a consistent clock/catalog pair without holding a write transaction.
        var head = await db.MetadataSourceClocks.AsNoTracking().Where(c => c.Id == 1)
            .Select(c => new {c.Generation, c.Revision,
                Catalog = db.MetadataSourceChanges.Where(r => r.ResourceId == 0).Select(r => (long?) r.Revision).FirstOrDefault() ?? 0,
                Structure = db.MetadataSourceChanges.Where(r => r.ResourceId == -1).Select(r => (long?) r.Revision).FirstOrDefault() ?? 0})
            .SingleAsync(ct);
        var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"metadata-v2:{head.Generation}:{head.Structure}"))).ToLowerInvariant();
        return new(generation, head.Revision, head.Catalog);
    }

    public async Task<IReadOnlyList<MetadataSourceChange>> ChangesAsync(long after, long through, int take, CancellationToken ct)
    {
        if (after < 0 || through < after || take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(after));
        return await db.MetadataSourceChanges.AsNoTracking().Where(c => c.ResourceId > 0 && c.Revision > after && c.Revision <= through)
            .OrderBy(c => c.Revision).ThenBy(c => c.ResourceId)
            .Take(take).Select(c => new MetadataSourceChange(c.ResourceId, c.Revision)).ToArrayAsync(ct);
    }

    public async Task<int[]> ResourceIdsAsync(int after, int take, CancellationToken ct) =>
        await db.ResourcesV2.AsNoTracking().Where(r => r.Id > after).OrderBy(r => r.Id)
            .Select(r => r.Id).Take(take).ToArrayAsync(ct);

    public async Task<Dictionary<int, long>> RevisionsAsync(int[] ids, CancellationToken ct) =>
        // Keep this an Enumerable call: newer compilers prefer the span overload for
        // arrays, which EF cannot translate or evaluate inside the query expression.
        await db.MetadataSourceChanges.AsNoTracking().Where(c => Enumerable.Contains(ids, c.ResourceId))
            .ToDictionaryAsync(c => c.ResourceId, c => c.Revision, ct);
}
