namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionExecutionFactProjection
{
    // The private checkpoint can contain 100,000 file paths. Only identities/counts are
    // returned. Invalid, oversized or untyped old checkpoints yield no positive facts.
    private const string CurrentSql = """
        WITH c AS (
          SELECT t.Id TaskId, t.PlanRevision, r.Id RunId, r.Status RunStatus, r.CurrentStepIndex,
            r.WaitReason, r.PendingSignalJson,
            CASE WHEN length(t.PlanJson)<=1048576 AND json_valid(t.PlanJson) THEN t.PlanJson ELSE '{{}}' END Plan,
            CASE WHEN length(r.PlanSnapshotJson)<=4194304 AND json_valid(r.PlanSnapshotJson) THEN r.PlanSnapshotJson ELSE '{{}}' END Frozen,
            CASE WHEN length(r.CurrentItemJson)<=8388608 AND json_valid(r.CurrentItemJson) THEN r.CurrentItemJson ELSE '{{}}' END Item,
            CASE WHEN length(r.WaitPromptJson)<=65536 AND json_valid(r.WaitPromptJson) THEN r.WaitPromptJson END PromptJson
          FROM AcquisitionTasks t JOIN WorkflowRuns r ON r.Id=t.WorkflowRunId
          WHERE t.Id IN (SELECT value FROM json_each({0})) AND t.DeletedAt IS NULL
            AND t.Status NOT IN (4,6) AND r.Status IN (4,6,7) AND r.CurrentStepIndex>=0
        ), normalized AS (
          SELECT c.*,
            CASE WHEN json_extract(Item,'$.type')={1} AND json_type(Item,'$.item.targets')='array'
              AND json_array_length(Item,'$.item.targets') BETWEEN 1 AND 100
              THEN json_extract(Item,'$.item.targets') ELSE '[]' END Targets,
            CASE WHEN json_type(Plan,'$.parts')='array' AND json_array_length(Plan,'$.parts') BETWEEN 1 AND 100
              THEN json_extract(Plan,'$.parts') ELSE '[]' END Parts,
            CASE WHEN json_type(Frozen,'$.nodes')='array' AND json_array_length(Frozen,'$.nodes') BETWEEN 1 AND 512
              THEN json_extract(Frozen,'$.nodes') ELSE '[]' END Nodes
          FROM c
        )
        SELECT c.TaskId,c.RunId,c.RunStatus,
          CASE WHEN c.PendingSignalJson IS NULL THEN 0 ELSE 1 END HasPendingSignal,
          CASE WHEN EXISTS (SELECT 1 FROM WorkflowRunSourceDbModel s WHERE s.WorkflowRunId=c.RunId AND s.Module='acquisition'
            AND s.EntityKind='task' AND s.EntityId=CAST(c.TaskId AS TEXT) AND s.Revision=CAST(c.PlanRevision AS TEXT))
            AND NOT EXISTS (SELECT 1 FROM WorkflowRunSourceDbModel s WHERE s.WorkflowRunId=c.RunId AND s.Module='acquisition'
              AND s.EntityKind='task' AND s.EntityId<>CAST(c.TaskId AS TEXT)) THEN 1 ELSE 0 END OwnsRun,
          json_extract(c.Frozen,'$.triggerKind') TriggerKind,c.WaitReason,c.PromptJson,
          MAX(0,json_array_length(c.Nodes)-c.CurrentStepIndex) RemainingNodes,
          n.NodeId,n.Kind NodeKind,COALESCE(n.Attempt,0) NodeAttempt,n.Status NodeStatus,n.WaitReason NodeWaitReason,
          (SELECT json_group_array(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.id'))
            FROM json_each(c.Parts) p) PartIdsJson,
          (SELECT json_group_array(json_object(
            'valid',json(CASE WHEN p.type='object' AND json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.id')='text' AND length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.id'))>0
              AND (json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.excluded') IS NULL OR json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.excluded') IN ('true','false'))
              AND (json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publishedFiles') IS NULL OR json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publishedFiles')='array')
              AND (json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview') IS NULL OR json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview') IN ('object','null'))
              AND (json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.accepted') IS NULL OR json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.accepted') IN ('true','false'))
              AND NOT EXISTS (SELECT 1 FROM json_each(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END) f
                WHERE f.key IN ('acquisitionNonce','downloadArtifactId','downloadOperationId','processedArtifactId','processingOperationId',
                  'publishedArtifactId','publicationOperationId','publicationStatus','processingWaitReason','publicationWaitReason') AND f.type NOT IN ('text','null'))
              THEN 'true' ELSE 'false' END),
            'id',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.id'),
            'excluded',json(CASE WHEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.excluded')=1 THEN 'true' ELSE 'false' END),
            'acquisitionNonce',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.acquisitionNonce'),
            'downloadArtifactId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.downloadArtifactId'),'downloadOperationId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.downloadOperationId'),
            'processedArtifactId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.processedArtifactId'),'processingOperationId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.processingOperationId'),
            'publishedArtifactId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publishedArtifactId'),'publicationOperationId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publicationOperationId'),'publishedCount',COALESCE(json_array_length(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publishedFiles'),0),
            'publicationStatus',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publicationStatus'),'processingWaitReason',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.processingWaitReason'),
            'publicationWaitReason',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.publicationWaitReason'),
            'reviewArtifactId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.artifactId'),'reviewFingerprint',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.fingerprint'),
            'reviewWaitReason',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.waitReason'),
            'reviewAccepted',json(CASE WHEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.deliveryReview.accepted')=1 THEN 'true' ELSE 'false' END)
          )) FROM json_each(c.Targets) p WHERE p.type='object') TargetsJson,
          (SELECT json_group_array(json_object('nodeId',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.nodeId'),'kind',json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.kind'),
            'configValid',json(CASE WHEN json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') IS NULL OR
              (json_type(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson')='text' AND
                length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND
                json_type(CASE WHEN json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                  THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE 'null' END)='object')
              THEN 'true' ELSE 'false' END),
            'targetId',json_extract(CASE WHEN length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE '{{}}' END,'$.targetId'),
            'source',json_extract(CASE WHEN length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE '{{}}' END,'$.source'),
            'continueArtifactId',json_extract(CASE WHEN length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE '{{}}' END,'$.continueFrom.artifactId'),
            'continueOperationId',json_extract(CASE WHEN length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE '{{}}' END,'$.continueFrom.operationId'),
            'continueFingerprint',json_extract(CASE WHEN length(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))<=65536 AND json_valid(json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson'))
                THEN json_extract(CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END,'$.configJson') ELSE '{{}}' END,'$.continueFrom.fingerprint')
          )) FROM json_each(c.Nodes) p WHERE p.type='object' AND CAST(p.key AS INTEGER)>=c.CurrentStepIndex) NodesJson
        FROM normalized c LEFT JOIN WorkflowNodeExecutions n ON n.Id=(
          SELECT x.Id FROM WorkflowNodeExecutions x WHERE x.WorkflowRunId=c.RunId AND x.StepIndex=c.CurrentStepIndex
          ORDER BY x.Attempt DESC,x.Id DESC LIMIT 1)
        """;

    // Full manifests remain in SQLite. Available is a recorded lifecycle state, not a fresh
    // stat/hash result. A retained old manifest on a Retired/Quarantined artifact is excluded.
    private const string ArtifactSql = """
        WITH c AS (
          SELECT a.*,o.Kind OperationKind,o.State OperationState,o.OutputArtifactId,
            CASE WHEN length(a.ManifestJson)<=1048576 AND json_valid(a.ManifestJson) THEN a.ManifestJson ELSE '{{}}' END Manifest
          FROM Artifacts a JOIN ArtifactOperations o ON a.OperationId=o.Id
          WHERE a.OperationId IN (SELECT value FROM json_each({0}))
        ), normalized AS (
          SELECT c.*,
            CASE WHEN json_type(Manifest,'$.entries')='array' AND json_array_length(Manifest,'$.entries') BETWEEN 1 AND 10000
              THEN json_extract(Manifest,'$.entries') ELSE '[]' END Entries,
            CASE WHEN json_type(Manifest,'$.directories')='array' AND json_array_length(Manifest,'$.directories')<=10000
              THEN json_extract(Manifest,'$.directories') ELSE '[]' END Directories
          FROM c
        ), entries AS (
          SELECT c.Id,e.type EntryType,
            json_extract(CASE WHEN e.type='object' THEN e.value ELSE '{{}}' END,'$.relativePath') Path,
            json_type(CASE WHEN e.type='object' THEN e.value ELSE '{{}}' END,'$.relativePath') PathType,
            json_extract(CASE WHEN e.type='object' THEN e.value ELSE '{{}}' END,'$.length') Length,
            json_type(CASE WHEN e.type='object' THEN e.value ELSE '{{}}' END,'$.length') LengthType,
            json_extract(CASE WHEN e.type='object' THEN e.value ELSE '{{}}' END,'$.sha256') Hash
          FROM normalized c,json_each(c.Entries) e
        ), paths AS (
          SELECT Id,Path,PathType FROM entries
          UNION ALL SELECT c.Id,d.value,d.type FROM normalized c,json_each(c.Directories) d
        )
        SELECT Id,OperationId,Kind,OperationKind,Fingerprint,
          CASE WHEN State=1 AND OperationState=3 AND OutputArtifactId=Id AND length(RootPath)>0
            AND length(Fingerprint)=64 AND Fingerprint NOT GLOB '*[^0-9a-fA-F]*'
            AND json_extract(Manifest,'$.fingerprint')=Fingerprint AND json_array_length(Entries)>0
            AND (json_type(Manifest,'$.directories') IS NULL OR json_type(Manifest,'$.directories')='null' OR
              json_type(Manifest,'$.directories')='array' AND json_array_length(Manifest,'$.directories')<=10000)
            AND NOT EXISTS (SELECT 1 FROM entries e WHERE e.Id=c.Id AND (e.EntryType<>'object' OR
              COALESCE(e.LengthType,'')<>'integer' OR e.Length<0 OR COALESCE(length(e.Hash),0)<>64 OR e.Hash GLOB '*[^0-9a-fA-F]*'))
            AND NOT EXISTS (SELECT 1 FROM paths p WHERE p.Id=c.Id AND (COALESCE(p.PathType,'')<>'text' OR
              length(trim(p.Path))=0 OR length(p.Path)>32768 OR substr(p.Path,1,1)='/' OR substr(p.Path,-1)='/' OR
              instr(p.Path,'\')>0 OR instr(p.Path,':')>0 OR instr(p.Path,'//')>0 OR instr(p.Path,char(0))>0 OR
              instr('/'||p.Path||'/','/../')>0 OR instr('/'||p.Path||'/','/./')>0))
            AND NOT EXISTS (SELECT 1 FROM paths p WHERE p.Id=c.Id GROUP BY lower(p.Path) HAVING count(*)>1)
            THEN 1 ELSE 0 END Valid
        FROM normalized c
        """;
}
