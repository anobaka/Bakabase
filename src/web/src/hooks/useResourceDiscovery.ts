import { useEffect, useState, useRef, useMemo, useCallback } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { ResourceCacheType } from "@/sdk/constants";
import type { Resource } from "@/core/models/Resource";

// Data source for discovery
export type DiscoverySource = "cache" | "realtime";

export type DiscoveryState = {
  status: "idle" | "loading" | "ready" | "error";
  source: DiscoverySource;
  cacheEnabled: boolean;
  coverPaths?: string[];
  playableFilePaths?: string[];
  hasMorePlayableFiles?: boolean;
  error?: string;
};

export type DiscoveryResult = DiscoveryState & {
  /** Manually trigger discovery (transitions from "idle" to "loading") */
  trigger: () => void;
};

/**
 * Hook to discover filesystem-level cover and playable files for a resource.
 * Uses SSE-based discovery channel for efficient server communication.
 * Only discovers filesystem data - external source data is resolved by backend at query time.
 */
export function useResourceDiscovery(
  resource: Resource,
  types: ResourceCacheType[],
  cacheEnabled: boolean = true,
  autoDiscover: boolean = true
): DiscoveryResult {
  const typesKey = useMemo(() => types.sort().join(","), [types]);

  // Extract stable primitive values from cache
  const cachedTypesKey = resource.cache?.cachedTypes?.sort().join(",") ?? "";
  const coverPathsKey = resource.cache?.coverPaths?.join(",") ?? "";
  const playableFilePathsKey = resource.cache?.playableFilePaths?.join(",") ?? "";
  const hasMorePlayableFiles = resource.cache?.hasMorePlayableFiles;

  const initialState = useMemo((): DiscoveryState => {
    const cachedTypes = resource.cache?.cachedTypes ?? [];
    const hasAllRequiredTypes = types.every((t) => cachedTypes.includes(t));

    if (cacheEnabled && hasAllRequiredTypes) {
      return {
        status: "ready",
        source: "cache",
        cacheEnabled,
        coverPaths: resource.cache?.coverPaths,
        playableFilePaths: resource.cache?.playableFilePaths,
        hasMorePlayableFiles: resource.cache?.hasMorePlayableFiles,
      };
    }

    if (!autoDiscover) {
      return { status: "idle", source: "realtime", cacheEnabled };
    }

    return { status: "loading", source: "realtime", cacheEnabled };
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey, cacheEnabled, autoDiscover]);

  const [state, setState] = useState<DiscoveryState>(initialState);
  const mountedRef = useRef(true);
  const subscribedRef = useRef(false);

  const trigger = useCallback(() => {
    setState(prev => prev.status === "idle" ? { ...prev, status: "loading" } : prev);
  }, []);

  // Reset state when resource changes
  useEffect(() => {
    const cachedTypes = resource.cache?.cachedTypes ?? [];
    const hasAllRequiredTypes = types.every((t) => cachedTypes.includes(t));

    if (cacheEnabled && hasAllRequiredTypes) {
      setState({
        status: "ready",
        source: "cache",
        cacheEnabled,
        coverPaths: resource.cache?.coverPaths,
        playableFilePaths: resource.cache?.playableFilePaths,
        hasMorePlayableFiles: resource.cache?.hasMorePlayableFiles,
      });
      subscribedRef.current = false;
    } else if (!autoDiscover) {
      setState({ status: "idle", source: "realtime", cacheEnabled });
      subscribedRef.current = false;
    } else {
      setState({ status: "loading", source: "realtime", cacheEnabled });
      subscribedRef.current = false;
    }
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey, cacheEnabled, autoDiscover]);

  useEffect(() => {
    mountedRef.current = true;

    if (state.status === "ready" || state.status === "idle") {
      return;
    }

    if (subscribedRef.current) {
      return;
    }
    subscribedRef.current = true;

    let unsubscribe: (() => void) | null = null;

    resourceDiscoveryChannel
      .subscribe(resource.id, types, (data: DiscoveryData | null, error?: string) => {
        if (!mountedRef.current) return;

        if (error) {
          setState({ status: "error", source: "realtime", cacheEnabled, error });
        } else if (data) {
          setState({
            status: "ready",
            source: "realtime",
            cacheEnabled,
            coverPaths: data.coverPaths,
            playableFilePaths: data.playableFilePaths,
            hasMorePlayableFiles: data.hasMorePlayableFiles,
          });
        }
      })
      .then((unsub) => {
        if (mountedRef.current) {
          unsubscribe = unsub;
        } else {
          unsub();
        }
      })
      .catch((err) => {
        if (mountedRef.current) {
          setState({ status: "error", source: "realtime", cacheEnabled, error: String(err) });
        }
      });

    return () => {
      mountedRef.current = false;
      subscribedRef.current = false;
      unsubscribe?.();
    };
  }, [resource.id, typesKey, state.status]);

  return { ...state, trigger };
}

/**
 * Hook specifically for discovering covers from filesystem.
 * When autoDiscover is false, stays idle until trigger() is called.
 */
export function useCoverDiscovery(resource: Resource, cacheEnabled: boolean = true, autoDiscover: boolean = true): DiscoveryResult {
  return useResourceDiscovery(resource, [ResourceCacheType.Covers], cacheEnabled, autoDiscover);
}

/**
 * Hook specifically for discovering playable files from filesystem.
 * Supports deferred discovery via autoDiscover parameter.
 */
export function usePlayableFilesDiscovery(
  resource: Resource,
  cacheEnabled: boolean = true,
  autoDiscover: boolean = true
): DiscoveryResult {
  return useResourceDiscovery(resource, [ResourceCacheType.PlayableFiles], cacheEnabled, autoDiscover);
}

export default useResourceDiscovery;
