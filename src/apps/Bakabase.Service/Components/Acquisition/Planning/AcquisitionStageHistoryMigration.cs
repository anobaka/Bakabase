using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A stage attempt uses the durable node journal. Where an older acquisition run lacks a source index,
/// restore the index from matching records on both sides. Never change a plan, cursor, result, or acquisition state.</summary>
internal static class AcquisitionStageHistoryMigration
{
    internal static Task<int> BackfillAsync(BakabaseDbContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync("""
            WITH missing AS MATERIALIZED (
                SELECT r.Id, json_extract(r.PayloadJson,'$.id') PayloadId
                FROM WorkflowRuns r
                WHERE r.WorkflowDefinitionId=0
                    AND NOT EXISTS (SELECT 1 FROM WorkflowRunSourceDbModel s
                        WHERE s.WorkflowRunId=r.Id AND s.Module='acquisition' AND s.EntityKind='task')
                    AND CASE WHEN json_valid(r.PayloadJson)
                        THEN json_extract(r.PayloadJson,'$.id') LIKE 'acquisition:%:%' ELSE 0 END
            ), owners AS (
                SELECT t.Id TaskId, t.WorkflowRunId RunId FROM AcquisitionTasks t JOIN missing m ON m.Id=t.WorkflowRunId
                UNION
                SELECT t.Id, m.Id
                FROM AcquisitionTasks t
                JOIN json_each(CASE WHEN json_valid(t.PlanHistoryJson) THEN t.PlanHistoryJson ELSE '[]' END) h
                JOIN missing m ON m.Id=json_extract(CASE WHEN h.type='object' THEN h.value END,'$.previousRunId')
                WHERE EXISTS (SELECT 1 FROM missing)
            ), uniqueOwners AS (
                SELECT RunId, min(TaskId) TaskId FROM owners GROUP BY RunId HAVING count(*)=1
            ), matches AS (
                SELECT u.TaskId, m.Id, m.PayloadId,
                    substr(m.PayloadId,length('acquisition:' || u.TaskId || ':')+1) Revision
                FROM uniqueOwners u JOIN missing m ON u.RunId=m.Id
            )
            INSERT INTO WorkflowRunSourceDbModel (WorkflowRunId,Module,EntityKind,EntityId,Revision,ResultKey)
            SELECT Id,'acquisition','task',CAST(TaskId AS TEXT),Revision,''
            FROM matches
            WHERE CAST(Revision AS INTEGER)>0 AND CAST(CAST(Revision AS INTEGER) AS TEXT)=Revision
                AND PayloadId='acquisition:' || TaskId || ':' || Revision
            """, ct);
}
