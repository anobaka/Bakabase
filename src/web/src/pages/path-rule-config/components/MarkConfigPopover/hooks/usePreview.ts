import { useState, useEffect, useRef } from "react";
import type { MarkConfig } from "../types";
import { buildConfigJson } from "../utils";
import { PathMarkType, PathMatchMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

export const usePreview = (
  rootPath: string | undefined,
  markType: PathMarkType,
  config: MarkConfig,
  debounceMs: number = 500
) => {
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>();

  useEffect(() => {
    if (!rootPath) {
      setResults([]);
      return;
    }

    // Only preview if we have valid config
    const hasValidConfig =
      config.matchMode === PathMatchMode.Layer
        ? config.layer !== undefined
        : config.regex && config.regex.length > 0;

    if (!hasValidConfig) {
      setResults([]);
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
        const rsp = await BApi.pathRule.previewPathRuleMatchedPaths({
          id: 0,
          path: rootPath,
          marks: [
            {
              type: markType,
              priority: 10,
              configJson: buildConfigJson(config, markType),
            },
          ],
          createDt: new Date().toISOString(),
          updateDt: new Date().toISOString(),
        });

        if (!rsp.code && rsp.data) {
          setResults(rsp.data.slice(0, 5)); // Show max 5 results
        } else {
          setResults([]);
          if (rsp.message) {
            setError(rsp.message);
          }
        }
      } catch (err) {
        console.error("Preview failed:", err);
        setError("Preview failed");
        setResults([]);
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
    rootPath,
    markType,
    config.matchMode,
    config.layer,
    config.regex,
    config.fsTypeFilter,
    config.extensions,
    config.extensionGroupIds,
    debounceMs,
  ]);

  return { loading, results, error };
};
