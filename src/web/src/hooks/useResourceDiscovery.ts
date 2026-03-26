import { useEffect, useState, useRef, useMemo, useCallback } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { ResourceCacheType } from "@/sdk/constants";
import type { PlayableItem, Resource } from "@/core/models/Resource";

// Data source for discovery
export type DiscoverySource = "cache" | "realtime";

export type DiscoveryState = {
  status: "idle" | "loading" | "ready" | "error";
  source: DiscoverySource;          // Data source: cache or realtime discovery
  cacheEnabled: boolean;            // Whether cache is enabled
  coverPaths?: string[];
  playableFilePaths?: string[];
  playableItems?: PlayableItem[];
  hasMoreFileSystemPlayableItems?: boolean;
  error?: string;
};

export type DiscoveryResult = DiscoveryState & {
  /** Manually trigger discovery (transitions from "idle" to "loading") */
  trigger: () => void;
};

/**
 * Hook to discover cover and playable files for a resource.
 * Uses SSE-based discovery channel for efficient server communication.
 *
 * @param resource - The resource to discover data for
 * @param types - Array of ResourceCacheType to discover (Covers, PlayableFiles)
 * @param cacheEnabled - Whether cache is enabled (when true, uses cached data if available)
 * @param autoDiscover - When false, waits for manual trigger() call before subscribing to SSE
 * @returns DiscoveryResult with status, discovered data, and trigger function
 */
export function useResourceDiscovery(
  resource: Resource,
  types: ResourceCacheType[],
  cacheEnabled: boolean = true,
  autoDiscover: boolean = true
): DiscoveryResult {
  // Memoize types array to prevent unnecessary re-renders
  const typesKey = useMemo(() => types.sort().join(","), [types]);

  // Extract stable primitive values from cache to use as dependencies
  // This prevents re-renders when cache object reference changes but content is the same
  const cachedTypesKey = resource.cache?.cachedTypes?.sort().join(",") ?? "";
  const coverPathsKey = resource.cache?.coverPaths?.join(",") ?? "";
  const playableFilePathsKey = resource.cache?.playableFilePaths?.join(",") ?? "";
  const playableItemsKey = resource.cache?.playableItems?.map(i => `${i.source}:${i.key}`).join(",") ?? "";
  const hasMoreFileSystemPlayableItems = resource.cache?.hasMoreFileSystemPlayableItems;

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
        playableItems: resource.cache?.playableItems,
        hasMoreFileSystemPlayableItems: resource.cache?.hasMoreFileSystemPlayableItems,
      };
    }

    // If not auto-discovering, stay idle until trigger() is called
    if (!autoDiscover) {
      return {
        status: "idle",
        source: "realtime",
        cacheEnabled,
      };
    }

    // Otherwise, we need to load via SSE (realtime discovery)
    return {
      status: "loading",
      source: "realtime",
      cacheEnabled,
    };
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, playableItemsKey, hasMoreFileSystemPlayableItems, typesKey, cacheEnabled, autoDiscover]);

  const [state, setState] = useState<DiscoveryState>(initialState);
  const mountedRef = useRef(true);
  const subscribedRef = useRef(false);

  /** Manually trigger discovery - transitions from "idle" to "loading" */
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
        playableItems: resource.cache?.playableItems,
        hasMoreFileSystemPlayableItems: resource.cache?.hasMoreFileSystemPlayableItems,
      });
      subscribedRef.current = false;
    } else if (!autoDiscover) {
      setState({
        status: "idle",
        source: "realtime",
        cacheEnabled,
      });
      subscribedRef.current = false;
    } else {
      setState({
        status: "loading",
        source: "realtime",
        cacheEnabled,
      });
      subscribedRef.current = false;
    }
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, playableItemsKey, hasMoreFileSystemPlayableItems, typesKey, cacheEnabled, autoDiscover]);

  useEffect(() => {
    mountedRef.current = true;

    // If already ready from cache or idle (waiting for trigger), no need to subscribe
    if (state.status === "ready" || state.status === "idle") {
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
            source: "realtime",
            cacheEnabled,
            error,
          });
        } else if (data) {
          setState({
            status: "ready",
            source: "realtime",
            cacheEnabled,
            coverPaths: data.coverPaths,
            playableFilePaths: data.playableFilePaths,
            playableItems: data.playableItems,
            hasMoreFileSystemPlayableItems: data.hasMoreFileSystemPlayableItems,
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
            source: "realtime",
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

  return { ...state, trigger };
}

/**
 * Hook specifically for discovering covers.
 * When autoDiscover is false, stays idle until trigger() is called.
 */
export function useCoverDiscovery(resource: Resource, cacheEnabled: boolean = true, autoDiscover: boolean = true): DiscoveryResult {
  return useResourceDiscovery(resource, [ResourceCacheType.Covers], cacheEnabled, autoDiscover);
}

/**
 * Hook specifically for discovering playable files.
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
