import { useEffect, useState } from "react";
import BApi from "@/sdk/BApi";
import { AcquisitionStatus } from "@/sdk/constants";
import type { PlanTaskDetail } from "@/components/Acquire/PlanTask/models";
import type { RuntimeOverview } from "@/components/Acquire/PlanTask/runtimeOverview";

export type TaskDirectoryContext = { taskId: number; runId: number };
export type TaskDirectoryAccess = "checking" | "editable" | "active" | "historical" | "unavailable";

/** UI admission only. The existing filesystem APIs still enforce storage permissions. */
export function taskDirectoryAccess(detail: PlanTaskDetail, runtime: RuntimeOverview | undefined,
  context: TaskDirectoryContext, path: string): TaskDirectoryAccess {
  const task = detail.task;
  if (task.id !== context.taskId || task.workflowRunId !== context.runId || task.deletedAt || task.isDeleted)
    return "historical";
  if (!runtime || runtime.taskId !== task.id || runtime.workflowRunId !== context.runId) return "unavailable";
  const directories = detail.stageDirectories;
  const bound = directories?.taskId === task.id && Object.values(directories.parts).some(part =>
    [part.download, part.processing, part.delivery].some(location => location?.directory === path));
  const input = (detail.replanArtifacts ?? []).some(artifact => artifact.rootDirectory === path && artifact.canReadCurrentDirectory === true);
  if (!bound && !input) return "historical";
  if ([AcquisitionStatus.Pending, AcquisitionStatus.Running].includes(task.status) ||
    ["pending", "running"].includes(runtime.workflowStatus ?? "") || runtime.activeDownloadCount > 0 ||
    ["running", "queued"].includes(runtime.downloadActivity ?? "")) return "active";
  if (!["waiting", "failed", "interrupted", "cancelled", "success"].includes(runtime.workflowStatus ?? ""))
    return "unavailable";
  return "editable";
}

/** Revoke editing when a new attempt, queued download, deletion or failed status refresh is observed. */
export default function useTaskDirectoryAccess(context: TaskDirectoryContext | undefined, path: string) {
  const key = context ? JSON.stringify([context.taskId, context.runId, path]) : "";
  const [result, setResult] = useState<{ key: string; access: TaskDirectoryAccess }>();
  useEffect(() => {
    if (!context) return;
    const controller = new AbortController();
    let busy = false;
    const refresh = async () => {
      if (busy) return;
      busy = true;
      try {
        const options = { signal: controller.signal, showErrorToast: false, sensitiveResponse: true };
        const [detail, runtime] = await Promise.all([
          BApi.acquisition.getAcquisitionPlanTask(context.taskId, options),
          BApi.acquisition.getAcquisitionRuntimeOverviews({ taskIds: [context.taskId] }, options),
        ]);
        if (controller.signal.aborted) return;
        setResult({ key, access: detail.code || runtime.code || !detail.data ? "unavailable"
          : taskDirectoryAccess(detail.data, runtime.data?.find(item => item.taskId === context.taskId), context, path) });
      } catch { if (!controller.signal.aborted) setResult({ key, access: "unavailable" }); }
      finally { busy = false; }
    };
    void refresh();
    const timer = setInterval(() => void refresh(), 3000);
    const onFocus = () => void refresh();
    window.addEventListener("focus", onFocus);
    return () => { controller.abort(); clearInterval(timer); window.removeEventListener("focus", onFocus); };
  }, [key]);
  return context ? result?.key === key ? result.access : "checking" : "historical";
}
