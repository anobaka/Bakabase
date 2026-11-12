using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Read-only source history after a successful run clears its checkpoint. These sources
/// come from exact durable operation bindings, not output previews. Never use this projection as
/// an execution checkpoint or as authorization to resume, transfer, or download anything.</summary>
internal static class AcquisitionCompletedSourceProjection
{
    private const int MaximumBindingsPerRun = 1024;
    private const int MaximumRequestCharacters = 1024 * 1024;
    private const long MaximumRunRequestCharacters = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyDictionary<int, DownloadWorkset>> ReadAsync(BakabaseDbContext db,
        IReadOnlyList<int> runIds, CancellationToken ct)
    {
        var result = new Dictionary<int, DownloadWorkset>();
        // Keep SQL parameter counts and the number of candidate immutable requests bounded.
        foreach (var ids in runIds.Distinct().Chunk(25))
        {
            var completed = await db.WorkflowRuns.AsNoTracking().Where(r => ids.Contains(r.Id) &&
                    r.Status == WorkflowRunStatus.Success && (r.CurrentItemJson == null || r.CurrentItemJson == ""))
                .Select(r => r.Id).ToArrayAsync(ct);
            if (completed.Length == 0) continue;
            var owners = completed.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var query = db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b =>
                b.OwnerKind == "workflow" && owners.Contains(b.OwnerId) &&
                (b.Slot.Contains(":transfer:credential:") || b.Slot.Contains(":download:credential:")));
            var counts = await query.GroupBy(b => b.OwnerId).Select(g => new {Owner = g.Key, Count = g.Count()}).ToArrayAsync(ct);
            var eligible = counts.Where(c => c.Count <= MaximumBindingsPerRun).Select(c => c.Owner).ToArray();
            if (eligible.Length == 0) continue;
            var bindings = await query.Where(b => eligible.Contains(b.OwnerId)).ToArrayAsync(ct);
            var operationIds = bindings.Select(b => b.OperationId).Distinct().ToArray();
            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var batch in operationIds.Chunk(100))
                foreach (var size in await db.CloudDownloadOperations.AsNoTracking().Where(o => batch.Contains(o.Id))
                             .Select(o => new {o.Id, Length = o.RequestJson.Length}).ToArrayAsync(ct)) sizes[size.Id] = size.Length;
            var boundedOwners = bindings.GroupBy(b => b.OwnerId).Where(g =>
                    g.Select(b => b.OperationId).Distinct().Sum(id => (long)sizes.GetValueOrDefault(id)) <= MaximumRunRequestCharacters)
                .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            bindings = bindings.Where(b => boundedOwners.Contains(b.OwnerId)).ToArray();
            operationIds = bindings.Select(b => b.OperationId).Distinct().ToArray();
            var operations = new Dictionary<string, CloudOperationDbModel>(StringComparer.Ordinal);
            foreach (var batch in operationIds.Chunk(100))
                foreach (var operation in await db.CloudDownloadOperations.AsNoTracking()
                             .Where(o => batch.Contains(o.Id) && o.RequestJson.Length <= MaximumRequestCharacters).ToArrayAsync(ct))
                    operations[operation.Id] = operation;
            foreach (var group in bindings.GroupBy(b => b.OwnerId))
            {
                var candidates = new Dictionary<string, List<CloudSource>>(StringComparer.Ordinal);
                var blocked = new HashSet<string>(StringComparer.Ordinal);
                var malformedSlot = false;
                foreach (var binding in group)
                {
                    if (!ReadSlot(binding.Slot, out var targetId, out var kind, out var correctionId))
                    { malformedSlot = true; break; }
                    var source = operations.TryGetValue(binding.OperationId, out var operation)
                        ? ReadSource(operation, kind, correctionId) : null;
                    if (source == null) { blocked.Add(targetId); continue; }
                    if (!candidates.TryGetValue(targetId, out var targetSources))
                        candidates[targetId] = targetSources = [];
                    targetSources.Add(source);
                }
                if (malformedSlot) continue;
                var targets = new List<CloudDownloadTarget>();
                foreach (var (targetId, sources) in candidates)
                {
                    if (blocked.Contains(targetId)) continue;
                    var source = LongestCompatible(sources);
                    if (source != null) targets.Add(new() {Id = targetId, Sources = [source], SelectedSourceId = source.Id});
                }
                if (targets.Count > 0 && int.TryParse(group.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var runId))
                    result[runId] = new() {Id = "completed-source-display:" + group.Key, Targets = targets};
            }
        }
        return result;
    }

    private static bool ReadSlot(string slot, out string targetId, out string kind, out string correctionId)
    {
        targetId = kind = correctionId = "";
        foreach (var candidate in new[] {"transfer", "download"})
        {
            var marker = ":" + candidate + ":credential:";
            var index = slot.LastIndexOf(marker, StringComparison.Ordinal);
            if (index <= 0 || index > 512) continue;
            var suffix = slot[(index + marker.Length)..];
            if (!Guid.TryParseExact(suffix, "N", out _)) continue;
            targetId = slot[..index]; kind = candidate; correctionId = suffix;
            return true;
        }
        return false;
    }

    private static CloudSource? ReadSource(CloudOperationDbModel operation, string kind, string correctionId)
    {
        if (operation.Kind != kind || string.IsNullOrWhiteSpace(operation.RequestJson)) return null;
        try
        {
            CloudSource? source;
            string? requestId;
            if (kind == "transfer")
            {
                var request = JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json);
                source = request?.Source; requestId = request?.OperationId;
            }
            else
            {
                var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, Json);
                source = request?.Source; requestId = request?.OperationId;
            }
            if (requestId != operation.Id || source == null || string.IsNullOrWhiteSpace(source.Id) ||
                string.IsNullOrWhiteSpace(source.Url) || source.Kind != "share" ||
                source.AccessCodeRevisions is not {Count: > 0 and <= 32} revisions ||
                revisions.Any(r => r == null) || revisions[^1].CorrectionId != correctionId ||
                revisions.Select(r => r.CorrectionId).Distinct(StringComparer.Ordinal).Count() != revisions.Count) return null;
            var code = revisions[0].PreviousAccessCode;
            foreach (var revision in revisions)
            {
                if (!Guid.TryParseExact(revision.CorrectionId, "N", out _) || revision.PreviousAccessCode != code ||
                    revision.Basis is not ("manual" or "sourceEvidence") ||
                    revision.NewAccessCode?.Length > 512 || revision.NewAccessCode?.Any(char.IsControl) == true ||
                    revision.ExpectedSourceIdentity != CloudSourceIdentity.Key(source with {AccessCode = code})) return null;
                code = revision.NewAccessCode;
            }
            return code == source.AccessCode ? source : null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }

    private static CloudSource? LongestCompatible(IReadOnlyList<CloudSource> sources)
    {
        var longest = sources.OrderByDescending(s => s.AccessCodeRevisions!.Count).First();
        var identity = WithoutCredentials(longest);
        return sources.All(s => WithoutCredentials(s) == identity &&
                s.AccessCodeRevisions!.SequenceEqual(longest.AccessCodeRevisions!.Take(s.AccessCodeRevisions.Count)))
            ? longest : null;
    }

    private static string WithoutCredentials(CloudSource source) =>
        JsonSerializer.Serialize(source with {AccessCode = null, AccessCodeRevisions = null}, Json);
}
