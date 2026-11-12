import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { WorkflowRunStatus } from "@/sdk/constants";
import { querySourceRuns, sourceRefKey, type SourceRef, type SourceRuns, type SourceHistoryFilter } from "./sourceHistory";

const activeStatuses = new Set([
  WorkflowRunStatus.Pending,
  WorkflowRunStatus.Running,
  WorkflowRunStatus.Waiting,
]);

/** One page-level subscription; rows are pure projections and never start their own polling. */
export function useSourceRuns(sources: SourceRef[], refreshKey?: string | number, filter: SourceHistoryFilter = {}) {
  const sourceIdentity = JSON.stringify(sources);
  const filterIdentity = JSON.stringify(filter);
  const identity = sourceIdentity + "|" + filterIdentity;
  const stableSources = useMemo<SourceRef[]>(() => JSON.parse(sourceIdentity), [sourceIdentity]);
  const stableFilter = useMemo<SourceHistoryFilter>(() => JSON.parse(filterIdentity), [filterIdentity]);
  const [rows, setRows] = useState<Map<string, SourceRuns>>(new Map());
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const scope = useRef<string>();
  const [revision, setRevision] = useState(0);
  const refresh = useCallback(() => setRevision((value) => value + 1), []);

  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    if (scope.current !== identity) {
      scope.current = identity;
      setRows(new Map());
      setError(undefined);
    }
    setLoading(true);
    const load = async () => {
      if (stableSources.length === 0) {
        if (!disposed) setLoading(false);
        return;
      }
      try {
        const results: SourceRuns[] = [];
        for (let offset = 0; offset < stableSources.length; offset += 100)
          results.push(...(await (Object.keys(stableFilter).length
            ? querySourceRuns(stableSources.slice(offset, offset + 100), 1, 5, stableFilter)
            : querySourceRuns(stableSources.slice(offset, offset + 100)))));
        if (disposed) return;
        setRows(new Map(results.map((row) => [sourceRefKey(row.source), row])));
        setError(undefined);
        if (
          results.some(
            (row) =>
              Object.entries(row.statusCounts ?? {}).some(
                ([status, count]) =>
                  ["pending", "running", "waiting"].includes(status) && count > 0,
              ) || row.runs.some(({ run }) => activeStatuses.has(run.status)),
          )
        )
          timer = setTimeout(() => void load(), 5000);
      } catch (failure) {
        if (disposed) return;
        setError(failure instanceof Error ? failure.message : String(failure));
        timer = setTimeout(() => void load(), 15000);
      } finally {
        if (!disposed) setLoading(false);
      }
    };
    void load();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
    };
  }, [identity, stableSources, stableFilter, revision, refreshKey]);

  return { rows, error, loading, refresh };
}
