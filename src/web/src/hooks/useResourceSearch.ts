import { useState, useCallback, useRef } from "react";

import BApi from "@/sdk/BApi";
import { ResourceAdditionalItem } from "@/sdk/constants";

import type { Resource } from "@/core/models/Resource";
import type { components } from "@/sdk/BApi2";

type SearchForm = components["schemas"]["Bakabase.Service.Models.Input.ResourceSearchInputModel"];

type SearchOptions = {
  saveSearch?: boolean;
  searchId?: string;
  /** How to combine new resources with existing ones */
  mode?: "replace" | "append" | "prepend";
  /** Optional filter to apply to search results */
  filter?: (resources: Resource[]) => Resource[];
};

type SearchResponse = {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
};

type UseResourceSearchResult = {
  /** Current resources list */
  resources: Resource[];
  /** Whether the initial (basic) search is in progress */
  loading: boolean;
  /** Whether the full details are being loaded in background */
  loadingDetails: boolean;
  /** Search response metadata (pageIndex, pageSize, totalCount) */
  response: SearchResponse | undefined;
  /** Execute a search with two-phase loading optimization */
  search: (form: SearchForm, options?: SearchOptions) => Promise<Resource[]>;
  /** Manually set resources (useful for external updates) */
  setResources: React.Dispatch<React.SetStateAction<Resource[]>>;
  /** Reload specific resources by their IDs */
  reloadResources: (ids: number[]) => Promise<void>;
};

/**
 * Hook for searching resources with two-phase loading optimization.
 *
 * Phase 1: Quick search with Cache data for fast initial display
 * Phase 2: Background fetch of full details (additionalItems=All) for complete data
 *
 * @example
 * const { resources, loading, loadingDetails, search, reloadResources } = useResourceSearch();
 *
 * // Basic usage
 * useEffect(() => {
 *   search({ page: 1, pageSize: 20, group: { ... } });
 * }, []);
 *
 * // Append mode for infinite scroll
 * const loadMore = () => {
 *   search({ page: currentPage + 1, pageSize: 20 }, { mode: 'append' });
 * };
 *
 * // With filter
 * search({ page: 1, pageSize: 20 }, {
 *   filter: (resources) => resources.filter(r => r.playedAt)
 * });
 */
export const useResourceSearch = (): UseResourceSearchResult => {
  const [resources, setResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadingDetails, setLoadingDetails] = useState(false);
  const [response, setResponse] = useState<SearchResponse>();

  // Use ref to track the latest search to avoid race conditions
  const searchIdRef = useRef(0);
  // Keep a ref to resources for use in callbacks
  const resourcesRef = useRef(resources);
  resourcesRef.current = resources;

  const search = useCallback(
    async (form: SearchForm, options?: SearchOptions): Promise<Resource[]> => {
      const currentSearchId = ++searchIdRef.current;
      const mode = options?.mode ?? "replace";
      const filter = options?.filter;

      setLoading(true);
      setLoadingDetails(false);

      if (mode === "replace") {
        setResources([]);
      }

      try {
        // Step 1: Quick search with Cache for fast initial response
        // Cache allows ResourceCover and PlayableFiles to use cached data immediately
        const rsp = await BApi.resource.searchResources(form, {
          additionalItems: ResourceAdditionalItem.Cache,
          saveSearch: options?.saveSearch,
          searchId: options?.searchId,
        });

        // Check if this search is still the latest one
        if (currentSearchId !== searchIdRef.current) {
          return [];
        }

        let basicResources = (rsp.data || []) as Resource[];

        console.log('[useResourceSearch] Phase 1 (Cache) loaded', basicResources.length, 'resources. Sample properties:', basicResources.slice(0, 3).map(r => ({ id: r.id, hasProperties: !!r.properties, propertyKeys: r.properties ? Object.keys(r.properties) : [], mediaLibraries: r.mediaLibraries, category: r.category?.name })));

        // Apply filter if provided
        if (filter) {
          basicResources = filter(basicResources);
        }

        // Update resources based on mode
        setResources((prev) => {
          switch (mode) {
            case "append":
              return [...prev, ...basicResources];
            case "prepend":
              return [...basicResources, ...prev];
            default:
              return basicResources;
          }
        });

        setResponse({
          pageIndex: rsp.pageIndex!,
          pageSize: rsp.pageSize!,
          totalCount: rsp.totalCount!,
        });
        setLoading(false);

        // Step 2: Fetch full details in background
        if (basicResources.length > 0) {
          setLoadingDetails(true);

          const ids = basicResources.map((r) => r.id);
          const fullRsp = await BApi.resource.getResourcesByKeys({
            ids,
            additionalItems: ResourceAdditionalItem.All,
          });

          // Check again if this search is still the latest one
          if (currentSearchId !== searchIdRef.current) {
            console.log('[useResourceSearch] Phase 2 aborted: searchId mismatch', currentSearchId, '!==', searchIdRef.current);
            return [];
          }

          if (!fullRsp.code) {
            let fullResources = (fullRsp.data || []) as Resource[];

            console.log('[useResourceSearch] Phase 2 (All) loaded', fullResources.length, 'resources. Sample properties:', fullResources.slice(0, 3).map(r => ({ id: r.id, hasProperties: !!r.properties, propertyPools: r.properties ? Object.keys(r.properties).map(pool => ({ pool, propIds: Object.keys(r.properties![pool as any] || {}) })) : [], mediaLibraries: r.mediaLibraries, category: r.category?.name })));

            // Apply filter to full resources as well
            if (filter) {
              fullResources = filter(fullResources);
            }

            // For append/prepend mode, we need to replace only the newly added resources
            const fullResourcesMap = new Map(fullResources.map((r) => [r.id, r]));

            setResources((prev) => {
              const next = prev.map((r) => fullResourcesMap.get(r.id) ?? r);
              console.log('[useResourceSearch] Phase 2 setResources: prev refs === next refs?', prev.map((r, i) => r === next[i]));
              return next;
            });
            setLoadingDetails(false);
            return fullResources;
          }

          console.log('[useResourceSearch] Phase 2 API error, code:', fullRsp.code, 'message:', fullRsp.message);
          setLoadingDetails(false);
        }

        return basicResources;
      } catch (error) {
        if (currentSearchId === searchIdRef.current) {
          setLoading(false);
          setLoadingDetails(false);
        }
        throw error;
      }
    },
    [],
  );

  const reloadResources = useCallback(async (ids: number[]) => {
    const rsp = await BApi.resource.getResourcesByKeys({
      ids,
      additionalItems: ResourceAdditionalItem.All,
    });

    if (!rsp.code && rsp.data) {
      const updatedResources = rsp.data as Resource[];
      const updatedMap = new Map(updatedResources.map((r) => [r.id, r]));

      setResources((prev) =>
        prev.map((r) => updatedMap.get(r.id) ?? r),
      );
    }
  }, []);

  return {
    resources,
    loading,
    loadingDetails,
    response,
    search,
    setResources,
    reloadResources,
  };
};

/**
 * Standalone progressive search function for cases where you manage state externally.
 * Useful for simple one-off searches without needing the full hook.
 *
 * @example
 * const [resources, setResources] = useState<Resource[]>([]);
 * progressiveSearch(
 *   { page: 1, pageSize: 20 },
 *   setResources,
 *   (r) => r.filter(res => res.playedAt)
 * );
 */
export const progressiveSearch = async (
  searchParams: SearchForm,
  setState: React.Dispatch<React.SetStateAction<Resource[]>>,
  filter?: (resources: Resource[]) => Resource[],
): Promise<Resource[]> => {
  // Step 1: Quick search with Cache for fast initial response
  const quickRes = await BApi.resource.searchResources(searchParams, {
    additionalItems: ResourceAdditionalItem.Cache,
  });
  let basicResources = (quickRes.data ?? []) as Resource[];

  // Apply filter if provided
  if (filter) {
    basicResources = filter(basicResources);
  }

  // Set basic resources immediately for quick display
  setState(basicResources);

  // Step 2: Fetch full details for the resources
  if (basicResources.length > 0) {
    const ids = basicResources.map((r) => r.id);
    const fullRes = await BApi.resource.getResourcesByKeys({
      ids,
      additionalItems: ResourceAdditionalItem.All,
    });
    let fullResources = (fullRes.data ?? []) as Resource[];

    // Apply filter again if provided
    if (filter) {
      fullResources = filter(fullResources);
    }

    setState(fullResources);
    return fullResources;
  }

  return basicResources;
};

export default useResourceSearch;
