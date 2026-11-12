import { lazy, Suspense, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { ClockCircleOutlined, ExclamationCircleOutlined } from "@ant-design/icons";
import { Button, Popover, Spinner } from "@/components/bakaui";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskResourceType, BTaskStatus } from "@/sdk/constants";
import type { BTask } from "@/core/models/BTask";

const TaskTable = lazy(() => import("@/components/FloatingAssistant/components/TaskTable")
  .then(module => ({ default: module.TaskTable })));

export function directoryTasks(tasks: BTask[], path: string) {
  const root = path.replace(/\\/g, "/").replace(/\/+$/, "");
  const latest = new Map<string, BTask>();
  for (const task of tasks) {
    if (task.resourceType !== BTaskResourceType.FileSystemEntry) continue;
    for (const key of task.resourceKeys ?? []) {
      if (typeof key !== "string") continue;
      const normalized = key.replace(/\\/g, "/").replace(/\/+$/, "");
      if (normalized !== root && !normalized.startsWith(`${root}/`)) continue;
      if (!latest.has(normalized) || task.createdAt >= latest.get(normalized)!.createdAt)
        latest.set(normalized, task);
    }
  }
  return Array.from(new Set(latest.values())).filter(task =>
    ![BTaskStatus.Completed, BTaskStatus.Cancelled].includes(task.status));
}

/** Keep job feedback reachable even if its source file has been renamed or moved away. */
export default function DirectoryTaskStatus({ path }: { path: string }) {
  const { t } = useTranslation();
  const tasks = useBTasksStore(state => state.tasks);
  const relevant = useMemo(() => directoryTasks(tasks, path), [tasks, path]);
  if (!relevant.length) return null;
  const failed = relevant.some(task => task.status === BTaskStatus.Error || !!task.error);
  const running = relevant.some(task => [BTaskStatus.Running, BTaskStatus.Resuming].includes(task.status));
  return <Popover trigger={<Button size="sm" variant="light" color={failed ? "danger" : "primary"}
    startContent={failed ? <ExclamationCircleOutlined aria-hidden /> : running ? <Spinner size="sm" /> : <ClockCircleOutlined aria-hidden />}>
    {t("fileExplorer.tasks.current", { count: relevant.length })}
  </Button>}>
    <div className="max-h-[min(60vh,32rem)] max-w-[calc(100vw-3rem)] overflow-auto p-2">
      <Suspense fallback={<Spinner size="sm" />}><TaskTable tasks={relevant} /></Suspense>
    </div>
  </Popover>;
}
