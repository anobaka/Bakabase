using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Workflow.Services;

public sealed class WorkflowSourceIndexCursor
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public int LastRunId { get; set; }
}

/// <summary>Indexes typed historical payloads at runtime; schema migrations never rewrite user data.</summary>
public sealed class WorkflowSourceIndex<TDbContext>(TDbContext db, IWorkflowTriggerRegistry triggers,
    WorkflowSourceIndexCursor cursor) where TDbContext : DbContext
{
    public async Task EnsureIndexedAsync(CancellationToken ct)
    {
        await cursor.Gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var rows = await db.Set<WorkflowRunDbModel>().Where(r => r.Id > cursor.LastRunId)
                    .OrderBy(r => r.Id).Take(250).Include(r => r.Sources).ToListAsync(ct);
                if (rows.Count == 0) return;
                var definitionIds = rows.Select(r => r.WorkflowDefinitionId).Distinct().ToList();
                var definitions = await db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
                    .Where(d => definitionIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.TriggerKind, ct);
                foreach (var row in rows.Where(r => r.Sources.Count == 0 && r.PayloadJson != null))
                {
                    try
                    {
                        var kind = WorkflowRunPlans.Read(row.PlanSnapshotJson)?.TriggerKind ?? definitions.GetValueOrDefault(row.WorkflowDefinitionId);
                        if (kind == null || !triggers.TryGet(kind, out var trigger) ||
                            !typeof(IWorkflowSourcePayload).IsAssignableFrom(trigger.PayloadType)) continue;
                        using var json = JsonDocument.Parse(row.PayloadJson!);
                        if (json.RootElement.ValueKind != JsonValueKind.Object ||
                            json.RootElement.EnumerateObject().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) continue;
                        var payload = JsonSerializer.Deserialize(row.PayloadJson!, trigger.PayloadType, WorkflowJson.Options);
                        if (payload != null) row.Sources = WorkflowRunSourceDbModel.From(payload);
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidDataException)
                    {
                        // Unattributable historical data remains in the ordinary workflow history.
                    }
                }
                await db.SaveChangesAsync(ct);
                cursor.LastRunId = rows[^1].Id;
            }
        }
        finally {cursor.Gate.Release();}
    }
}
