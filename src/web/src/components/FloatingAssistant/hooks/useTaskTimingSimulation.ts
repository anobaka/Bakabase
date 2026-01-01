import { useEffect, useRef } from "react";
import moment from "moment";
import type { BTask } from "@/core/models/BTask";
import { BTaskStatus } from "@/sdk/constants";

interface TimingRecord {
  baseElapsedMs?: number;
  baseRemainingMs?: number;
  lastSeenAt: number;
  lastStatus: number;
  lastRawElapsedMs?: number;
  lastRawRemainingMs?: number;
}

export function useTaskTimingSimulation(tasks: BTask[]) {
  const simTimingRef = useRef<Map<string, TimingRecord>>(new Map());

  // When tasks update from the store, (re)initialize or adjust simulation baselines
  useEffect(() => {
    const now = Date.now();
    const map = simTimingRef.current;
    const existingIds = new Set(map.keys());

    tasks.forEach((task) => {
      const rawElapsedMs = task.elapsed
        ? moment.duration(task.elapsed).asMilliseconds()
        : undefined;
      const rawRemainingMs = task.estimateRemainingTime
        ? moment.duration(task.estimateRemainingTime).asMilliseconds()
        : undefined;

      const rec = map.get(task.id);

      if (!rec) {
        map.set(task.id, {
          baseElapsedMs: rawElapsedMs,
          baseRemainingMs: rawRemainingMs,
          lastSeenAt: now,
          lastStatus: task.status,
          lastRawElapsedMs: rawElapsedMs,
          lastRawRemainingMs: rawRemainingMs,
        });
      } else {
        const incomingChanged =
          rec.lastRawElapsedMs !== rawElapsedMs ||
          rec.lastRawRemainingMs !== rawRemainingMs;

        if (incomingChanged) {
          // Override simulation with incoming backend updates
          rec.baseElapsedMs = rawElapsedMs;
          rec.baseRemainingMs = rawRemainingMs;
          rec.lastSeenAt = now;
          rec.lastRawElapsedMs = rawElapsedMs;
          rec.lastRawRemainingMs = rawRemainingMs;
          rec.lastStatus = task.status;
        } else if (task.status !== rec.lastStatus) {
          // Commit simulated delta on status change, then reset the baseline
          const delta = Math.max(0, now - rec.lastSeenAt);

          if (rec.lastStatus === BTaskStatus.Running) {
            rec.baseElapsedMs = (rec.baseElapsedMs ?? 0) + delta;
            if (rec.baseRemainingMs != null) {
              rec.baseRemainingMs = Math.max(0, rec.baseRemainingMs - delta);
            }
          }
          rec.lastSeenAt = now;
          rec.lastStatus = task.status;
        }
      }

      existingIds.delete(task.id);
    });

    // Cleanup records of tasks that no longer exist
    existingIds.forEach((id) => map.delete(id));
  }, [tasks]);

  const computeDisplayElapsedMs = (task: BTask): number | undefined => {
    const rec = simTimingRef.current.get(task.id);
    const baseMs = rec?.baseElapsedMs;

    if (baseMs == null) return undefined;
    if (task.status === BTaskStatus.Running) {
      return baseMs + Math.max(0, Date.now() - (rec?.lastSeenAt ?? Date.now()));
    }

    return baseMs;
  };

  const computeDisplayRemainingMs = (task: BTask): number | undefined => {
    const rec = simTimingRef.current.get(task.id);
    const baseMs = rec?.baseRemainingMs;

    if (baseMs == null) return undefined;
    if (task.status === BTaskStatus.Running) {
      const ms = baseMs - Math.max(0, Date.now() - (rec?.lastSeenAt ?? Date.now()));
      return Math.max(0, ms);
    }

    return baseMs;
  };

  return {
    computeDisplayElapsedMs,
    computeDisplayRemainingMs,
  };
}
