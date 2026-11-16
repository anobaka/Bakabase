using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.ProcessingPlans;

public sealed class PostProcessingPlanSourceReader(BakabaseDbContext db) : IProcessingPlanSourceReader
{
    public bool Supports(WorkflowSourceRef source) => source.Module == "postParser" && source.EntityKind == "task";

    public async Task<ProcessingPlanSourceSnapshot> ReadAsync(WorkflowSourceRef source, CancellationToken ct)
    {
        if (!Supports(source) || !CanonicalInt(source.EntityId, out var taskId) ||
            !CanonicalInt(source.Revision, out var revision) || !CanonicalInt(source.ResultKey, out var index))
            throw new ArgumentException("PROCESSING_PLAN_SOURCE_INVALID");
        var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new ArgumentException("PROCESSING_PLAN_SOURCE_UNAVAILABLE");
        if (task.Revision != revision) throw new InvalidOperationException("PROCESSING_PLAN_SOURCE_CHANGED");
        await EnsureStableAsync(db, task, ct);
        return Snapshot(task, index);
    }

    internal static async Task EnsureStableAsync(BakabaseDbContext db, PostParserTaskDbModel task, CancellationToken ct)
    {
        if (!PostParserSavedResults.HasCompletedDownloadResult(task) || task.Error != null &&
            task.Error != PostParserSavedResults.MissingWorkflowRunError)
            throw new ArgumentException("PROCESSING_PLAN_SOURCE_UNAVAILABLE");
        if (task.WorkflowRunId != null && await db.WorkflowRuns.AsNoTracking().AnyAsync(r => r.Id == task.WorkflowRunId &&
            (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running), ct))
            throw new InvalidOperationException("PROCESSING_PLAN_SOURCE_BUSY");
    }

    internal static ProcessingPlanSourceSnapshot Snapshot(PostParserTaskDbModel task, int index) => Snapshots(task, [index]).Single();

    internal static IReadOnlyList<ProcessingPlanSourceSnapshot> Snapshots(PostParserTaskDbModel task, IReadOnlyList<int>? indices = null)
    {
        var resources = PostParserSavedResults.ReadDownloadInfo(task).Resources;
        indices ??= Enumerable.Range(0, resources.Count).ToArray();
        if (indices.Any(index => index < 0 || index >= resources.Count)) throw new ArgumentException("PROCESSING_PLAN_SOURCE_INVALID");
        var content = string.IsNullOrWhiteSpace(task.ContentSnapshotJson) ? null :
            JsonSerializer.Deserialize<PostContent>(task.ContentSnapshotJson, WorkflowJson.Options);
        var html = content == null ? new List<string>() : new[] {content.MainHtml}.Concat(content.Comments.Count > 0
            ? content.Comments.Select(c => c.Html) : content.CommentHtmlList).ToList();
        // Plain pasted content is itself a saved source; generated evidence fields are never a fallback.
        if (html.Count == 0 && !string.IsNullOrWhiteSpace(task.Text)) html.Add(task.Text);
        var texts = (content?.Scope == "pastedText" || content == null
                ? html : html.SelectMany(VisibleText)).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        return indices.Select(index =>
        {
        var resource = resources[index];
        var source = new WorkflowSourceRef("postParser", "task", task.Id.ToString(CultureInfo.InvariantCulture),
            task.Revision.ToString(CultureInfo.InvariantCulture), index.ToString(CultureInfo.InvariantCulture));
        // Full saved resource binds URL, code, archive password and original instructions, including multiple
        // resources in one post. These values are hashed by the store; they are not copied to the audit row.
        var fingerprint = JsonSerializer.Serialize(new {resource, task.ContentSnapshotJson, task.Text}, WorkflowJson.Options);
        return new ProcessingPlanSourceSnapshot(source, resource.Extraction, texts, fingerprint, resource.Link);
        }).ToList();
    }

    private static IEnumerable<string> VisibleText(string html)
    {
        yield return ProcessingPlanSourceText.Visible(html);
    }
    private static bool CanonicalInt(string? value, out int number) => int.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out number) && number >= 0 && number.ToString(CultureInfo.InvariantCulture) == value;
}
