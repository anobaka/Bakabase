import { useEffect, useState, useRef, useMemo } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { ResourceCacheType } from "@/sdk/constants";
import type { Resource } from "@/core/models/Resource";

// Data source for discovery
export type DiscoverySource = "cache" | "realtime";

export type DiscoveryState = {
  status: "loading" | "ready" | "error";
  source: DiscoverySource;          // Data source: cache or realtime discovery
  cacheEnabled: boolean;            // Whether cache is enabled
  coverPaths?: string[];
  playableFilePaths?: string[];
  hasMorePlayableFiles?: boolean;
  error?: string;
};

/**
 * Hook to discover cover and playable files for a resource.
 * Uses SSE-based discovery channel for efficient server communication.
 *
 * @param resource - The resource to discover data for
 * @param types - Array of ResourceCacheType to discover (Covers, PlayableFiles)
 * @param cacheEnabled - Whether cache is enabled (when true, uses cached data if available)
 * @returns DiscoveryState with status and discovered data
 */
export function useResourceDiscovery(
  resource: Resource,
  types: ResourceCacheType[],
  cacheEnabled: boolean = true
): DiscoveryState {
  // Memoize types array to prevent unnecessary re-renders
  const typesKey = useMemo(() => types.sort().join(","), [types]);

  // Extract stable primitive values from cache to use as dependencies
  // This prevents re-renders when cache object reference changes but content is the same
  const cachedTypesKey = resource.cache?.cachedTypes?.sort().join(",") ?? "";
  const coverPathsKey = resource.cache?.coverPaths?.join(",") ?? "";
  const playableFilePathsKey = resource.cache?.playableFilePaths?.join(",") ?? "";
  const hasMorePlayableFiles = resource.cache?.hasMorePlayableFiles;

  // Check if we already have cached data
  const initialState = useMemo((): DiscoveryState => {
    const cachedTypes = resource.cache?.cachedTypes ?? [];
    const hasAllRequiredTypes = types.every((t) => cachedTypes.includes(t));

    // If cache is enabled and we have all required types, return cached data
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

    // Otherwise, we need to load via SSE (realtime discovery)
    // Even if cacheEnabled, we use realtime discovery when cache is not ready
    // This provides instant feedback instead of waiting for cache preparation
    return {
      status: "loading",
      source: "realtime",  // Always realtime when loading via SSE
      cacheEnabled,
    };
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey, cacheEnabled]);

  const [state, setState] = useState<DiscoveryState>(initialState);
  const mountedRef = useRef(true);
  const subscribedRef = useRef(false);

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
    } else {
      // Use realtime discovery when cache is not ready (even if cache is enabled)
      setState({
        status: "loading",
        source: "realtime",
        cacheEnabled,
      });
      subscribedRef.current = false;
    }
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey, cacheEnabled]);

  useEffect(() => {
    mountedRef.current = true;

    // If already ready from cache, no need to subscribe
    if (state.status === "ready") {
      return;
    }

    // Prevent duplicate subscriptions
    if (subscribedRef.current) {
      return;
    }
    subscribedRef.current = true;

    let unsubscribe: (() => void) | null = null;

    resourceDiscoveryChannel
      .subscribe(resource.id, types, (data: DiscoveryData | null, error?: string) => {
        if (!mountedRef.current) return;

        if (error) {
          setState({
            status: "error",
            source: "realtime",  // Data was loaded via realtime discovery
            cacheEnabled,
            error,
          });
        } else if (data) {
          setState({
            status: "ready",
            source: "realtime",  // Data was loaded via realtime discovery
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
          // Component unmounted before subscription completed
          unsub();
        }
      })
      .catch((err) => {
        if (mountedRef.current) {
          setState({
            status: "error",
            source: "realtime",  // Data was loaded via realtime discovery
            cacheEnabled,
            error: String(err),
          });
        }
      });

    return () => {
      mountedRef.current = false;
      subscribedRef.current = false;
      unsubscribe?.();
    };
  }, [resource.id, typesKey, state.status]);

  return state;
}

/**
 * Hook specifically for discovering covers.
 * Convenience wrapper around useResourceDiscovery.
 */
export function useCoverDiscovery(resource: Resource, cacheEnabled: boolean = true): DiscoveryState {
  return useResourceDiscovery(resource, [ResourceCacheType.Covers], cacheEnabled);
}

/**
 * Hook specifically for discovering playable files.
 * Convenience wrapper around useResourceDiscovery.
 */
export function usePlayableFilesDiscovery(resource: Resource, cacheEnabled: boolean = true): DiscoveryState {
  return useResourceDiscovery(resource, [ResourceCacheType.PlayableFiles], cacheEnabled);
}

export default useResourceDiscovery;
