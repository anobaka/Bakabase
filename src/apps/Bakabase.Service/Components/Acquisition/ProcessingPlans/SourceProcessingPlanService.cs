using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.ProcessingPlans;

/// <summary>Append-only, source-bound instructions. Neither producer results nor existing execution plans are rewritten.</summary>
public sealed class SourceProcessingPlanService(BakabaseDbContext db, IEnumerable<IProcessingPlanSourceReader> readers)
{
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;
    private DbSet<ProcessingPlanRevisionDbModel> Revisions => db.Set<ProcessingPlanRevisionDbModel>();

    public async Task<SourceProcessingPlanView> GetAsync(WorkflowSourceRef source, CancellationToken ct)
    {
        var key = SourceKey(source);
        try
        {
            var snapshot = await ReadSourceAsync(source, ct);
            return (await ResolveAsync([snapshot], ct, includeHistory: true))[0];
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException && error.Message is
                   "PROCESSING_PLAN_SOURCE_CHANGED" or "PROCESSING_PLAN_SOURCE_UNAVAILABLE" or "PROCESSING_PLAN_SOURCE_BUSY")
        {
            // Re-parsing/deletion makes the overlay unusable, not its audit history invisible.
            var rows = await Revisions.AsNoTracking().Where(r => r.SourceKey == key).OrderByDescending(r => r.Revision).ToListAsync(ct);
            if (rows.Count == 0) throw;
            var history = rows.Select(RevisionView).ToList();
            return new(source, "", history[0].Revision, history[0].OriginalPlan, null, 0, history, true, false, error.Message);
        }
    }

    // A producer that already holds its saved snapshot can resolve all resources in one query.
    public async Task<IReadOnlyList<SourceProcessingPlanView>> ResolveAsync(
        IReadOnlyList<ProcessingPlanSourceSnapshot> snapshots, CancellationToken ct, bool includeHistory = false)
    {
        if (snapshots.Count > 500) throw new ArgumentException("PROCESSING_PLAN_SOURCE_INVALID");
        var keys = snapshots.Select(s => SourceKey(s.Source)).Distinct().ToArray();
        var rows = await Revisions.AsNoTracking().Where(r => keys.Contains(r.SourceKey) &&
            (includeHistory || !Revisions.Any(newer => newer.SourceKey == r.SourceKey && newer.Revision > r.Revision)))
            .OrderByDescending(r => r.Revision).ToListAsync(ct);
        return snapshots.Select(s => View(s, rows.Where(r => r.SourceKey == SourceKey(s.Source)).ToList())).ToList();
    }

    public async Task<SourceProcessingPlanView> ReviseAsync(ProcessingPlanRevisionInput input, CancellationToken ct)
    {
        if (input == null || input.Plan == null || input.ExpectedRevision < 0 ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 ||
            input.Evidence == null || input.Evidence.Count is < 1 or > 32 ||
            JsonSerializer.Serialize(input.Plan, Json).Length > 131072)
            throw new ArgumentException("PROCESSING_PLAN_REVISION_INVALID");
        FileProcessingPlanValidator.Validate(input.Plan);
        // The original source read and append share one database snapshot; a concurrent producer write
        // cannot be silently accepted between verification and commit.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await ReadSourceAsync(input.Source, ct);
        var hash = SourceHash(snapshot);
        if (hash != input.ExpectedSourceHash) throw new InvalidOperationException("PROCESSING_PLAN_SOURCE_CHANGED");
        ValidateEvidence(input, snapshot);
        var key = SourceKey(input.Source);
        // SERIALIZABLE + unique (source, revision) prevents a second writer from overwriting a reviewed head.
        var rows = await Revisions.AsNoTracking().Where(r => r.SourceKey == key)
            .OrderByDescending(r => r.Revision).ToListAsync(ct);
        if ((rows.FirstOrDefault()?.Revision ?? 0) != input.ExpectedRevision)
            throw new InvalidOperationException("PROCESSING_PLAN_VERSION_CONFLICT");
        if (rows.Count >= 100) throw new InvalidOperationException("PROCESSING_PLAN_REVISION_LIMIT");
        var row = new ProcessingPlanRevisionDbModel
        {
            Id = Guid.NewGuid().ToString("N"), SourceKey = key, SourceJson = JsonSerializer.Serialize(input.Source, Json),
            Revision = input.ExpectedRevision + 1, SourceHash = hash,
            OriginalPlanJson = snapshot.OriginalPlan == null ? null : JsonSerializer.Serialize(snapshot.OriginalPlan, Json),
            PlanJson = JsonSerializer.Serialize(input.Plan, Json), EvidenceJson = JsonSerializer.Serialize(input.Evidence, Json),
            Reason = input.Reason.Trim(), CreatedAt = DateTime.UtcNow
        };
        Revisions.Add(row);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            throw new InvalidOperationException("PROCESSING_PLAN_VERSION_CONFLICT");
        }
        rows.Insert(0, row);
        return View(snapshot, rows);
    }

    public async Task<ProcessingPlanRevisionView> RequireCurrentRevisionAsync(WorkflowSourceRef source,
        string revisionId, CancellationToken ct)
    {
        var current = await GetAsync(source, ct);
        var revision = current.History.FirstOrDefault();
        if (revision == null || revision.Id != revisionId || revision.SourceHash != current.SourceHash)
            throw new InvalidOperationException("PROCESSING_PLAN_SOURCE_CHANGED");
        return revision;
    }

    public async Task<ProcessingPlanSourceSnapshot> ReadSourceAsync(WorkflowSourceRef source, CancellationToken ct)
    {
        _ = SourceKey(source);
        var reader = readers.SingleOrDefault(r => r.Supports(source))
            ?? throw new ArgumentException("PROCESSING_PLAN_SOURCE_UNAVAILABLE");
        return await reader.ReadAsync(source, ct);
    }

    public static string SourceKey(WorkflowSourceRef source)
    {
        if (source == null || new[] {source.Module, source.EntityKind, source.EntityId, source.Revision, source.ResultKey}
                .Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 512))
            throw new ArgumentException("PROCESSING_PLAN_SOURCE_INVALID");
        return Hash(JsonSerializer.Serialize(source, Json));
    }

    public static string SourceHash(ProcessingPlanSourceSnapshot snapshot) => Hash(JsonSerializer.Serialize(new
    {snapshot.Source, snapshot.OriginalPlan, snapshot.EvidenceTexts, snapshot.SourceFingerprint}, Json));

    private static SourceProcessingPlanView View(ProcessingPlanSourceSnapshot snapshot, List<ProcessingPlanRevisionDbModel> rows)
    {
        var hash = SourceHash(snapshot);
        var history = rows.Select(RevisionView).ToList();
        var effective = history.FirstOrDefault() is { } latest && latest.SourceHash == hash ? latest : null;
        return new(snapshot.Source, hash, history.FirstOrDefault()?.Revision ?? 0, snapshot.OriginalPlan,
            effective?.Plan ?? snapshot.OriginalPlan, effective?.Revision ?? 0, history,
            history.Any(r => r.SourceHash != hash));
    }

    private static ProcessingPlanRevisionView RevisionView(ProcessingPlanRevisionDbModel r) => new(r.Id, r.Revision, r.SourceHash,
        r.OriginalPlanJson == null ? null : JsonSerializer.Deserialize<FileProcessingPlan>(r.OriginalPlanJson, Json),
        JsonSerializer.Deserialize<FileProcessingPlan>(r.PlanJson, Json)!,
        JsonSerializer.Deserialize<List<ProcessingPlanEvidence>>(r.EvidenceJson, Json)!, r.Reason,
        new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)));

    private static void ValidateEvidence(ProcessingPlanRevisionInput input, ProcessingPlanSourceSnapshot snapshot)
    {
        var texts = snapshot.EvidenceTexts.Select(NormalizeText).ToArray();
        foreach (var evidence in input.Evidence)
        {
            if (evidence == null || string.IsNullOrWhiteSpace(evidence.Quote) || evidence.Quote.Length > 1000 ||
                (evidence.StepId != null && !input.Plan.Steps.Any(s => s.Id == evidence.StepId)) ||
                !texts.Any(t => t.Contains(NormalizeText(evidence.Quote), StringComparison.Ordinal)))
                throw new ArgumentException("PROCESSING_PLAN_EVIDENCE_MISSING");
        }
        if (input.Plan.Steps.Any(s => !input.Evidence.Any(e => e.StepId == s.Id)))
            throw new ArgumentException("PROCESSING_PLAN_EVIDENCE_MISSING");
    }
    private static string NormalizeText(string value) => Regex.Replace(value, @"\s+", " ").Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
