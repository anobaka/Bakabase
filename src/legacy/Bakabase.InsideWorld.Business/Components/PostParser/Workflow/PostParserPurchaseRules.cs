using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public static class PostParserPurchaseRules
{
    public static bool SupportsSource(string? sourceHint, IEnumerable<ISharedContentPurchaser> purchasers) =>
        !string.IsNullOrWhiteSpace(sourceHint) && purchasers.Any(p =>
            string.Equals(p.Source.ToString(), sourceHint, StringComparison.OrdinalIgnoreCase));

    /// <summary>Use this run's frozen identity, never today's editable workflow template.</summary>
    public static string? CurrentActivityKind(WorkflowRunDbModel run)
    {
        if (run.CurrentStepIndex is not { } index || index < 0) return null;
        if (run.PlanSnapshotJson != null)
        {
            try
            {
                var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
                return plan != null && index < plan.Nodes.Count ? plan.Nodes[index].Kind : null;
            }
            catch (InvalidDataException) { return null; }
        }
        return run.NodeExecutions.Where(n => n.StepIndex == index && !string.IsNullOrWhiteSpace(n.Kind))
            .OrderByDescending(n => n.Id).ThenByDescending(n => n.Attempt).FirstOrDefault()?.Kind ??
            WorkflowExtensions.ParseStepStats(run.StepStatsJson).LastOrDefault(s => s.StepIndex == index)?.Kind;
    }
}
