using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only a current completed acquisition may authorize its earlier execution owners.
/// Other callers retain the generic single-run policy; unrelated tasks never share authorization.</summary>
public sealed class AcquisitionArtifactCleanupOwnerScope(BakabaseDbContext db) : IArtifactCleanupOwnerScopeProvider
{
    public async Task<IReadOnlyList<int>?> ExpandAsync(ArtifactRecord publication, IReadOnlyList<int> directRunIds, CancellationToken ct)
    {
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.DeletedAt == null &&
            t.WorkflowRunId != null && directRunIds.Contains(t.WorkflowRunId.Value))
            .Select(t => new {t.Id, t.Status, RunId = t.WorkflowRunId!.Value}).ToListAsync(ct);
        if (tasks.Count == 0) return null;
        if (tasks.Count != 1) return [];
        if (tasks[0].Status != AcquisitionStatus.Completed) return [];
        var task = tasks[0]; var id = task.Id.ToString(CultureInfo.InvariantCulture);
        var runs = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && s.EntityId == id).Select(s => s.WorkflowRunId).Distinct().ToListAsync(ct);
        if (!runs.Contains(task.RunId)) runs.Add(task.RunId);
        return directRunIds.All(runs.Contains) ? runs : [];
    }
}
