using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

/// <summary>No execution here: the outbox and the producer's Success transition commit together.</summary>
public sealed class PostAcquisitionCompletionObserver(BakabaseDbContext db,
    IBOptions<ThirdPartyOptions> options, AcquisitionOutputDirectoryResolver directories) : IWorkflowRunCompletionObserver
{
    public async Task StageAsync(WorkflowRunDbModel run, object payload, CancellationToken ct)
    {
        if (options.Value.PostParserAcquisition?.Enabled != true || run.Status != WorkflowRunStatus.Success ||
            run.FailedItemCount != 0 || run.OutputCount != 1 || payload is not PostParserInput {TaskId: { } id} input)
            return;
        var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
        if (task == null || task.Revision != input.Revision || task.WorkflowRunId != run.Id ||
            task.ParsingState != "complete" || !PostParserSavedResults.HasCompletedDownloadResult(task)) return;
        var sourceId = id.ToString(CultureInfo.InvariantCulture);
        var revision = input.Revision.ToString(CultureInfo.InvariantCulture);
        var key = "postParser:task:" + sourceId + ":" + revision;
        if (await db.AcquisitionHandoffs.AnyAsync(h => h.SourceModule == "postParser" && h.SourceKind == "task" &&
                h.SourceId == sourceId && h.SourceRevision == revision, ct)) return;
        var directory = options.Value.PostParserAcquisition.OutputDirectory;
        db.AcquisitionHandoffs.Add(new AcquisitionHandoffDbModel
        {
            Id = key, SourceModule = "postParser", SourceKind = "task", SourceId = sourceId,
            SourceRevision = revision, ProducerRunId = run.Id,
            OutputDirectory = directories.ResolveNew(string.IsNullOrWhiteSpace(directory) ? null : directory),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
    }
}
