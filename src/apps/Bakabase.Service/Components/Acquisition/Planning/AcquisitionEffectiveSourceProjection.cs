using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Read-only credential projection. The creation plan and its history stay immutable.
/// A typed execution checkpoint supplies only revisions of the exact original locator.</summary>
internal static class AcquisitionEffectiveSourceProjection
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DownloadWorkset? Read(string? checkpoint)
    {
        try
        {
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(checkpoint);
            return HasValidShape(workset) ? workset : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    public static AcquisitionPlan Apply(AcquisitionPlan plan, DownloadWorkset? workset)
    {
        if (!HasValidShape(workset) || plan.Parts == null || plan.Parts.Count > 100 ||
            plan.Parts.Any(p => p == null || p.Sources == null || p.Sources.Count > 100 || p.Sources.Any(s => s == null))) return plan;
        return plan with {Parts = plan.Parts.Select(part =>
        {
            var targets = workset!.Targets.Where(t => t.Id == part.Id).ToArray();
            if (targets.Length != 1) return part;
            return part with {Sources = part.Sources.Select(source =>
            {
                var candidates = targets[0].Sources.Where(s => s.Id == source.Id && MatchesLocator(source, s)).ToArray();
                if (candidates.Length != 1) return source;
                var current = candidates[0];
                if (current.AccessCodeRevisions is not {Count: > 0 and <= 32} revisions) return source;
                var code = source.AccessCode;
                foreach (var revision in revisions)
                {
                    if (revision.PreviousAccessCode != code) return source;
                    code = revision.NewAccessCode;
                }
                if (code != current.AccessCode) return source;
                return source.Url?.Trim() == current.Url ? source with {AccessCode = code} : source with
                {
                    AccessCode = code, Url = current.Url, OriginalUrl = current.OriginalUrl,
                    ResolutionChain = current.ResolutionChain.ToList(), ProviderId = current.ProviderId, Kind = current.Kind
                };
            }).ToList()};
        }).ToList()};
    }

    private static bool HasValidShape(DownloadWorkset? workset) => workset?.Targets is {Count: <= 100} targets &&
        targets.All(t => t != null && t.Sources is {Count: <= 100} sources && sources.All(s => s != null &&
            !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Url) && s.Url.Length <= 8192 &&
            (s.AccessCodeRevisions == null || s.AccessCodeRevisions.Count <= 32 && s.AccessCodeRevisions.All(r => r != null &&
                r.PreviousAccessCode?.Length is not > 512 && r.NewAccessCode?.Length is not > 512))));

    private static bool MatchesLocator(AcquisitionPlanSource original, CloudSource current)
    {
        if (string.IsNullOrWhiteSpace(original.Url)) return false;
        if (original.Url.Trim() == current.Url) return true;
        // A stored resolver chain is evidence for this exact original locator. Never resolve
        // again or infer a relationship from a shared ID, host or similar-looking address.
        if (current.OriginalUrl != original.Url.Trim() ||
            current.ResolutionChain is not {Count: >= 2} chain || chain.Count > HttpRedirectSourceResolver.MaximumRedirects + 1 ||
            chain[0] != current.OriginalUrl || chain[^1] != current.Url ||
            chain.Distinct(StringComparer.Ordinal).Count() != chain.Count ||
            original.ProviderId is not (null or "unknown") && original.ProviderId != current.ProviderId ||
            original.Kind is not ("auto" or "share") && original.Kind != current.Kind ||
            original.MethodId != null && original.MethodId != current.MethodId ||
            original.AccountId != null && original.AccountId != current.AccountId) return false;
        try
        {
            string? scheme = null;
            foreach (var locator in chain)
            {
                if (string.IsNullOrWhiteSpace(locator) || locator.Length > 8192 ||
                    !Uri.TryCreate(locator, UriKind.Absolute, out var uri)) return false;
                PublicCloudHttp.EnsureSafeUri(uri);
                scheme ??= uri.Scheme;
                if (uri.Scheme != scheme) return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    public static string? Apply(string? planJson, DownloadWorkset? workset)
    {
        if (workset == null || string.IsNullOrWhiteSpace(planJson) || planJson.Length > 1024 * 1024) return planJson;
        try
        {
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(planJson, Json);
            return plan == null ? planJson : JsonSerializer.Serialize(Apply(plan, workset), Json);
        }
        catch (JsonException) { return planJson; }
    }

    /// <summary>Overlay source display fields without replacing runtime file/task metadata or
    /// hiding other targets when completed-source evidence contains only corrected targets.</summary>
    public static List<CloudDownloadTarget> ApplyDisplaySources(List<CloudDownloadTarget> targets, DownloadWorkset? corrections)
    {
        if (corrections == null || !HasValidShape(new() {Targets = targets})) return targets;
        var plan = new AcquisitionPlan {Parts = targets.Select(t => new AcquisitionPlanPart {Id = t.Id,
            Sources = t.Sources.Select(s => new AcquisitionPlanSource {Id = s.Id, Url = s.Url, Kind = s.Kind,
                ProviderId = s.ProviderId, MethodId = s.MethodId, AccountId = s.AccountId, AccessCode = s.AccessCode,
                OriginalUrl = s.OriginalUrl, ResolutionChain = s.ResolutionChain}).ToList()}).ToList()};
        var effective = Apply(plan, corrections);
        return targets.Select((target, i) => target with {Sources = target.Sources.Select((source, j) =>
        {
            var projected = effective.Parts[i].Sources[j];
            return source with {Url = projected.Url, AccessCode = projected.AccessCode, ProviderId = projected.ProviderId ?? source.ProviderId,
                Kind = projected.Kind, OriginalUrl = projected.OriginalUrl, ResolutionChain = projected.ResolutionChain ?? source.ResolutionChain};
        }).ToList()}).ToList();
    }

    /// <summary>Do not load every task's potentially large checkpoint for list filtering.
    /// Existing correction operation bindings and the compact serialized revision marker
    /// narrow reads to runs which actually have credential history, including pre-operation waits.</summary>
    public static async Task<IReadOnlyDictionary<int, DownloadWorkset?>> ReadCorrectedRunsAsync(
        BakabaseDbContext db, IReadOnlyList<int> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new Dictionary<int, DownloadWorkset?>();
        var owners = runIds.Select(id => id.ToString()).ToArray();
        var bound = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && owners.Contains(b.OwnerId) && b.Slot.Contains(":credential:"))
            .Select(b => b.OwnerId).Distinct().ToArrayAsync(ct);
        var rows = await db.WorkflowRuns.AsNoTracking().Where(r => runIds.Contains(r.Id) && r.CurrentItemJson != null &&
                (bound.Contains(r.Id.ToString()) || r.CurrentItemJson.Contains("\"accessCodeRevisions\":[")))
            .Select(r => new {r.Id, r.CurrentItemJson}).ToArrayAsync(ct);
        var result = rows.ToDictionary(r => r.Id, r => Read(r.CurrentItemJson));
        var missing = runIds.Where(id => !result.ContainsKey(id)).ToArray();
        foreach (var (id, completed) in await AcquisitionCompletedSourceProjection.ReadAsync(db, missing, ct))
            if (HasValidShape(completed)) result[id] = completed;
        return result;
    }
}
