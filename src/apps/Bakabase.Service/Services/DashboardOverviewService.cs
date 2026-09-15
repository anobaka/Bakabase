using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Models.View;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Services;

/// <summary>
/// SQL aggregates and bounded projections only. The homepage must not hydrate the resource library,
/// evaluate collection rules, validate workflows, inspect files, or deserialize execution payloads.
/// </summary>
public class DashboardOverviewService(BakabaseDbContext db, TimeProvider? timeProvider = null)
{
    public async Task<DashboardOverviewViewModel> GetAsync(CancellationToken ct = default)
    {
        // Stored resource/run timestamps use the server's local clock.
        var now = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime;
        var thisMonday = now.Date.AddDays(-((7 + (int) now.DayOfWeek - (int) DayOfWeek.Monday) % 7));
        var recentFrom = now.AddDays(-7);

        var resourceCounts = await db.ResourcesV2.AsNoTracking().GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Local = group.Count(resource => resource.Path != null && resource.Path != ""),
                ThisWeek = group.Count(resource => resource.CreateDt >= thisMonday && resource.CreateDt <= now)
            }).SingleOrDefaultAsync(ct);
        var collectionCount = await db.Collections.CountAsync(ct);
        var mediaLibraryCount = await db.MediaLibrariesV2.CountAsync(ct);

        var mediaLibraries = await db.MediaLibrariesV2.AsNoTracking()
            .Select(library => new DashboardMediaLibraryViewModel
            {
                Id = library.Id,
                Name = library.Name,
                ResourceCount = db.MediaLibraryResourceMappings
                    .Where(mapping => mapping.MediaLibraryId == library.Id)
                    .Join(db.ResourcesV2, mapping => mapping.ResourceId, resource => resource.Id,
                        (mapping, resource) => resource.Id)
                    .Distinct().Count()
            })
            .OrderByDescending(library => library.ResourceCount).ThenBy(library => library.Id)
            .Take(6).ToListAsync(ct);

        var workflowCounts = await db.WorkflowRuns.AsNoTracking().GroupBy(_ => 1)
            .Select(group => new
            {
                Running = group.Count(run => run.Status == WorkflowRunStatus.Pending || run.Status == WorkflowRunStatus.Running),
                Waiting = group.Count(run => run.Status == WorkflowRunStatus.Waiting),
                FailedRecently = group.Count(run =>
                    (run.Status == WorkflowRunStatus.Failed || run.Status == WorkflowRunStatus.Interrupted) &&
                    (run.CompletedAt ?? run.StartedAt) >= recentFrom && (run.CompletedAt ?? run.StartedAt) <= now)
            }).SingleOrDefaultAsync(ct);

        return new DashboardOverviewViewModel
        {
            TotalResourceCount = resourceCounts?.Total ?? 0,
            LocalResourceCount = resourceCounts?.Local ?? 0,
            PendingResourceCount = (resourceCounts?.Total ?? 0) - (resourceCounts?.Local ?? 0),
            CollectionCount = collectionCount,
            MediaLibraryCount = mediaLibraryCount,
            ThisWeekAddedCount = resourceCounts?.ThisWeek ?? 0,
            MediaLibraries = mediaLibraries,
            Workflows = new DashboardWorkflowsViewModel
            {
                RunningCount = workflowCounts?.Running ?? 0,
                WaitingCount = workflowCounts?.Waiting ?? 0,
                FailedRecentlyCount = workflowCounts?.FailedRecently ?? 0
            }
        };
    }
}
