using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static class AcquisitionQueueCount
{
    // Project one scalar in SQLite. Do not transfer builder histories, workflow payloads,
    // or the most recent N queue batches on every list refresh.
    internal static Task<int> ReadAsync(BakabaseDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<int>("""
        WITH states AS (
            SELECT CASE WHEN json_valid(BuilderStateJson) THEN BuilderStateJson ELSE '{{}}' END state,
                   Status, PlanRevision, WorkflowRunId
            FROM AcquisitionTasks WHERE DeletedAt IS NULL
        )
        SELECT COUNT(*) AS Value FROM states WHERE
          (json_type(state, '$.localRecoveryQueue') = 'object'
           AND json_type(state, '$.localRecoveryQueue.batchId') = 'text'
           AND length(json_extract(state, '$.localRecoveryQueue.batchId')) > 0
           AND json_extract(state, '$.localRecoveryQueue.status') IN (SELECT value FROM json_each({0}))
           AND NOT COALESCE((Status = {2} AND WorkflowRunId > 0
               AND json_extract(state, '$.localRecoveryQueue.status') IN ('paused', 'spaceBlocked')
               AND json_extract(state, '$.localRecoveryQueue.planRevision') BETWEEN 1 AND PlanRevision - 1
               AND json_extract(state, '$.localRecoveryQueue.workflowRunId') > 0
               AND json_extract(state, '$.localRecoveryQueue.workflowRunId') <> WorkflowRunId), 0))
          OR
          (json_type(state, '$.continuationRequest') = 'object'
           AND json_type(state, '$.continuationRequest.batchId') = 'text'
           AND length(json_extract(state, '$.continuationRequest.batchId')) > 0
           AND json_extract(state, '$.continuationRequest.status') IN (SELECT value FROM json_each({1})))
        """, JsonSerializer.Serialize(AcquisitionLocalRecoveryQueueEntry.OpenStatuses),
        JsonSerializer.Serialize(AcquisitionContinuationEntry.OpenStatuses), (int)AcquisitionStatus.Completed).SingleAsync(ct);
}
