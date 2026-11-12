using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record WorkflowSourceRunsQuery
{
    public List<WorkflowSourceRef> Sources { get; init; } = [];
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 5;
    /// <summary>Omit known producer triggers when a source surface is showing only downstream work.</summary>
    public List<string>? ExcludedTriggerKinds { get; init; }
}

public sealed record WorkflowSourceRunView
{
    public required WorkflowRunViewModel Run { get; init; }
    public required string WorkflowName { get; init; }
    public bool WorkflowIsBuiltin { get; init; }
    public string Phase { get; set; } = "queued";
    public List<string> DownloadTaskIds { get; set; } = [];
    public int CompletedFileCount { get; set; }
    public bool Reused { get; set; }
    public string? DeliveredDirectory { get; set; }
    public long? DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public double? SpeedBytesPerSecond { get; set; }
    public List<WorkflowSourceRunLocation>? OutputLocations { get; set; } = [];
    public List<string>? ProblemCodes { get; set; } = [];
}

/// <summary>A recorded artifact location, or a planned directory with no committed artifact yet.</summary>
public sealed record WorkflowSourceRunLocation
{
    public required string Kind { get; init; }
    public string? Directory { get; init; }
    public string? ArtifactId { get; init; }
    public string? State { get; init; }
    public int? FileCount { get; init; }
    public long? TotalBytes { get; init; }
    public bool IsPlanned { get; init; }
}

public sealed record WorkflowSourceRunsView(WorkflowSourceRef Source, int RunCount, List<WorkflowSourceRunView> Runs)
{
    /// <summary>Counts across every matching run, independent of the requested page.</summary>
    public Dictionary<string, int>? StatusCounts { get; init; }
}

/// <summary>Feature-specific output projection; provenance and execution state stay in the shared ledger.</summary>
public interface IWorkflowSourceRunProjection
{
    void Enrich(WorkflowRunDbModel row, WorkflowSourceRunView view);
}

[ApiController]
[Route("~/workflow-sources/runs")]
public sealed class WorkflowSourcesController(BakabaseDbContext db, WorkflowSourceIndex<BakabaseDbContext> index,
    IEnumerable<IWorkflowSourceRunProjection> projections, BTaskManager tasks, DownloadCenterService downloads,
    IArtifactOperationStore? artifacts = null) : ControllerBase
{
    [HttpPost("query")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "QueryWorkflowSourceRuns")]
    public async Task<SingletonResponse<List<WorkflowSourceRunsView>>> Query([FromBody] WorkflowSourceRunsQuery input, CancellationToken ct)
    {
        if (input.Sources.Count > 100) throw new ArgumentException("Query at most 100 sources at a time.");
        var excludedTriggers = input.ExcludedTriggerKinds ?? [];
        if (excludedTriggers.Count > 32 || excludedTriggers.Any(kind => string.IsNullOrWhiteSpace(kind) ||
                kind.Length > 128 || kind.Any(char.IsControl)))
            throw new ArgumentException("Exclude at most 32 nonempty trigger kinds, each at most 128 characters.");
        excludedTriggers = excludedTriggers.Distinct(StringComparer.Ordinal).ToList();
        var sources = input.Sources.Distinct().ToList();
        if (sources.Count == 0) return new([]);
        _ = WorkflowRunSourceDbModel.From(new SourcePayload(sources));
        await index.EnsureIndexedAsync(ct);
        var modules = sources.Select(s => s.Module).Distinct().ToList();
        var kinds = sources.Select(s => s.EntityKind).Distinct().ToList();
        var ids = sources.Select(s => s.EntityId).Distinct().ToList();
        var links = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking()
            .Where(s => modules.Contains(s.Module) && kinds.Contains(s.EntityKind) && ids.Contains(s.EntityId)).ToListAsync(ct);
        var pageSize = Math.Clamp(input.PageSize, 1, 100);
        var skip = (int)Math.Min(int.MaxValue, ((long)Math.Max(1, input.PageIndex) - 1) * pageSize);
        var groups = sources.Select(source => new {Source = source, Ids = links.Where(s =>
                s.Module == source.Module && s.EntityKind == source.EntityKind && s.EntityId == source.EntityId &&
                (source.Revision == null || s.Revision == source.Revision) &&
                (source.ResultKey == null || s.ResultKey == source.ResultKey))
            .Select(s => s.WorkflowRunId).Distinct().OrderByDescending(id => id).ToList()}).ToList();
        var allRunIds = groups.SelectMany(g => g.Ids).Distinct().ToList();
        var eligibleRuns = db.WorkflowRuns.AsNoTracking().Where(r => allRunIds.Contains(r.Id));
        // The frozen trigger describes this execution even after its reusable template changes.
        // Unknown legacy definitions remain visible. Filter before counts and paging.
        var statusRows = await eligibleRuns.Select(r => new {r.Id, r.Status, r.WorkflowDefinitionId, r.PlanSnapshotJson}).ToListAsync(ct);
        var legacyDefinitionIds = statusRows.Where(r => r.PlanSnapshotJson == null).Select(r => r.WorkflowDefinitionId).Distinct().ToList();
        var legacyTriggers = excludedTriggers.Count == 0 ? new Dictionary<int, string>() : await db.WorkflowDefinitions
            .Where(d => legacyDefinitionIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.TriggerKind, ct);
        var statuses = statusRows.Where(r => excludedTriggers.Count == 0 || !excludedTriggers.Contains(
                (r.PlanSnapshotJson == null ? legacyTriggers.GetValueOrDefault(r.WorkflowDefinitionId) : SavedTrigger(r.PlanSnapshotJson)) ?? ""))
            .ToDictionary(r => r.Id, r => r.Status);
        var runIds = groups.SelectMany(g => g.Ids.Where(statuses.ContainsKey).Skip(skip).Take(pageSize)).Distinct().ToList();
        var rows = await db.WorkflowRuns.AsNoTracking().Include(r => r.NodeExecutions)
            .Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var definitionIds = rows.Values.Select(r => r.WorkflowDefinitionId).Distinct().ToList();
        var definitions = await db.WorkflowDefinitions.AsNoTracking().Where(d => definitionIds.Contains(d.Id))
            .Select(d => new {d.Id, d.Name, d.IsBuiltin}).ToDictionaryAsync(d => d.Id, ct);
        var views = rows.Values.ToDictionary(r => r.Id, row =>
        {
            var run = WorkflowRunViewModel.From(row.ToDomainModel());
            if (row.Status == WorkflowRunStatus.Success) run.Progress = 100;
            if (row.Status == WorkflowRunStatus.Running && tasks.GetTaskViewModel($"workflow.run.{row.Id}") is { } live)
            {
                run.Progress = live.Percentage;
                run.Process = live.Process;
                if (live.Data is WorkflowTaskProgress detail && detail.WorkflowRunId == row.Id)
                {run.ActivityKind = detail.ActivityKind; run.Stage = detail.Stage;}
            }
            var definition = definitions.GetValueOrDefault(row.WorkflowDefinitionId);
            var view = new WorkflowSourceRunView {Run = run, WorkflowName = run.PlanName ?? definition?.Name ?? "",
                WorkflowIsBuiltin = definition?.IsBuiltin == true,
                Phase = row.Status switch {WorkflowRunStatus.Pending => "queued", WorkflowRunStatus.Running => "running",
                    WorkflowRunStatus.Waiting => "waiting", WorkflowRunStatus.Success => "completed",
                    WorkflowRunStatus.Cancelled => "cancelled", _ => "failed"}};
            foreach (var projection in projections) projection.Enrich(row, view);
            WorkflowSourceRunOutputs.CaptureRecordedMetadata(view);
            WorkflowSourceRunProblems.Add(view, row.ErrorMessage);
            return view;
        });
        await WorkflowSourceRunOutputs.ResolveArtifactsAsync(views.Values, artifacts, ct);
        if (views.Values.Any(v => v.DownloadTaskIds.Count > 0))
        {
            var all = (await downloads.ListAsync(ct)).ToDictionary(t => t.Id);
            foreach (var view in views.Values)
                WorkflowSourceRunOutputs.EnrichDownloadTasks(view, all);
        }
        return new(groups.Select(g => new WorkflowSourceRunsView(g.Source, g.Ids.Count(statuses.ContainsKey),
            g.Ids.Where(statuses.ContainsKey).Skip(skip).Take(pageSize).Where(views.ContainsKey).Select(id => views[id]).ToList())
        {
            StatusCounts = g.Ids.Where(statuses.ContainsKey).GroupBy(id => statuses[id])
                .ToDictionary(group => group.Key.ToString().ToLowerInvariant(), group => group.Count())
        }).ToList());
    }

    private sealed record SourcePayload(IReadOnlyList<WorkflowSourceRef> SourceRefs) : IWorkflowSourcePayload;

    private static string? SavedTrigger(string snapshot)
    {
        try {return WorkflowRunPlans.Read(snapshot)?.TriggerKind;}
        catch (System.IO.InvalidDataException) {return null;}
    }
}
