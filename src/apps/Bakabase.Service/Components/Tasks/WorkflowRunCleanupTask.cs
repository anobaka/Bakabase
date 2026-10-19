using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Trims <c>WorkflowRun</c> history: keeps the most recent <see cref="RetentionPerDefinition"/>
/// unreferenced terminal rows per workflow definition, preserving active and source-owned runs. Runs every 6 hours so retention stays
/// loose (the cap isn't enforced in real time — bursty workflows can briefly exceed it).
///
/// The TaskManager runs only one instance at a time via the default <c>ConflictKeys=[Id]</c>.
/// </summary>
public class WorkflowRunCleanupTask : AbstractPredefinedBTaskBuilder
{
    private const int RetentionPerDefinition = 50;

    public WorkflowRunCleanupTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "WorkflowRunCleanup";
    public override bool IsEnabled() => true;
    public override TimeSpan? GetInterval() => TimeSpan.FromHours(6);

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var downloads = scope.ServiceProvider.GetService<IBtDownloadManager>();
        var httpDownloads = scope.ServiceProvider.GetService<IHttpDownloadManager>();
        IEnumerable<int> linkedBtRuns = downloads == null ? [] : (await downloads.ListAsync(args.CancellationToken))
            .Where(j => j.WorkflowRunId != null).Select(j => j.WorkflowRunId!.Value);
        IEnumerable<int> linkedHttpRuns = httpDownloads == null ? [] : (await httpDownloads.ListAsync(args.CancellationToken))
            .Where(j => j.WorkflowRunId != null).Select(j => j.WorkflowRunId!.Value);
        // Capability creation and registration are separate writes. Protect the short gap before
        // the canonical binding is saved as well as registered tasks during the deletion query.
        var linkedTransferRuns = linkedBtRuns.Concat(linkedHttpRuns).Distinct().ToArray();

        // Select concrete IDs, including a deterministic ID tie-breaker for runs created together.
        // Active and source-linked runs do not consume the unreferenced history retention budget.
        var definitionIds = await db.Set<WorkflowRunDbModel>()
            .Select(r => r.WorkflowDefinitionId)
            .Distinct()
            .ToListAsync(args.CancellationToken);

        var totalDeleted = 0;
        for (var i = 0; i < definitionIds.Count; i++)
        {
            await args.YieldAsync();
            var defId = definitionIds[i];

            var removable = await GetPrunableRunIdsAsync(db, defId, linkedTransferRuns, RetentionPerDefinition, args.CancellationToken);
            if (removable.Count == 0) continue;
            // Recheck ownership and status when deleting: a retry may have started since selection.
            var deleted = Eligible(db, linkedTransferRuns).Where(r => removable.Contains(r.Id))
                .ExecuteDeleteAsync(args.CancellationToken);
            totalDeleted += await deleted;

            await args.UpdateTask(t =>
            {
                t.Percentage = (i + 1) * 100 / Math.Max(1, definitionIds.Count);
                t.SetProcess(BTaskText.Localize(Localizer, "BTask_Process_WorkflowCleanup", i + 1, definitionIds.Count, totalDeleted));
            });
        }
    }

    internal static Task<List<int>> GetPrunableRunIdsAsync(BakabaseDbContext db, int definitionId,
        IReadOnlyCollection<int> linkedTransferRuns, int retention, CancellationToken ct = default) =>
        Eligible(db, linkedTransferRuns).Where(r => r.WorkflowDefinitionId == definitionId)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Skip(retention)
            .Select(r => r.Id).ToListAsync(ct);

    private static IQueryable<WorkflowRunDbModel> Eligible(BakabaseDbContext db, IReadOnlyCollection<int> linkedTransferRuns) =>
        db.Set<WorkflowRunDbModel>().Where(r =>
            (r.Status == WorkflowRunStatus.Success || r.Status == WorkflowRunStatus.Failed ||
             r.Status == WorkflowRunStatus.Cancelled || r.Status == WorkflowRunStatus.Interrupted) &&
            !db.DownloadResultProcessing.Any(p => p.WorkflowRunId == r.Id) &&
            !db.DownloadResultOwners.Any(o => o.WorkflowRunId == r.Id) && !linkedTransferRuns.Contains(r.Id) &&
            !db.ManagedDownloadTasks.Any(t => t.WorkflowRunId == r.Id));
}
