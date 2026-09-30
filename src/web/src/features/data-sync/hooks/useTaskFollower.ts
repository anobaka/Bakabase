import type { BTask } from "@/core/models/BTask";

import { useCallback, useEffect, useRef, useState } from "react";

import { findTask, taskPhase } from "./useDataSyncTask";

import { useBTasksStore } from "@/stores/bTasks";

/** How long a task may stay out of the task list before it is taken as over. */
export const UNSEEN_TASK_MS = 30_000;

interface Followed {
  taskId: string;
  /** An earlier run under the same id, still listed when this one was asked for (`listedTasks`). */
  earlier?: string;
  sentAt: number;
  /** The task list has shown it; its end is being said. Noted as they happen, never rendered. */
  seen?: boolean;
  ending?: boolean;
}

/**
 * Follows the data sync tasks started from here — a decision of "Needs you", an undo — each under
 * the keys it is shown on; one task can carry several keys (a bulk decision). A task that fails
 * or is cancelled is said through `onFailed`. One that completes, that the list showed and then
 * no longer does, or that it never showed for {@link UNSEEN_TASK_MS}, is over: `onOver` reads
 * again, and its keys stop running once that read is in. The caller takes `listedTasks()` before
 * it sends the request, so an earlier run under the same id is never taken for the new one.
 */
export function useTaskFollower<K>(handlers: {
  onOver: (keys: K[]) => Promise<unknown>;
  onFailed: (keys: K[], task: BTask) => void;
}) {
  const tasks = useBTasksStore((state) => state.tasks);
  const [followed, setFollowed] = useState<ReadonlyMap<K, Followed>>(new Map());
  const [tick, setTick] = useState(0);
  const latest = useRef(handlers);

  latest.current = handlers;

  // Only the keys still following that task: one sent again meanwhile is followed on its own.
  const drop = useCallback(
    (keys: K[], entry: Followed) =>
      setFollowed((current) => {
        const next = new Map(current);

        for (const key of keys) if (next.get(key) === entry) next.delete(key);

        return next;
      }),
    [],
  );

  useEffect(() => {
    const byTask = new Map<Followed, K[]>();

    for (const [key, entry] of followed) byTask.set(entry, [...(byTask.get(entry) ?? []), key]);
    for (const [entry, keys] of byTask) {
      if (entry.ending) continue;
      const task = findTask(tasks, entry.taskId, entry.earlier);
      const phase = taskPhase(task);

      if (task && (phase === "failed" || phase === "cancelled")) {
        entry.ending = true;
        drop(keys, entry);
        latest.current.onFailed(keys, task);
      } else if (
        phase === "completed" ||
        (!task && (entry.seen || Date.now() - entry.sentAt >= UNSEEN_TASK_MS))
      ) {
        entry.ending = true;
        void latest.current.onOver(keys).finally(() => drop(keys, entry));
      } else if (task) entry.seen = true;
    }
  }, [tasks, followed, tick, drop]);

  // A task never shown is judged again once it could have been.
  useEffect(() => {
    if (!Array.from(followed.values()).some((entry) => !entry.seen)) return;
    const timer = setTimeout(() => setTick((count) => count + 1), UNSEEN_TASK_MS);

    return () => clearTimeout(timer);
  }, [followed]);

  const follow = useCallback((keys: K[], taskId: string, earlier?: string) => {
    const entry: Followed = { taskId, earlier, sentAt: Date.now() };

    setFollowed((current) => {
      const next = new Map(current);

      for (const key of keys) next.set(key, entry);

      return next;
    });
  }, []);

  return { running: followed as ReadonlyMap<K, unknown>, follow };
}
