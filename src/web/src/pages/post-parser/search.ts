import type { PostParserTask } from "@/core/models/PostParserTask";

import { getDownloadInfo, getTargetResult } from "./results";

/** Index all persisted records, including rows that are outside the virtual viewport. */
export function buildTaskSearchText(task: PostParserTask): string {
  const downloads = getDownloadInfo(task);

  return [
    String(task.id),
    task.title,
    task.link,
    task.text,
    task.content,
    task.contentSnapshot?.mainHtml,
    ...(task.contentSnapshot?.comments?.map((comment) => comment.html) ??
      task.contentSnapshot?.commentHtmlList ??
      []),
    task.error,
    downloads?.title,
    ...(downloads?.resources?.flatMap((resource) => [
      resource.link,
      resource.code,
      resource.password,
      JSON.stringify(resource.extraction ?? ""),
    ]) ?? []),
    ...task.targets.map((target) => getTargetResult(task, target)?.error),
  ]
    .filter(Boolean)
    .join("\n")
    .toLocaleLowerCase();
}
