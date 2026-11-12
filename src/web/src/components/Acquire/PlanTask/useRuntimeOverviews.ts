import { useEffect, useRef, useState } from "react";
import { planTaskApi } from "./api";
import type { RuntimeOverview } from "./runtimeOverview";

/** Refresh the current page as one read; keep existing rows and ignore responses for a departed page. */
export default function useRuntimeOverviews(
  tasks: { id: number; workflowRunId?: number | null; planRevision?: number | null }[],
) {
  const identities = Object.fromEntries(
    tasks
      .filter((task) => Number.isInteger(task.id) && task.id > 0)
      .map((task) => [
        task.id,
        `${task.id}:${task.workflowRunId ?? ""}:${task.planRevision ?? ""}`,
      ]),
  );
  const idsKey = Object.values(identities).sort().join(",");
  const [cached, setCached] = useState<
    Record<number, { identity: string; overview: RuntimeOverview }>
  >({});
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(false);
  const epoch = useRef(0);

  useEffect(() => {
    const generation = ++epoch.current;
    const identitiesForRequest = Object.fromEntries(
      idsKey ? idsKey.split(",").map((identity) => [Number(identity.split(":")[0]), identity]) : [],
    );
    const ids = Object.keys(identitiesForRequest)
      .map(Number)
      .sort((a, b) => a - b);
    const controller = new AbortController();
    let busy = false;
    const refresh = async () => {
      if (!ids.length || busy || document.hidden) return;
      busy = true;
      setLoading(true);
      try {
        const result = await planTaskApi.runtimeOverviews(ids, controller.signal);
        if (epoch.current !== generation || controller.signal.aborted) return;
        setCached((previous) => {
          const next = { ...previous };
          for (const id of ids) delete next[id];
          for (const overview of result)
            if (ids.includes(overview.taskId))
              next[overview.taskId] = { identity: identitiesForRequest[overview.taskId], overview };
          return next;
        });
        setFailed(false);
      } catch {
        if (epoch.current === generation && !controller.signal.aborted) setFailed(true);
      } finally {
        busy = false;
        if (epoch.current === generation && !controller.signal.aborted) setLoading(false);
      }
    };
    if (!ids.length) {
      setLoading(false);
      setFailed(false);
    }
    void refresh();
    const timer = setInterval(() => void refresh(), 5000);
    const onVisibility = () => {
      if (!document.hidden) void refresh();
    };
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      controller.abort();
      clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [idsKey]);

  const overviews = Object.fromEntries(
    Object.entries(cached)
      .filter(([id, entry]) => entry.identity === identities[Number(id)])
      .map(([id, entry]) => [id, entry.overview]),
  );
  return { overviews, failed, loading };
}
