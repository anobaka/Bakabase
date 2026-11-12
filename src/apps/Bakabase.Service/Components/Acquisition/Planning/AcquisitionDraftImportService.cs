using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Explicit preservation/import commands. Browsing task lists never invokes this service.</summary>
public sealed class AcquisitionDraftImportService(BakabaseDbContext db, AcquisitionPlanService plans,
    IResourceService resources, IReservedPropertyValueService names, IResourceSourceLinkService sourceLinks)
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;

    public async Task<AcquisitionDraftImportResult> ImportLegacyAsync(AcquisitionLegacyImportInput input, CancellationToken ct)
    {
        if (input.ResourceIds == null || input.ResourceIds.Count is 0 or > 1000 || input.ResourceIds.Any(id => id <= 0))
            throw new ArgumentException("Choose between one and 1000 existing overview entries.");
        var ids = input.ResourceIds.Distinct().ToArray();
        await ImportGate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var knownResources = (await resources.GetAll(r => ids.Contains(r.Id))).Select(r => r.Id).ToHashSet();
            var titles = (await names.GetAll(v => ids.Contains(v.ResourceId))).Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .GroupBy(v => v.ResourceId).ToDictionary(g => g.Key, g => g.First().Name!);
            var leads = await db.Set<AcquisitionLeadDbModel>().AsNoTracking().Where(l => Enumerable.Contains(ids, l.ResourceId)).ToListAsync(ct);
            var identities = await sourceLinks.GetByResourceIdsGrouped(ids);
            var existing = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                .Where(t => t.BuilderStateJson != null || (t.ResourceId != null && Enumerable.Contains(ids, t.ResourceId.Value)))
                .OrderByDescending(t => t.Id).ToListAsync(ct);
            var taskIds = new List<int>(); var problems = new List<string>(); var created = 0; var matched = 0;
            foreach (var id in ids)
            {
                var previous = existing.FirstOrDefault(t => t.ResourceId == id ||
                    AcquisitionTaskExtensions.ReadOriginResourceId(t.BuilderStateJson) == id);
                if (previous != null) {taskIds.Add(previous.Id); matched++; continue;}
                if (!knownResources.Contains(id)) {problems.Add($"Resource #{id} no longer exists."); continue;}
                var snapshots = leads.Where(l => l.ResourceId == id).Select(l => new AcquisitionLegacyLeadSnapshot(
                    "lead-" + l.Id, l.Kind, l.Value, l.IsResolved, l.Note, l.AccessCode, l.Password,
                    l.ExtractionPlanJson, l.SourceReference, ReadRefs(l.SourceRefsJson), l.SourceRefsJson)).ToList();
                snapshots.AddRange((identities.GetValueOrDefault(id) ?? []).Where(l => l.Source.IsPlatformHolding())
                    .Select(l => new AcquisitionLegacyLeadSnapshot("holding-" + l.Id, AcquisitionLeadKind.PlatformHolding,
                        l.Source + ":" + l.SourceKey, false)));
                var refs = snapshots.SelectMany(l => l.SourceRefs ?? []).Append(new WorkflowSourceRef("resource", "resource", id.ToString(CultureInfo.InvariantCulture)))
                    .Distinct().ToList();
                var sources = snapshots.Select(ToDraftSource).Where(s => s != null).Cast<AcquisitionDraftSource>().Take(100).ToList();
                var originalTitle = titles.GetValueOrDefault(id) ?? $"#{id}";
                var displayTitle = originalTitle.Trim();
                if (displayTitle.Length > 512) displayTitle = displayTitle[..512];
                var row = await plans.CreateDraftRowAsync(new(displayTitle, sources),
                    id, snapshots, refs, "legacy-resource:" + id, ct, legacyTitle: originalTitle);
                existing.Add(row); taskIds.Add(row.Id); created++;
            }
            await transaction.CommitAsync(ct);
            return new(created, matched, taskIds.Distinct().ToList(), problems);
        }
        finally {ImportGate.Release();}
    }

    public async Task<AcquisitionDraftImportResult> ImportListAsync(AcquisitionDraftListInput input, CancellationToken ct)
    {
        if (input.Rows == null || input.Rows.Count is 0 or > 1000 || input.Rows.Any(r => r == null))
            throw new ArgumentException("Supply between one and 1000 confirmed list rows.");
        await ImportGate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var prior = await db.Set<AcquisitionTaskDbModel>().AsNoTracking().Where(t => t.BuilderStateJson != null).ToListAsync(ct);
            var imported = prior.Select(t => new {t.Id, State = ReadState(t.BuilderStateJson)})
                .Where(t => t.State.ImportKey != null).GroupBy(t => t.State.ImportKey!)
                .ToDictionary(g => g.Key, g => g.First().Id);
            var taskIds = new List<int>(); var problems = new List<string>(); var created = 0; var matched = 0;
            foreach (var row in input.Rows)
            {
                var title = row.Title?.Trim(); var url = row.Url?.Trim();
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(url)) continue;
                if (title?.Length > 512 || url?.Length > 2048 || row.Password?.Length > 2048 ||
                    (!string.IsNullOrEmpty(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        uri.Scheme is not ("http" or "https" or "magnet") || !string.IsNullOrEmpty(uri.UserInfo))))
                {problems.Add($"Line {row.LineNumber}: invalid title or source URL."); continue;}
                var key = "list:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new {title, url, row.Password}, Json))));
                if (imported.TryGetValue(key, out var existingId)) {taskIds.Add(existingId); matched++; continue;}
                var source = string.IsNullOrEmpty(url) ? new List<AcquisitionDraftSource>() :
                    [new AcquisitionDraftSource {Id = "source-1", Url = url, ArchivePassword = row.Password}];
                var saved = await plans.CreateDraftRowAsync(new(title, source), null, [], [], key, ct, importedListRow: row);
                imported.Add(key, saved.Id); taskIds.Add(saved.Id); created++;
            }
            await transaction.CommitAsync(ct);
            return new(created, matched, taskIds.Distinct().ToList(), problems);
        }
        finally {ImportGate.Release();}
    }

    private static AcquisitionDraftSource? ToDraftSource(AcquisitionLegacyLeadSnapshot lead)
    {
        if (lead.Value.Length > 2048 || lead.AccessCode?.Length > 512 || lead.Password?.Length > 2048 ||
            !Uri.TryCreate(lead.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "magnet") ||
            !string.IsNullOrEmpty(uri.UserInfo)) return null;
        FileProcessingPlan? processing = null;
        if (lead.IsResolved && lead.ExtractionPlanJson != null)
        {
            try
            {
                processing = JsonSerializer.Deserialize<FileProcessingPlan>(lead.ExtractionPlanJson, Json);
                if (processing != null) FileProcessingPlanValidator.Validate(processing);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) {processing = null;}
        }
        return new() {Id = lead.Id, Url = lead.Value, AccessCode = lead.AccessCode, ArchivePassword = lead.Password,
            Kind = lead.Kind switch {AcquisitionLeadKind.DirectUrl when CloudSourceClassifier.Classify(lead.Value) is "http" or "unknown" => "http",
                AcquisitionLeadKind.Torrent => "torrent", _ => "auto"},
            Processing = processing};
    }
    private static IReadOnlyList<WorkflowSourceRef> ReadRefs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try {return JsonSerializer.Deserialize<List<WorkflowSourceRef>>(json, Json) ?? [];}
        catch (JsonException) {return [];}
    }
    private static AcquisitionPlanBuilderState ReadState(string? json)
    {
        try {return json == null ? new() : JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(json, Json) ?? new();}
        catch (JsonException) {return new();}
    }
}
