using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public record PostParserWorkflowRunSearchInput
{
    [Range(1, int.MaxValue)] public int? TaskId { get; init; }
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

[ApiController]
[Route("~/post-parser/workflow-runs")]
public class PostParserWorkflowRunsController(BakabaseDbContext db) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SearchPostParserWorkflowRuns")]
    public async Task<SearchResponse<WorkflowRunViewModel>> Search(
        [FromQuery] PostParserWorkflowRunSearchInput input, CancellationToken ct)
    {
        // The trigger identifies the source across all preset versions and custom definitions.
        // The task's current run/definition would omit earlier revisions after a reparse or upgrade.
        var definitions = db.Set<WorkflowDefinitionDbModel>()
            .Where(d => d.TriggerKind == PostParserWorkflow.Trigger).Select(d => d.Id);
        var query = db.Set<WorkflowRunDbModel>().AsNoTracking()
            .Where(r => definitions.Contains(r.WorkflowDefinitionId));
        var pageIndex = Math.Max(1, input.PageIndex);
        var pageSize = Math.Clamp(input.PageSize, 1, 200);
        var skip = (int) Math.Min(int.MaxValue, ((long) pageIndex - 1) * pageSize);
        int total;

        if (input.TaskId is { } taskId)
        {
            // Task ids currently live in payload JSON, not an indexed column. Parse only this
            // source's ids/payloads, then paginate the matching ids before loading full run data.
            // A substring match confuses ids such as 1 and 10 and cannot handle JSON whitespace.
            var candidates = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Select(r => new {r.Id, r.PayloadJson}).ToListAsync(ct);
            var matchingIds = candidates.Where(r => ReadTaskId(r.PayloadJson) == taskId)
                .Select(r => r.Id).ToList();
            total = matchingIds.Count;
            var pageIds = matchingIds.Skip(skip).Take(pageSize).ToList();
            query = query.Where(r => pageIds.Contains(r.Id));
        }
        else
        {
            total = await query.CountAsync(ct);
            query = query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Skip(skip).Take(pageSize);
        }

        var rows = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .ToListAsync(ct);
        return new SearchResponse<WorkflowRunViewModel>(
            rows.Select(r => WorkflowRunViewModel.From(r.ToDomainModel())), total, pageIndex, pageSize);
    }

    private static int? ReadTaskId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            int? taskId = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("taskId", StringComparison.OrdinalIgnoreCase)) continue;
                if (taskId != null || property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt32(out var value) || value <= 0) return null;
                taskId = value;
            }
            return taskId;
        }
        catch (JsonException)
        {
            // An unreadable historical payload is still visible in source-wide history, but
            // cannot safely be attributed to an individual task.
            return null;
        }
    }
}
