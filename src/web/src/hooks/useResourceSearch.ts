import { useState, useCallback, useRef } from "react";

import BApi from "@/sdk/BApi";
import { ResourceAdditionalItem } from "@/sdk/constants";

import type { Resource } from "@/core/models/Resource";
import type { components } from "@/sdk/BApi2";

type SearchForm = components["schemas"]["Bakabase.Service.Models.Input.ResourceSearchInputModel"];

type SearchOptions = {
  saveSearch?: boolean;
  searchId?: string;
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
  /** Execute a search with optional two-phase loading */
  search: (form: SearchForm, options?: SearchOptions) => Promise<Resource[]>;
  /** Manually set resources (useful for external updates) */
  setResources: React.Dispatch<React.SetStateAction<Resource[]>>;
};

/**
 * Hook for searching resources with two-phase loading optimization.
 *
 * Phase 1: Quick search with minimal data (additionalItems=None) for fast initial display
 * Phase 2: Background fetch of full details (additionalItems=All) for complete data
 *
 * @example
 * const { resources, loading, response, search } = useResourceSearch();
 *
 * useEffect(() => {
 *   search({ page: 1, pageSize: 20, group: { ... } });
 * }, []);
 */
export const useResourceSearch = (): UseResourceSearchResult => {
  const [resources, setResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadingDetails, setLoadingDetails] = useState(false);
  const [response, setResponse] = useState<SearchResponse>();

  // Use ref to track the latest search to avoid race conditions
  const searchIdRef = useRef(0);

  const search = useCallback(
    async (form: SearchForm, options?: SearchOptions): Promise<Resource[]> => {
      const currentSearchId = ++searchIdRef.current;

      setLoading(true);
      setLoadingDetails(false);

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

        const basicResources = (rsp.data || []) as Resource[];
        setResources(basicResources);
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
            return [];
          }

          if (!fullRsp.code) {
            const fullResources = (fullRsp.data || []) as Resource[];
            setResources(fullResources);
            setLoadingDetails(false);
            return fullResources;
          }

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

  return {
    resources,
    loading,
    loadingDetails,
    response,
    search,
    setResources,
  };
};

export default useResourceSearch;
