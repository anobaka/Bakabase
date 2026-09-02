using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow;

/// <summary>
/// The clock half of E6: a minutely sweep over enabled definitions whose trigger implements
/// <see cref="IWorkflowScheduledTrigger"/>. Due = the last completed run is older than the
/// definition's own interval (a definition that has never run is due immediately). A due
/// definition starts through the exact manual-run path, so scheduled runs behave and validate
/// identically to a user's click; one already waiting or executing is never double-queued.
/// </summary>
public class WorkflowSchedulerTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(serviceProvider, localizer)
{
    public override string Id => "WorkflowScheduler";

    public override bool IsEnabled() => true;

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
        var triggers = scope.ServiceProvider.GetRequiredService<IWorkflowTriggerRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WorkflowSchedulerTask>>();

        var defs = await definitions.SearchAsync(new WorkflowDefinitionSearchInputModel {EnabledOnly = true});
        var now = DateTime.Now;

        foreach (var def in defs)
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            if (!triggers.TryGet(def.TriggerKind, out var trigger) ||
                trigger is not IWorkflowScheduledTrigger scheduled)
            {
                continue;
            }

            var interval = scheduled.GetInterval(def.TriggerFilterJson);
            if (interval is null) continue;

            if (def.LastRunAt is { } last && now < last + interval.Value) continue;

            // A run still waiting or executing means the previous firing hasn't finished —
            // stacking another behind it would burn a slot to produce the same plan.
            var hasActive = await db.Set<WorkflowRunDbModel>().AnyAsync(r =>
                    r.WorkflowDefinitionId == def.Id &&
                    (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running),
                args.CancellationToken);
            if (hasActive) continue;

            try
            {
                await definitions.RunManuallyAsync(def.Id, null, args.CancellationToken);
            }
            catch (Exception ex)
            {
                // A stale config (deleted root…) must not kill the sweep for every other
                // definition; the reason lands on the definition where the UI already shows it.
                logger.LogWarning(ex, "Scheduled start of workflow #{DefId} failed", def.Id);
                await db.Set<WorkflowDefinitionDbModel>()
                    .Where(d => d.Id == def.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastError, _ => ex.Message),
                        args.CancellationToken);
            }
        }
    }
}
