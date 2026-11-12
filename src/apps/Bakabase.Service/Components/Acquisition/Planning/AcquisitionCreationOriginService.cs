using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Producer-specific metadata belongs in an adapter; creation provenance is not a download source.</summary>
public interface IAcquisitionCreationOriginProvider
{
    string Module { get; }
    Task<IReadOnlyDictionary<WorkflowSourceRef, AcquisitionCreationOrigin>> ResolveAsync(
        IReadOnlyList<WorkflowSourceRef> references, CancellationToken ct);
}

/// <summary>Batch projection from persisted identities only. Reads never backfill or change producer/task data.</summary>
public sealed class AcquisitionCreationOriginService(BakabaseDbContext db,
    IEnumerable<IAcquisitionCreationOriginProvider> providers, IEnumerable<IAcquisitionPlanBuilder> builders)
    : IAcquisitionCreationOriginResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnrichAsync(IReadOnlyList<AcquisitionTask> tasks, CancellationToken ct = default)
    {
        if (tasks.Count == 0) return;
        var ids = tasks.Select(t => t.Id).Distinct().ToArray();
        var rows = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new {t.Id, t.BuilderId, t.SourceRefsJson, t.BuilderStateJson, t.PreparedContextJson,
                t.AcquisitionLeadId, t.WorkflowRunId}).ToListAsync(ct);
        var leadIds = rows.Where(r => r.AcquisitionLeadId.HasValue).Select(r => r.AcquisitionLeadId!.Value).Distinct().ToArray();
        var leads = await db.Set<AcquisitionLeadDbModel>().AsNoTracking().Where(l => leadIds.Contains(l.Id))
            .Select(l => new {l.Id, l.SourceRefsJson, l.SourceReference, l.Origin}).ToDictionaryAsync(l => l.Id, ct);
        var runIds = rows.Where(r => r.WorkflowRunId.HasValue).Select(r => r.WorkflowRunId!.Value).Distinct().ToArray();
        var runSources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(r => runIds.Contains(r.WorkflowRunId))
            .Select(r => new {r.WorkflowRunId, r.Module, r.EntityKind, r.EntityId, r.Revision}).ToListAsync(ct);
        var handoffs = await db.AcquisitionHandoffs.AsNoTracking().Where(h => h.AcquisitionTaskId.HasValue && ids.Contains(h.AcquisitionTaskId.Value))
            .Select(h => new {TaskId = h.AcquisitionTaskId!.Value, h.SourceModule, h.SourceKind, h.SourceId, h.SourceRevision}).ToListAsync(ct);
        var refsByTask = new Dictionary<int, IReadOnlyList<WorkflowSourceRef>>();
        var snapshotsByTask = new Dictionary<int, IReadOnlyList<AcquisitionCreationOrigin>>();
        foreach (var row in rows)
        {
            var lead = row.AcquisitionLeadId is {} leadId ? leads.GetValueOrDefault(leadId) : null;
            var snapshots = (ReadProperty<List<AcquisitionCreationOrigin>>(row.BuilderStateJson, "creationOrigins") ?? [])
                .Where(s => s != null).ToList();
            var refs = Read<List<WorkflowSourceRef>>(row.SourceRefsJson) ?? [];
            refs.AddRange(ReadProperty<List<WorkflowSourceRef>>(row.BuilderStateJson, "sourceRefs") ?? []);
            refs.AddRange(ReadProperty<List<WorkflowSourceRef>>(row.PreparedContextJson, "sourceRefs") ?? []);
            refs.AddRange(Read<List<WorkflowSourceRef>>(lead?.SourceRefsJson) ?? []);
            refs.AddRange(runSources.Where(r => r.WorkflowRunId == row.WorkflowRunId).Select(r =>
                new WorkflowSourceRef(r.Module, r.EntityKind, r.EntityId, r.Revision)));
            refs.AddRange(handoffs.Where(h => h.TaskId == row.Id).Select(h =>
                new WorkflowSourceRef(h.SourceModule, h.SourceKind, h.SourceId, h.SourceRevision)));
            refs.AddRange(snapshots.Where(s => s.Module != null && s.EntityKind != null && s.EntityId != null)
                .Select(s => new WorkflowSourceRef(s.Module!, s.EntityKind!, s.EntityId!, s.Revision)));
            refsByTask[row.Id] = Normalize(refs, row.Id);
            // Old lead provenance may retain an original post URL without a task identity. Do not guess that identity.
            if (snapshots.Count == 0 && refsByTask[row.Id].Count == 0 && lead?.Origin == AcquisitionLeadOrigin.PostParser &&
                SafeUrl(lead.SourceReference) is {} original)
                snapshots.Add(new() {Kind = "postParser", Module = "postParser", Url = original, SourceState = "snapshot"});
            snapshotsByTask[row.Id] = snapshots;
        }
        var resolved = await ResolveReferencesAsync(refsByTask.Values.SelectMany(r => r).Distinct().ToArray(), ct);
        var taskMap = tasks.ToDictionary(t => t.Id);
        foreach (var row in rows)
        {
            var saved = snapshotsByTask[row.Id];
            var origins = refsByTask[row.Id].Select(reference =>
            {
                var current = resolved.GetValueOrDefault(reference) ?? FromReference(reference);
                var snapshot = saved.FirstOrDefault(s => SameIdentity(s, reference));
                return current with {Title = SafeTitle(snapshot?.Title) ?? current.Title,
                    Url = SafeUrl(snapshot?.Url) ?? current.Url, BuilderId = snapshot?.BuilderId ?? current.BuilderId};
            }).ToList();
            if (origins.Count == 0)
                origins.AddRange(saved.Where(s => s.Module == null || s.EntityId == null).Select(Sanitize));
            if (origins.Count == 0) origins.Add(BuilderOrigin(row.BuilderId, false));
            taskMap[row.Id].CreationOrigins = origins;
        }
    }

    /// <summary>Called only by creation commands; snapshot metadata survives deletion of a producer record.</summary>
    public async Task<IReadOnlyList<AcquisitionCreationOrigin>> CaptureAsync(IReadOnlyList<WorkflowSourceRef> sourceRefs,
        string? builderId, bool manuallyCreated, CancellationToken ct)
    {
        var refs = Normalize(sourceRefs, null);
        if (refs.Count == 0) return [BuilderOrigin(builderId, manuallyCreated)];
        var resolved = await ResolveReferencesAsync(refs, ct);
        return refs.Select(r => Sanitize(resolved.GetValueOrDefault(r) ?? FromReference(r)) with
            {SourceState = "snapshot"}).ToArray();
    }

    private AcquisitionCreationOrigin BuilderOrigin(string? builderId, bool manuallyCreated)
    {
        var builder = builders.FirstOrDefault(b => b.Descriptor.Id == builderId)?.Descriptor;
        return new() {Kind = builder?.InputKind == "title" ? "nameSearch" : manuallyCreated ? "manual" : "unknown",
            BuilderId = builderId, SourceState = "unknown"};
    }

    private async Task<Dictionary<WorkflowSourceRef, AcquisitionCreationOrigin>> ResolveReferencesAsync(
        IReadOnlyList<WorkflowSourceRef> references, CancellationToken ct)
    {
        var result = new Dictionary<WorkflowSourceRef, AcquisitionCreationOrigin>();
        foreach (var group in references.GroupBy(r => r.Module))
        {
            var provider = providers.SingleOrDefault(p => p.Module == group.Key);
            if (provider == null) continue;
            foreach (var pair in await provider.ResolveAsync(group.ToArray(), ct)) result[pair.Key] = Sanitize(pair.Value);
        }
        return result;
    }

    private static IReadOnlyList<WorkflowSourceRef> Normalize(IEnumerable<WorkflowSourceRef> references, int? ownerId) =>
        references.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Module) && r.Module.Length <= 100 &&
            !string.IsNullOrWhiteSpace(r.EntityKind) && r.EntityKind.Length <= 100 &&
            !string.IsNullOrWhiteSpace(r.EntityId) && r.EntityId.Length <= 500 && r.Revision?.Length is not > 100 &&
            !(ownerId.HasValue && r.Module == "acquisition" && r.EntityKind == "task" && r.EntityId == ownerId.Value.ToString(CultureInfo.InvariantCulture)))
            .Select(r => r with {Revision = string.IsNullOrEmpty(r.Revision) ? null : r.Revision, ResultKey = null}).Distinct().ToArray();
    private static bool SameIdentity(AcquisitionCreationOrigin origin, WorkflowSourceRef reference) =>
        origin.Module == reference.Module && origin.EntityKind == reference.EntityKind && origin.EntityId == reference.EntityId &&
        (string.IsNullOrEmpty(origin.Revision) ? null : origin.Revision) == reference.Revision;
    private static AcquisitionCreationOrigin FromReference(WorkflowSourceRef reference) => new()
    {Kind = "module", Module = reference.Module, EntityKind = reference.EntityKind, EntityId = reference.EntityId, Revision = reference.Revision};
    private static AcquisitionCreationOrigin Sanitize(AcquisitionCreationOrigin origin) => origin with
    {Title = SafeTitle(origin.Title), Url = SafeUrl(origin.Url)};
    internal static string? SafeUrl(string? url) => url is {Length: > 0 and <= 8192} &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" && parsed.UserInfo.Length == 0 ? url : null;
    private static string? SafeTitle(string? title) => string.IsNullOrWhiteSpace(title) ? null : title.Length <= 512 ? title : title[..512];
    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) return null;
        try {return JsonSerializer.Deserialize<T>(json, Json);} catch (JsonException) {return null;}
    }
    private static T? ReadProperty<T>(string? json, string property) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(property, out var value)
                ? value.Deserialize<T>(Json) : null;
        }
        catch (JsonException) {return null;}
    }
}
