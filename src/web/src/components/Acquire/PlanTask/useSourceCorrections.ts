import { useCallback, useEffect, useRef, useState } from "react";
import { planTaskApi } from "./api";
import type { PlanTask } from "./models";

export type SourceCorrections = Awaited<ReturnType<typeof planTaskApi.sourceCorrections>>;

/** Reads the server's guarded checkpoint projection, never parses a workflow wait prompt. */
export default function useSourceCorrections(taskId: number, task?: PlanTask) {
  const [result, setResult] = useState<{ key: string; data: SourceCorrections }>();
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const request = useRef(0);
  const controller = useRef<AbortController>();
  const key = JSON.stringify([
    taskId,
    task?.workflowRunId,
    task?.planRevision,
    task?.status,
    task?.isDeleted,
  ]);
  const expectedRunId = task?.workflowRunId;
  const expectedRevision = task?.planRevision;
  const enabled = !!expectedRunId;
  const refresh = useCallback(async () => {
    const sequence = ++request.current;
    controller.current?.abort();
    if (!enabled) {
      setResult(undefined);
      setFailed(false);
      setLoading(false);
      return;
    }
    const next = new AbortController();
    controller.current = next;
    setLoading(true);
    setFailed(false);
    try {
      const value = await planTaskApi.sourceCorrections(taskId, next.signal);
      if (sequence !== request.current || next.signal.aborted) return;
      if (
        value.taskId !== taskId ||
        value.expectedRunId !== expectedRunId ||
        value.expectedRevision !== expectedRevision
      )
        throw new Error("SOURCE_CORRECTION_TASK_CHANGED");
      setResult({ key, data: value });
    } catch {
      if (sequence !== request.current || next.signal.aborted) return;
      setResult(undefined);
      setFailed(true);
    } finally {
      if (sequence === request.current && !next.signal.aborted) setLoading(false);
    }
  }, [taskId, enabled, key, expectedRunId, expectedRevision]);
  useEffect(() => {
    void refresh();
    return () => {
      request.current++;
      controller.current?.abort();
    };
  }, [refresh]);
  return { data: result?.key === key ? result.data : undefined, loading, failed, refresh };
}
