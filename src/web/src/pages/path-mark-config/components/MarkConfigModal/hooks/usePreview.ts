import { useState, useEffect, useRef, useMemo } from "react";
import type { MarkConfig } from "../types";
import { buildConfigJson } from "../utils";
import { PathMarkType, PathMatchMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

export type PathMarkPreviewResult = {
  path: string;
  resourceLayerIndex?: number | null;
  resourceSegmentName?: string | null;
  propertyValue?: string | null;
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
  rootPaths?: string[]
) => {
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState<PathMarkPreviewResult[]>([]);
  const [resultsByPath, setResultsByPath] = useState<PreviewResultsByPath[]>([]);
  const [error, setError] = useState<string | null>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>();

  // Use rootPaths if provided, otherwise use single rootPath
  const effectivePaths = useMemo(() => {
    if (rootPaths && rootPaths.length > 0) {
      return rootPaths;
    }
    return rootPath ? [rootPath] : [];
  }, [rootPath, rootPaths]);

  useEffect(() => {
    if (effectivePaths.length === 0) {
      setResults([]);
      setResultsByPath([]);
      return;
    }

    // Only preview if we have valid config
    const hasValidConfig =
      config.matchMode === PathMatchMode.Layer
        ? config.layer !== undefined
        : config.regex && config.regex.length > 0;

    if (!hasValidConfig) {
      setResults([]);
      setResultsByPath([]);
      return;
    }

    // Clear previous timeout
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }

    setLoading(true);
    setError(null);
    timeoutRef.current = setTimeout(async () => {
      try {
        const allResults: PathMarkPreviewResult[] = [];
        const allResultsByPath: PreviewResultsByPath[] = [];

        // Fetch preview for each path
        await Promise.all(
          effectivePaths.map(async (path) => {
            // Use the preview API with PathMarkPreviewRequest
            const rsp = await BApi.pathMark.previewPathMarkMatchedPaths({
              path: path,
              type: markType,
              configJson: buildConfigJson(config, markType),
            });

            if (!rsp.code && rsp.data) {
              const pathResults = rsp.data.slice(0, 5);
              allResults.push(...pathResults);
              allResultsByPath.push({ path, results: pathResults });
            } else if (rsp.message) {
              setError(rsp.message);
            }
          })
        );

        setResults(allResults.slice(0, 10)); // Show max 10 combined results
        setResultsByPath(allResultsByPath);
      } catch (err) {
        console.error("Preview failed:", err);
        setError("Preview failed");
        setResults([]);
        setResultsByPath([]);
      } finally {
        setLoading(false);
      }
    }, debounceMs);

    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, [
    effectivePaths,
    markType,
    config.matchMode,
    config.layer,
    config.regex,
    config.fsTypeFilter,
    config.extensions,
    config.extensionGroupIds,
    config.applyScope,
    debounceMs,
  ]);

  return { loading, results, resultsByPath, error, isMultiplePaths: effectivePaths.length > 1, applyScope: config.applyScope };
};
