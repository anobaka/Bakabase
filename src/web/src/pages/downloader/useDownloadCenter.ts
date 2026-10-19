import { useCallback, useEffect, useRef, useState } from "react";
import type { CenterTask } from "./downloadCenter";
import BApi from "@/sdk/BApi";

/** One complete snapshot for every task type, irrespective of list size or expanded rows. */
export default function useDownloadCenter() {
  const [tasks, setTasks] = useState<CenterTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const generation = useRef(0);
  const pending = useRef(false);
  const mounted = useRef(false);
  const refresh = useCallback(async () => {
    const request = ++generation.current;
    pending.current = true;
    try {
      const response = await BApi.downloadCenter.getDownloadCenterTasks();
      if (!mounted.current || request !== generation.current) return;
      if (response.code) throw new Error(response.message);
      setTasks((current) => {
        const previous = new Map(current.map((task) => [task.id, task]));
        return (response.data ?? []).map((task) => {
          const old = previous.get(task.id);
          return old && JSON.stringify(old) === JSON.stringify(task) ? old : task;
        });
      });
      setLoadFailed(false);
    } catch {
      if (mounted.current && request === generation.current) setLoadFailed(true);
    } finally {
      if (request === generation.current) {
        pending.current = false;
        if (mounted.current) setLoading(false);
      }
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void refresh();
    const timer = setInterval(() => {
      if (!document.hidden && !pending.current) void refresh();
    }, 3000);
    return () => {
      mounted.current = false;
      generation.current++;
      pending.current = false;
      clearInterval(timer);
    };
  }, [refresh]);
  return { tasks, loading, loadFailed, refresh };
}
