import type { MoveSourceContext } from "./types";

export function clearedMoveTaskStorageKey(context: MoveSourceContext) {
  return `bakabase:resourceMove:clearedTasks:${JSON.stringify([context.nodeId, context.libraryEpoch])}`;
}

export function readClearedMoveTaskIds(context: MoveSourceContext): Set<string> {
  try {
    const stored = JSON.parse(localStorage.getItem(clearedMoveTaskStorageKey(context)) || "[]");

    return new Set(
      Array.isArray(stored) ? stored.filter((id): id is string => typeof id === "string") : [],
    );
  } catch {
    return new Set();
  }
}

/** Keep durable submission receipts on the server so delayed retries remain idempotent. */
export function saveClearedMoveTaskIds(context: MoveSourceContext, batchIds: Iterable<string>) {
  const merged = new Set([...readClearedMoveTaskIds(context), ...batchIds]);

  localStorage.setItem(clearedMoveTaskStorageKey(context), JSON.stringify([...merged]));

  return merged;
}
