import type { PostParserTask } from "@/core/models/PostParserTask";

import { hasBlockingContentLocks } from "./contentReadiness";
import { getDownloadInfo, getTargetResult, type DownloadInfoData } from "./results";
import { PostParseTarget } from "@/sdk/constants";

// This is the server's precise legacy-history error, not a classification of arbitrary failures.
export const missingParsingWorkflowRunError =
  "The parsing workflow run no longer exists. Parse this task again.";

function hasCompletedSavedResult(task: PostParserTask) {
  const result = getTargetResult(task, PostParseTarget.DownloadInfo);
  return (
    task.parsingState === "complete" &&
    !result?.error &&
    result?.data?.isComplete === true &&
    Array.isArray(result.data.resources) &&
    result.data.resources.every(
      (resource) => resource != null && typeof resource === "object" && !Array.isArray(resource),
    )
  );
}

/** Missing producer history does not invalidate an otherwise complete, task-owned saved result. */
export function canAcquirePostResult(
  task: PostParserTask,
  info: DownloadInfoData | undefined = getDownloadInfo(task),
) {
  const errorAllowed =
    !task.error || (task.error === missingParsingWorkflowRunError && hasCompletedSavedResult(task));
  return (
    !task.isDeleted &&
    errorAllowed &&
    !getTargetResult(task, PostParseTarget.DownloadInfo)?.error &&
    !hasBlockingContentLocks(task) &&
    info?.isComplete !== false &&
    !!info?.resources?.some(
      (resource, index) =>
        !!resource.link?.trim() && !info.excludedResourceIndices?.includes(index),
    )
  );
}

/** The server's read-only preparation remains authoritative for current proof and configuration. */
export function canCatchUpPostAcquisition(
  task: PostParserTask,
  canAcquire = canAcquirePostResult(task),
) {
  return (
    !task.acquisitionHandoff &&
    Number.isSafeInteger(task.revision) &&
    task.revision! >= 0 &&
    hasCompletedSavedResult(task) &&
    canAcquire
  );
}
