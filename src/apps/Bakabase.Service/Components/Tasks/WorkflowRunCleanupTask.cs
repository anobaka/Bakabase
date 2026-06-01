using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Trims <c>WorkflowRun</c> history: keeps the most recent <see cref="RetentionPerDefinition"/>
/// rows per workflow definition, deletes the rest. Runs every 6 hours so retention stays
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

        // For each definition, find the StartedAt cutoff of the (Retention+1)-th most recent
        // run; everything older than that goes. SQLite handles the N-th-row trick with
        // OFFSET inside a subquery, but EF Core's translator does it as a follow-up scan,
        // which is fine for our scale (< thousands of rows).
        var definitionIds = await db.Set<WorkflowRunDbModel>()
            .Select(r => r.WorkflowDefinitionId)
            .Distinct()
            .ToListAsync(args.CancellationToken);

        var totalDeleted = 0;
        for (var i = 0; i < definitionIds.Count; i++)
        {
            await args.YieldAsync();
            var defId = definitionIds[i];

            var cutoff = await db.Set<WorkflowRunDbModel>()
                .Where(r => r.WorkflowDefinitionId == defId)
                .OrderByDescending(r => r.StartedAt)
                .ThenByDescending(r => r.Id)
                .Skip(RetentionPerDefinition)
                .Select(r => (DateTime?)r.StartedAt)
                .FirstOrDefaultAsync(args.CancellationToken);

            if (cutoff is null) continue;  // already at or below retention

            var deleted = await db.Set<WorkflowRunDbModel>()
                .Where(r => r.WorkflowDefinitionId == defId && r.StartedAt <= cutoff.Value)
                .ExecuteDeleteAsync(args.CancellationToken);
            totalDeleted += deleted;

            await args.UpdateTask(t =>
            {
                t.Percentage = (i + 1) * 100 / Math.Max(1, definitionIds.Count);
                t.Process = $"{i + 1}/{definitionIds.Count} · deleted {totalDeleted}";
            });
        }
    }
}
