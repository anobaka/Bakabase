import { useEffect, useRef } from "react";
import { useLocation } from "react-router-dom";

export const postTaskIdFromSearch = (search: string) => {
  const raw = new URLSearchParams(search).get("taskId") ?? "";
  if (!/^[1-9]\d*$/.test(raw)) return undefined;
  const id = Number(raw);
  return Number.isSafeInteger(id) && id <= 2147483647 ? id : undefined;
};

/** Navigates the existing paged list only; never starts parsing or acquisition. */
export default function usePostTaskDeepLink(onLocate: (taskId: number) => void) {
  const location = useLocation();
  const taskId = postTaskIdFromSearch(location.search);
  const callback = useRef(onLocate);
  callback.current = onLocate;
  const consumed = useRef<string>();
  useEffect(() => {
    const key = JSON.stringify([location.key, taskId]);
    if (!taskId || consumed.current === key) return;
    consumed.current = key;
    callback.current(taskId);
  }, [location.key, taskId]);
  return taskId;
}
