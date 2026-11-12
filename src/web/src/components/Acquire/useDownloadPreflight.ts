import { useCallback, useEffect, useRef, useState } from "react";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import type { AcquireAdapter, AcquirePreflight, AcquireRequest } from "./models";

/** Shared source readiness: debounce edits, reject stale replies and refresh after dependency changes. */
export function useDownloadPreflight(
  adapter: AcquireAdapter,
  request: AcquireRequest,
  outputDirectory: string,
  active = true,
) {
  const [response, setResponse] = useState<{ key: string; result: AcquirePreflight }>();
  const [error, setError] = useState<string>();
  const [revision, setRevision] = useState(0);
  const key = JSON.stringify([request, outputDirectory, revision]);
  const result = response?.key === key ? response.result : undefined;
  const refresh = useCallback(() => {
    setError(undefined);
    setRevision((value) => value + 1);
  }, []);
  const dependencies = useDependentComponentContextsStore((state) =>
    JSON.stringify(
      state.contexts
        .filter((context) => response?.result.candidates.some((candidate) =>
          candidate.methods.some((method) => method.dependencyIds.includes(context.id)),
        ))
        .map((context) => [context.id, context.status, context.error]),
    ),
  );
  const previousDependencies = useRef(dependencies);
  useEffect(() => {
    if (previousDependencies.current !== dependencies) {
      previousDependencies.current = dependencies;
      refresh();
    }
  }, [dependencies, refresh]);
  useEffect(() => {
    if (!active) return;
    let disposed = false;
    setError(undefined);
    const timer = setTimeout(() => {
      void adapter.preflight(request, outputDirectory).then((value) => {
        if (!disposed) setResponse({ key, result: value });
      }).catch((failure) => {
        if (!disposed) setError(failure instanceof Error ? failure.message : String(failure));
      });
    }, 200);
    return () => { disposed = true; clearTimeout(timer); };
  }, [adapter, key, active]);
  return { result, previousResult: response?.result, error, refreshing: !result && !error, refresh, revision };
}
