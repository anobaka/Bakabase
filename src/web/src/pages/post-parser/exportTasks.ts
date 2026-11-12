import BApi from "@/sdk/BApi";
import type { TaskFilterScope } from "./components/TaskFilterScopeSummary";
import type { PostParserTask } from "@/core/models/PostParserTask";

/** Explicit exports read bounded server pages; opening the list never loads this dataset. */
export async function loadPostParserExportTasks(
  scope: TaskFilterScope = {},
): Promise<PostParserTask[]> {
  const tasks = new Map<number, PostParserTask>();
  let pages = 1;
  for (let pageIndex = 1; pageIndex <= pages; pageIndex++) {
    const response = await BApi.postParser.searchPostParserTasks(
      { ...scope, pageIndex, pageSize: 100, status: scope.status ?? "all" },
      { sensitiveResponse: true },
    );
    if (response.code || !response.data) throw new Error(response.message || "POST_EXPORT_FAILED");
    if (pageIndex === 1) pages = Math.max(1, Math.ceil(response.data.totalCount / 100));
    for (const task of response.data.items) tasks.set(task.id, task as PostParserTask);
    if (response.data.items.length === 0) break;
  }
  return [...tasks.values()];
}
