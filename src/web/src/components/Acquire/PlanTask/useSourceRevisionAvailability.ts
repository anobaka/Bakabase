import { useEffect, useRef, useState } from "react";
import { AcquisitionStatus } from "@/sdk/constants";
import type { PlanTask } from "./models";
import { sourceRevisionApi } from "./sourceRevisionApi";

/** Ask the existing source policy, including producer evidence and checkpoint safety.
 * Search candidates and mirror counts alone do not establish a replaceable source. */
export default function useSourceRevisionAvailability(task?: PlanTask) {
  const eligible = task && !task.isDeleted && !task.deletedAt && task.workflowRunId &&
    [AcquisitionStatus.Waiting, AcquisitionStatus.Failed, AcquisitionStatus.Cancelled].includes(task.status);
  const key = eligible ? JSON.stringify([task.id, task.workflowRunId, task.planRevision, task.status]) : "";
  const cache = useRef(new Map<string, boolean>());
  const [result, setResult] = useState<{ key: string; available: boolean }>();
  useEffect(() => {
    if (!key || !task) return;
    const previous = cache.current.get(key);
    if (previous !== undefined) {
      setResult({ key, available: previous });
      return;
    }
    const controller = new AbortController();
    void sourceRevisionApi.options(task.id, controller.signal).then(options => {
      if (controller.signal.aborted) return;
      const available = options.taskId === task.id && options.expectedRunId === task.workflowRunId &&
        options.expectedRevision === task.planRevision && options.canRevise && options.candidates.length > 0;
      if (cache.current.size >= 8) cache.current.clear();
      cache.current.set(key, available);
      setResult({ key, available });
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ key, available: false });
    });
    return () => controller.abort();
  }, [key]);
  return !!key && result?.key === key && result.available;
}
