import { useCallback, useEffect, useRef, useState } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";
import type { TaskStatusGroup } from "./taskStatus";
import type { BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTaskPage } from "@/sdk/Api";
import BApi from "@/sdk/BApi";
import { usePostParserTasksStore } from "@/stores/postParserTasks";

export const POST_PARSER_SEARCH_DEBOUNCE_MS = 180;

export type PostParserPageData = Omit<
  BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTaskPage,
  "items" | "statusCounts"
> & {
  items: PostParserTask[];
  statusCounts: Record<TaskStatusGroup, number>;
};

export default function usePostParserTaskPage({
  keyword,
  status,
  reasonCode,
  pageIndex,
  pageSize,
  locateTaskId,
}: {
  keyword: string;
  status: TaskStatusGroup | "all";
  reasonCode?: string;
  pageIndex: number;
  pageSize: number;
  locateTaskId?: number;
}) {
  const changeVersion = usePostParserTasksStore((state) => state.changeVersion);
  const [debouncedKeyword, setDebouncedKeyword] = useState(keyword.trim());
  const [result, setResult] = useState<{ data: PostParserPageData; scope: string }>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const sequence = useRef(0);
  const mounted = useRef(true);
  const active = useRef<{ key: string; controller: AbortController; promise: Promise<void> }>();
  const dirty = useRef(false);
  const query = JSON.stringify({
    keyword: debouncedKeyword || undefined,
    status,
    reasonCode,
    pageIndex,
    pageSize,
    locateTaskId,
  });
  const latestQuery = useRef(query);
  latestQuery.current = query;
  const debouncing = keyword.trim() !== debouncedKeyword;

  useEffect(() => {
    if (!keyword.trim()) {
      setDebouncedKeyword("");
      return;
    }
    const timer = window.setTimeout(
      () => setDebouncedKeyword(keyword.trim()),
      POST_PARSER_SEARCH_DEBOUNCE_MS,
    );
    return () => window.clearTimeout(timer);
  }, [keyword]);

  const load: (force?: boolean, invalidated?: boolean) => Promise<void> = useCallback(
    (force = false, invalidated = false) => {
      if (!mounted.current || debouncing || query !== latestQuery.current) return Promise.resolve();
      if (!force && active.current?.key === query) {
        // A poll overlapping a slow response needs no second request. Only a real
        // change notification can make that in-flight snapshot obsolete.
        if (invalidated) dirty.current = true;
        return active.current.promise;
      }
      active.current?.controller.abort();
      dirty.current = false;
      const controller = new AbortController();
      const request = ++sequence.current;
      setLoading(true);
      setError(false);
      const promise = (async () => {
        try {
          const response = await BApi.postParser.searchPostParserTasks(JSON.parse(query), {
            signal: controller.signal,
            showErrorToast: false,
            sensitiveResponse: true,
          });
          if (request !== sequence.current || query !== latestQuery.current) return;
          if (response.code || !response.data || !Array.isArray(response.data.items))
            throw new Error("POST_PAGE_UNAVAILABLE");
          const data = response.data as PostParserPageData;
          const parameters = JSON.parse(query);
          setResult({
            data,
            scope: JSON.stringify([
              parameters.keyword ?? "",
              parameters.status,
              parameters.reasonCode ?? "",
              data.pageIndex,
              data.pageSize,
            ]),
          });
          setError(false);
        } catch {
          if (
            request === sequence.current &&
            query === latestQuery.current &&
            !controller.signal.aborted
          )
            setError(true);
        } finally {
          if (request === sequence.current) {
            active.current = undefined;
            setLoading(false);
            if (dirty.current && !controller.signal.aborted && query === latestQuery.current) {
              dirty.current = false;
              queueMicrotask(() => {
                if (request === sequence.current && query === latestQuery.current) void load();
              });
            }
          }
        }
      })();
      active.current = { key: query, controller, promise };
      return promise;
    },
    [query, debouncing],
  );
  const latestLoad = useRef(load);
  latestLoad.current = load;

  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      sequence.current++;
      active.current?.controller.abort();
      active.current = undefined;
    };
  }, [load]);

  const previousVersion = useRef(changeVersion);
  const invalidationTimer = useRef<number>();
  useEffect(() => {
    if (previousVersion.current === changeVersion) return;
    previousVersion.current = changeVersion;
    // A burst schedules one refresh within a bounded window, even while updates keep arriving.
    if (invalidationTimer.current == null)
      invalidationTimer.current = window.setTimeout(() => {
        invalidationTimer.current = undefined;
        void latestLoad.current(false, true);
      }, 350);
  }, [changeVersion]);
  useEffect(() => () => window.clearTimeout(invalidationTimer.current), []);

  const hasActiveTasks =
    !!result &&
    (result.data.pendingHandoffCount > 0 ||
      ["running", "queued", "paused"].some(
        (group) => result.data.statusCounts[group as TaskStatusGroup] > 0,
      ));
  useEffect(() => {
    if (!hasActiveTasks) return;
    const timer = window.setInterval(() => {
      if (!document.hidden) void load();
    }, 2000);
    return () => window.clearInterval(timer);
  }, [hasActiveTasks, load]);

  useEffect(() => {
    const refreshVisible = () => {
      if (!document.hidden) void load();
    };
    window.addEventListener("focus", refreshVisible);
    document.addEventListener("visibilitychange", refreshVisible);
    return () => {
      window.removeEventListener("focus", refreshVisible);
      document.removeEventListener("visibilitychange", refreshVisible);
    };
  }, [load]);

  // Row actions retain the same callback while the user edits the search query.
  const refresh = useCallback(() => latestLoad.current(true), []);
  const expectedScope = JSON.stringify([
    debouncedKeyword,
    status,
    reasonCode ?? "",
    pageIndex,
    pageSize,
  ]);
  const changing = debouncing || (!!result && result.scope !== expectedScope);
  return {
    data: result?.data,
    scope: result?.scope,
    loading: loading || debouncing || (changing && !error),
    changing,
    error,
    refresh,
  };
}
