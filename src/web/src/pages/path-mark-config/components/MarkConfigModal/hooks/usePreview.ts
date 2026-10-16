import type { MarkConfig } from "../types";
import type { PathMarkType } from "@/sdk/constants";

import { useState, useEffect, useMemo } from "react";

import { buildConfigJson, hasValidValueExtractor } from "../utils";

import { PathMatchMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

export type PathMarkPreviewResult = {
  path: string;
  resourceLayerIndex?: number | null;
  resourceSegmentName?: string | null;
  propertyValue?: string | null;
  valueSourcePath?: string | null;
};

export type PreviewResultsByPath = {
  path: string;
  results: PathMarkPreviewResult[];
};

export const usePreview = (
  rootPath: string | undefined,
  markType: PathMarkType,
  config: MarkConfig,
  debounceMs: number = 500,
  rootPaths?: string[],
) => {
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState<PathMarkPreviewResult[]>([]);
  const [resultsByPath, setResultsByPath] = useState<PreviewResultsByPath[]>([]);
  const [error, setError] = useState<string | null>(null);
  const configJson = buildConfigJson(config, markType);
  const hasValidConfig =
    hasValidValueExtractor(config, markType) &&
    (config.matchMode === PathMatchMode.Layer
      ? config.layer !== undefined
      : Boolean(config.regex && config.regex.length > 0));

  // Use rootPaths if provided, otherwise use single rootPath
  const effectivePaths = useMemo(() => {
    if (rootPaths && rootPaths.length > 0) {
      return rootPaths;
    }

    return rootPath ? [rootPath] : [];
  }, [rootPath, rootPaths]);

  useEffect(() => {
    if (effectivePaths.length === 0 || !hasValidConfig) {
      setLoading(false);
      setError(null);
      setResults([]);
      setResultsByPath([]);

      return;
    }

    // In-flight requests may finish after a config change, path change or unmount.
    let active = true;
    const controller = new AbortController();

    setLoading(true);
    setError(null);
    const timeout = setTimeout(async () => {
      try {
        const allResults: PathMarkPreviewResult[] = [];
        const allResultsByPath: PreviewResultsByPath[] = [];
        let previewError: string | null = null;

        // Fetch preview for each path
        await Promise.all(
          effectivePaths.map(async (path) => {
            // Use the preview API with PathMarkPreviewRequest
            const rsp = await BApi.pathMark.previewPathMarkMatchedPaths(
              {
                path: path,
                type: markType,
                configJson,
              },
              { signal: controller.signal, showErrorToast: false },
            );

            if (!rsp.code && rsp.data) {
              const pathResults = rsp.data.slice(0, 5);

              allResults.push(...pathResults);
              allResultsByPath.push({ path, results: pathResults });
            } else if (rsp.message) {
              previewError = rsp.message;
            }
          }),
        );

        if (!active) return;
        setError(previewError);
        setResults(allResults.slice(0, 10)); // Show max 10 combined results
        setResultsByPath(allResultsByPath);
      } catch (err) {
        if (!active) return;
        setError(err instanceof Error ? err.message : "pathMarkConfig.error.previewFailed");
        setResults([]);
        setResultsByPath([]);
      } finally {
        if (active) setLoading(false);
      }
    }, debounceMs);

    return () => {
      active = false;
      controller.abort();
      clearTimeout(timeout);
    };
  }, [effectivePaths, markType, configJson, hasValidConfig, debounceMs]);

  return {
    loading,
    results,
    resultsByPath,
    error,
    isMultiplePaths: effectivePaths.length > 1,
    applyScope: config.applyScope,
  };
};
