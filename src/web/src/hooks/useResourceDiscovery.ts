import { useEffect, useState, useRef, useMemo } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { ResourceCacheType } from "@/sdk/constants";
import type { Resource } from "@/core/models/Resource";

export type DiscoveryState = {
  status: "loading" | "ready" | "error";
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
 * @returns DiscoveryState with status and discovered data
 */
export function useResourceDiscovery(
  resource: Resource,
  types: ResourceCacheType[]
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

    if (hasAllRequiredTypes) {
      return {
        status: "ready",
        coverPaths: resource.cache?.coverPaths,
        playableFilePaths: resource.cache?.playableFilePaths,
        hasMorePlayableFiles: resource.cache?.hasMorePlayableFiles,
      };
    }

    return { status: "loading" };
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey]);

  const [state, setState] = useState<DiscoveryState>(initialState);
  const mountedRef = useRef(true);
  const subscribedRef = useRef(false);

  // Reset state when resource changes
  useEffect(() => {
    const cachedTypes = resource.cache?.cachedTypes ?? [];
    const hasAllRequiredTypes = types.every((t) => cachedTypes.includes(t));

    if (hasAllRequiredTypes) {
      setState({
        status: "ready",
        coverPaths: resource.cache?.coverPaths,
        playableFilePaths: resource.cache?.playableFilePaths,
        hasMorePlayableFiles: resource.cache?.hasMorePlayableFiles,
      });
      subscribedRef.current = false;
    } else {
      setState({ status: "loading" });
      subscribedRef.current = false;
    }
  }, [resource.id, cachedTypesKey, coverPathsKey, playableFilePathsKey, hasMorePlayableFiles, typesKey]);

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
          setState({ status: "error", error });
        } else if (data) {
          setState({
            status: "ready",
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
          setState({ status: "error", error: String(err) });
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
export function useCoverDiscovery(resource: Resource): DiscoveryState {
  return useResourceDiscovery(resource, [ResourceCacheType.Covers]);
}

/**
 * Hook specifically for discovering playable files.
 * Convenience wrapper around useResourceDiscovery.
 */
export function usePlayableFilesDiscovery(resource: Resource): DiscoveryState {
  return useResourceDiscovery(resource, [ResourceCacheType.PlayableFiles]);
}

export default useResourceDiscovery;
