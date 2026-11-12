using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Measured, current-run evidence only. No file inspection, provider probes, runtime
/// downloader initialization or historical run scans. Bounded manifests stay inside SQLite;
/// only compact part identities and byte totals are materialized.</summary>
public sealed class AcquisitionSizeEstimateProjection(BakabaseDbContext db)
{
    public async Task<Dictionary<int, AcquisitionSizeEstimate>> ReadAsync(IReadOnlyCollection<int> taskIds,
        CancellationToken ct = default)
    {
        var result = taskIds.Distinct().ToDictionary(id => id, _ => new AcquisitionSizeEstimate(null, "unknown"));
        foreach (var batch in result.Keys.ToArray().Chunk(50))
        {
            var parts = await db.Database.SqlQueryRaw<PartFact>(PartsSql, JsonSerializer.Serialize(batch)).ToListAsync(ct);
            var runs = await db.AcquisitionTasks.AsNoTracking().Where(t => batch.Contains(t.Id) && t.DeletedAt == null)
                .Select(t => new {t.Id, t.WorkflowRunId}).ToListAsync(ct);
            var owners = runs.Where(t => t.WorkflowRunId.HasValue).Select(t => t.WorkflowRunId!.Value.ToString()).ToArray();
            var bindings = db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" && owners.Contains(b.OwnerId));
            var counts = await bindings.GroupBy(b => b.OwnerId).Select(g => new {Owner = g.Key, Count = g.Count()}).ToListAsync(ct);
            var bounded = counts.Where(g => g.Count <= 1024).Select(g => g.Owner).ToArray();
            var facts = await (from binding in bindings
                where bounded.Contains(binding.OwnerId)
                join operation in db.ArtifactOperations.AsNoTracking() on binding.OperationId equals operation.Id
                where operation.State == ArtifactOperationState.Completed && operation.OutputArtifactId != null
                select new {binding.OwnerId, binding.Slot, ArtifactId = operation.OutputArtifactId!, operation.UpdatedAt}).ToListAsync(ct);
            var artifacts = new Dictionary<string, long?>(StringComparer.Ordinal);
            foreach (var ids in facts.Select(f => f.ArtifactId).Distinct().Chunk(100))
                foreach (var size in await db.Database.SqlQueryRaw<ArtifactSize>(SizesSql, JsonSerializer.Serialize(ids)).ToListAsync(ct))
                    artifacts[size.Id] = size.Bytes;
            foreach (var task in runs)
            {
                var expected = parts.Where(p => p.TaskId == task.Id).ToArray();
                var knownPlan = expected.Length > 0 && expected.All(p => p.PartId != null) &&
                                expected.Select(p => p.PartId).Distinct().Count() == expected.Length;
                var expectedIds = expected.Where(p => p.PartId != null).Select(p => p.PartId!).ToHashSet(StringComparer.Ordinal);
                var measured = facts.Where(f => f.OwnerId == task.WorkflowRunId?.ToString())
                    .Select(f => (Fact: f, Slot: ReadSlot(f.Slot)))
                    .Where(f => f.Slot != null && (!knownPlan || expectedIds.Contains(f.Slot.PartId)) &&
                                artifacts.GetValueOrDefault(f.Fact.ArtifactId).HasValue)
                    .GroupBy(f => f.Slot!.PartId, StringComparer.Ordinal)
                    .Select(g => g.OrderByDescending(f => f.Slot!.Rank).ThenByDescending(f => f.Fact.UpdatedAt)
                        .ThenBy(f => f.Fact.ArtifactId, StringComparer.Ordinal).First()).ToArray();
                if (measured.Length == 0) continue;
                // Mirrors/processing generations compete within a part; shared artifacts across
                // parts are counted once. Equal content in distinct artifacts is not assumed identical.
                long bytes = 0;
                try
                {
                    foreach (var artifact in measured.Select(m => m.Fact.ArtifactId).Distinct(StringComparer.Ordinal))
                        bytes = checked(bytes + artifacts[artifact]!.Value);
                }
                catch (OverflowException) { continue; }
                var rank = measured.Min(m => m.Slot!.Rank);
                result[task.Id] = new(bytes, rank == 3 ? "delivered" : rank == 2 ? "processed" : "downloadReference",
                    !knownPlan || expectedIds.Count != measured.Length || measured.Any(m => m.Slot!.Partial));
            }
        }
        return result;
    }

    private sealed record Slot(string PartId, int Rank, bool Partial = false);
    private static Slot? ReadSlot(string value)
    {
        foreach (var (suffix, rank) in new[] { (":artifact.publish", 3), (":artifact.process", 2),
                     (":artifact.download", 1), (":artifact.continuation", 1) })
            if (value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length)
                return new(value[..^suffix.Length], rank);
        var index = value.LastIndexOf(":process:", StringComparison.Ordinal);
        return index > 0 && index + 9 < value.Length
            ? new(value[..index], 2, value[(index + 9)..] != "complete") : null;
    }

    public sealed class PartFact
    {
        public int TaskId { get; set; }
        public string? PartId { get; set; }
    }
    public sealed class ArtifactSize
    {
        public string Id { get; set; } = "";
        public long? Bytes { get; set; }
    }

    // No frozen workflow plan, checkpoint or plan history is read. Invalid/oversized plans
    // cannot claim that every required part was measured.
    private const string PartsSql = """
        WITH candidates AS (
            SELECT Id, CASE WHEN length(PlanJson) <= 1048576 AND json_valid(PlanJson)
                THEN PlanJson ELSE '{{}}' END Plan
            FROM AcquisitionTasks WHERE DeletedAt IS NULL AND Id IN (SELECT value FROM json_each({0}))
        )
        SELECT c.Id TaskId, CASE WHEN p.type = 'object' THEN
            CASE WHEN json_type(p.value, '$.id') = 'text' AND length(json_extract(p.value, '$.id')) BETWEEN 1 AND 512
                THEN json_extract(p.value, '$.id') END END PartId
        FROM candidates c LEFT JOIN json_each(CASE WHEN json_type(c.Plan, '$.parts') = 'array'
            AND json_array_length(c.Plan, '$.parts') BETWEEN 1 AND 100 THEN json_extract(c.Plan, '$.parts') ELSE '[]' END) p
        """;

    // Reject oversized/malformed manifests and inconsistent duplicate paths. The per-file
    // bound (over 900 TB) also makes SUM exact and overflow-safe for at most 10,000 entries.
    // An empty, valid measured manifest is zero; missing evidence is always null.
    private const string SizesSql = """
        WITH candidates AS (
            SELECT Id, CASE WHEN length(ManifestJson) <= 1048576 AND json_valid(ManifestJson)
                THEN ManifestJson ELSE '{{}}' END Manifest
            FROM Artifacts WHERE Id IN (SELECT value FROM json_each({0}))
        ), entries AS (
            SELECT c.Id, CASE WHEN e.type = 'object' THEN e.value ELSE '{{}}' END Entry
            FROM candidates c, json_each(CASE WHEN json_type(c.Manifest, '$.entries') = 'array'
                AND json_array_length(c.Manifest, '$.entries') <= 10000
                THEN json_extract(c.Manifest, '$.entries') ELSE '[]' END) e
        ), files AS (
            SELECT Id, json_extract(Entry, '$.relativePath') Path,
                MIN(json_extract(Entry, '$.length')) Minimum, MAX(json_extract(Entry, '$.length')) Maximum
            FROM entries GROUP BY Id, json_extract(Entry, '$.relativePath')
        )
        SELECT c.Id, CASE WHEN json_type(c.Manifest, '$.entries') = 'array'
            AND json_array_length(c.Manifest, '$.entries') <= 10000
            AND NOT EXISTS (SELECT 1 FROM entries e WHERE e.Id = c.Id AND
                (COALESCE(json_type(e.Entry, '$.relativePath'), '') <> 'text'
                 OR length(json_extract(e.Entry, '$.relativePath')) = 0
                 OR COALESCE(json_type(e.Entry, '$.length'), '') <> 'integer'
                 OR json_extract(e.Entry, '$.length') NOT BETWEEN 0 AND 922337203685477))
            AND NOT EXISTS (SELECT 1 FROM files f WHERE f.Id = c.Id AND f.Minimum <> f.Maximum)
            THEN COALESCE((SELECT SUM(f.Maximum) FROM files f WHERE f.Id = c.Id), 0) END Bytes
        FROM candidates c
        """;
}
