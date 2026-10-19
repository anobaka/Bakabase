import type { BakabaseModulesDownloaderModelsTasksDownloadTask } from "@/sdk/Api";

export type CenterTask = BakabaseModulesDownloaderModelsTasksDownloadTask;
export type TaskAction = "start" | "stop" | "retry" | "delete";
export type CenterFilter = {
  category: "all" | "general" | "site";
  provider?: string;
  status?: string;
  keyword?: string;
  id?: string;
};
export const centerStatuses = [
  "preparing",
  "queued",
  "running",
  "stopping",
  "stopped",
  "completed",
  "failed",
  "waiting",
  "processing",
  "processingFailed",
];

export function readCenterFilter(params: URLSearchParams): CenterFilter {
  const category = params.get("category");
  return {
    category: category === "general" || category === "site" ? category : "all",
    provider: params.get("provider") || undefined,
    id: params.get("id") || undefined,
  };
}

export function btDownloadRedirect(search: string): string {
  const params = new URLSearchParams(search);
  params.set("category", "general");
  params.set("provider", "bt");
  const id = params.get("id");
  if (id && !id.startsWith("bt:")) params.set("id", `bt:${id}`);
  return `/downloader?${params}`;
}

export function indexCenterTasks(tasks: CenterTask[]) {
  const byId = new Map(tasks.map((task) => [task.id, task]));
  const children = new Map<string, CenterTask[]>();
  for (const task of byId.values()) {
    if (task.parentId && byId.has(task.parentId)) {
      const siblings = children.get(task.parentId) ?? [];
      siblings.push(task);
      children.set(task.parentId, siblings);
    }
  }
  return {
    byId,
    children,
    roots: [...byId.values()].filter((task) => !task.parentId || !byId.has(task.parentId)),
  };
}

/** Global counts use roots. A protocol filter deliberately shows the same child ID on its own. */
export function buildCenterRows(
  tasks: CenterTask[],
  filter: CenterFilter,
  expanded: Set<string>,
  sourceKeys = new Map<number, string>(),
) {
  const { byId, children, roots } = indexCenterTasks(tasks);
  const flatGeneralView =
    filter.category === "general" ||
    filter.provider === "bt" ||
    filter.provider === "http" ||
    !!filter.id;
  const candidates = flatGeneralView ? [...byId.values()] : roots;
  const matches = (task: CenterTask) => {
    if (filter.category !== "all" && task.category !== filter.category) return false;
    if (
      filter.provider &&
      (filter.provider.startsWith("site:")
        ? task.thirdPartyId !== Number(filter.provider.slice(5))
        : task.provider !== filter.provider)
    )
      return false;
    if (filter.status && task.status !== filter.status) return false;
    if (filter.id && task.id !== filter.id) return false;
    const keyword = filter.keyword?.trim().toLocaleLowerCase();
    return (
      !keyword ||
      `${task.name} ${task.outputDirectory} ${sourceKeys.get(task.siteTaskId ?? -1) ?? ""}`
        .toLocaleLowerCase()
        .includes(keyword)
    );
  };
  const filtered = candidates.filter(matches);
  const rows: { task: CenterTask; nested: boolean }[] = [];
  for (const task of filtered) {
    rows.push({ task, nested: false });
    if (!flatGeneralView && expanded.has(task.id)) {
      for (const child of children.get(task.id) ?? []) rows.push({ task: child, nested: true });
    }
  }
  return { rows, filtered, total: candidates.length, children, roots };
}

/** The server also deduplicates parent/child commands; prevent misleading UI selection counts. */
export function actionTargetIds(
  tasks: CenterTask[],
  selectedIds: string[],
  action: TaskAction,
): string[] {
  const byId = new Map(tasks.map((task) => [task.id, task]));
  const selected = new Set(selectedIds);
  return [...selected].filter((id) => {
    const task = byId.get(id);
    if (!task?.availableActions?.includes(action)) return false;
    return (
      action !== "stop" ||
      !task.parentId ||
      !selected.has(task.parentId) ||
      !byId.get(task.parentId)?.availableActions?.includes(action)
    );
  });
}

export function centerStatusColor(status: string) {
  if (status === "failed" || status === "processingFailed") return "danger" as const;
  if (status === "completed") return "success" as const;
  if (status === "running" || status === "processing") return "primary" as const;
  if (status === "stopping" || status === "waiting") return "warning" as const;
  return "default" as const;
}
