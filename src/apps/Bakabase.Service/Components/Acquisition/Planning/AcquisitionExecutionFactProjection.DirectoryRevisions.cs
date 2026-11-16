using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionExecutionFactProjection
{
    private async Task PreserveDirectoryOnlyRunFactsAsync(IReadOnlyList<CurrentFact> current, CancellationToken ct)
    {
        var candidates = current.Where(row => row.OwnsRun == 0).Select(row => row.TaskId).Distinct().ToArray();
        if (candidates.Length == 0) return;
        var proven = await db.Database.SqlQueryRaw<DirectoryRevisionOwner>(DirectoryRevisionOwnershipSql,
            JsonSerializer.Serialize(candidates)).ToListAsync(ct);
        var identities = proven.Select(row => (row.TaskId, row.RunId)).ToHashSet();
        foreach (var row in current)
            if (identities.Contains((row.TaskId, row.RunId))) row.OwnsRun = 1;
    }

    public sealed class DirectoryRevisionOwner
    {
        public int TaskId { get; set; }
        public int RunId { get; set; }
    }

    // Changing a stage directory adds a plan revision without rewriting the existing run's
    // source revision or immutable workset. That run still records downloaded local input.
    // Accept only a complete chain produced by directory edits, with identical plans after
    // removing that one field. A processing/source edit, missing history or another run
    // cannot inherit these facts. Current directory capture separately decides whether a
    // new continuation has files; this list projection never probes or mutates storage.
    // Histories can repeat large artifact manifests: reduce them in SQLite and return only
    // the two proven identities, not history JSON or file lists.
    private const string DirectoryRevisionOwnershipSql = """
        WITH candidates AS (
          SELECT DISTINCT t.Id TaskId,t.WorkflowRunId RunId,t.PlanRevision,
            CAST(s.Revision AS INTEGER) RunRevision,
            CASE WHEN length(t.PlanJson)<=1048576 AND json_valid(t.PlanJson)
              THEN t.PlanJson ELSE 'null' END Plan,
            CASE WHEN length(t.PlanHistoryJson)<=16777216 AND json_valid(t.PlanHistoryJson)
              THEN t.PlanHistoryJson ELSE 'null' END History
          FROM AcquisitionTasks t JOIN WorkflowRunSourceDbModel s ON s.WorkflowRunId=t.WorkflowRunId
          WHERE t.Id IN (SELECT value FROM json_each({0})) AND t.DeletedAt IS NULL
            AND s.Module='acquisition' AND s.EntityKind='task' AND s.EntityId=CAST(t.Id AS TEXT)
            AND s.Revision=CAST(CAST(s.Revision AS INTEGER) AS TEXT)
            AND CAST(s.Revision AS INTEGER)>0 AND t.PlanRevision>CAST(s.Revision AS INTEGER)
            AND t.PlanRevision-CAST(s.Revision AS INTEGER)<100
            AND NOT EXISTS (SELECT 1 FROM WorkflowRunSourceDbModel other
              WHERE other.WorkflowRunId=t.WorkflowRunId AND other.Module='acquisition' AND other.EntityKind='task'
                AND (other.EntityId<>s.EntityId OR other.Revision IS NULL OR other.Revision<>s.Revision))
        ), valid AS (
          SELECT * FROM candidates
          WHERE json_type(Plan)='object' AND json_type(Plan,'$.stageDirectories')='object'
            AND json_type(History)='array' AND json_array_length(History) BETWEEN 2 AND 100
        ), revisions AS (
          SELECT c.TaskId,p.type EntryType,
            CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END Entry
          FROM valid c,json_each(c.History) p
        ), bounded AS (
          SELECT c.*,r.Entry,
            json_extract(r.Entry,'$.revision') Revision
          FROM valid c JOIN revisions r ON r.TaskId=c.TaskId
          WHERE r.EntryType='object' AND json_type(r.Entry,'$.revision')='integer'
            AND json_extract(r.Entry,'$.revision') BETWEEN c.RunRevision AND c.PlanRevision
        )
        SELECT c.TaskId,c.RunId FROM valid c
        WHERE NOT EXISTS (SELECT 1 FROM revisions r WHERE r.TaskId=c.TaskId AND (
            r.EntryType<>'object' OR json_type(r.Entry,'$.revision') IS NOT 'integer'
            OR json_extract(r.Entry,'$.revision')<1 OR json_extract(r.Entry,'$.revision')>c.PlanRevision))
          AND (SELECT count(*) FROM bounded b WHERE b.TaskId=c.TaskId)=c.PlanRevision-c.RunRevision+1
          AND (SELECT count(DISTINCT b.Revision) FROM bounded b WHERE b.TaskId=c.TaskId)=c.PlanRevision-c.RunRevision+1
          AND NOT EXISTS (SELECT 1 FROM bounded b WHERE b.TaskId=c.TaskId AND (
            json_type(b.Entry,'$.plan') IS NOT 'object'
            OR length(json_extract(b.Entry,'$.plan'))>1048576
            OR json_remove(json_extract(b.Entry,'$.plan'),'$.stageDirectories')<>json_remove(c.Plan,'$.stageDirectories')
            OR json_type(b.Entry,'$.sourceRefs') IS NOT 'array'
            OR json_extract(b.Entry,'$.sourceRefs')<>(SELECT json_extract(first.Entry,'$.sourceRefs')
              FROM bounded first WHERE first.TaskId=c.TaskId AND first.Revision=c.RunRevision)
            OR (b.Revision>c.RunRevision AND (
              json_extract(b.Entry,'$.reason') IS NOT 'directories.changed'
              OR json_type(b.Entry,'$.previousRunId') IS NOT 'integer'
              OR json_extract(b.Entry,'$.previousRunId')<>c.RunId
              OR json_type(b.Entry,'$.plan.stageDirectories') IS NOT 'object'
              OR COALESCE(json_type(b.Entry,'$.sourceRevision'),'null')<>'null'
            ))))
          AND EXISTS (SELECT 1 FROM bounded b WHERE b.TaskId=c.TaskId AND b.Revision=c.PlanRevision
            AND json_extract(b.Entry,'$.plan')=json(c.Plan))
        """;
}
