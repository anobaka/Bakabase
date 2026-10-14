"use client";

import type { FilterConfig, SearchFilter, SearchFilterGroup } from "../../models";
import type { FilterLayout } from "../Filter";
import type { FilterDisplayMode } from "@/sdk/constants";

import { useEffect, useMemo, useRef } from "react";

import FilterGroup from "../FilterGroup";
import { FilterProvider, useHasFilterContext } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";
import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";
import { useResourceSources } from "@/features/management/resourceSourcesContext";
import { GroupCombinator } from "../../models";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyPool, ResourceProperty, SearchOperation } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

export interface FilterGroupWithContextProps {
  /** The root filter group */
  group?: SearchFilterGroup;
  /** Called when the group changes */
  onChange: (group: SearchFilterGroup) => void;
  /** Filter display mode - Simple hides operation selector */
  filterDisplayMode?: FilterDisplayMode;
  /** Filter layout - horizontal or vertical */
  filterLayout?: FilterLayout;
  /** Index of filter that should auto-trigger property selector */
  externalNewFilterIndex?: number | null;
  /** Called when external new filter index is consumed */
  onExternalNewFilterConsumed?: () => void;
  /** Custom class name */
  className?: string;
  /**
   * Optional filter config. When provided, creates its own FilterProvider.
   * When used inside ResourceFilterController, this is not needed as the context is already provided.
   */
  config?: FilterConfig;
  /**
   * Whether to auto-create a media library filter when group is empty or has no content.
   * Defaults to false.
   */
  autoCreateMediaLibraryFilter?: boolean;
  /** Readonly mode - hides all action buttons and makes filters readonly */
  isReadonly?: boolean;
}

/**
 * Helper function to check if a group is effectively empty (no filters or sub-groups).
 */
const isGroupEmpty = (group?: SearchFilterGroup): boolean => {
  if (!group) return true;
  const hasFilters = group.filters && group.filters.length > 0;
  const hasGroups = group.groups && group.groups.length > 0;

  return !hasFilters && !hasGroups;
};

/**
 * Creates an internal property filter by fetching property info from API.
 */
const createInternalPropertyFilter = async (
  propertyId: ResourceProperty,
  operation: SearchOperation = SearchOperation.In,
  api: typeof BApi = BApi,
): Promise<SearchFilter | null> => {
  try {
    const pool = PropertyPool.Internal;

    const propertyResponse = await api.property.getPropertiesByPool(pool);
    const property = (propertyResponse.data || []).find((p) => p.id === propertyId);

    if (!property) return null;

    const valuePropertyResponse = await api.resource.getFilterValueProperty({
      propertyPool: pool,
      propertyId,
      operation,
    });
    const valueProperty = valuePropertyResponse.data;

    const operationsResponse = await api.resource.getSearchOperationsForProperty({
      propertyPool: pool,
      propertyId,
    });
    const availableOperations = operationsResponse.data ?? [];

    return {
      propertyId,
      operation,
      propertyPool: pool,
      property,
      valueProperty,
      availableOperations,
      disabled: false,
    };
  } catch {
    return null;
  }
};

/**
 * Inner component that renders the filter group content.
 */
const FilterGroupWithContextContent = ({
  group,
  onChange,
  filterDisplayMode,
  filterLayout,
  externalNewFilterIndex,
  onExternalNewFilterConsumed,
  className = "",
  autoCreateMediaLibraryFilter = false,
  isReadonly = false,
}: Omit<FilterGroupWithContextProps, "config">) => {
  const autoCreateTriggeredRef = useRef(false);
  const api = useResourceApi();
  const owner = useResourceOwner();
  const sources = useResourceSources()?.sources;

  // Auto-create default filters (MediaLibrary + Source) when group is empty
  useEffect(() => {
    if (!autoCreateMediaLibraryFilter) return;
    if (autoCreateTriggeredRef.current) return;
    if (!isGroupEmpty(group)) return;

    autoCreateTriggeredRef.current = true;

    Promise.all([
      createInternalPropertyFilter(ResourceProperty.Source, SearchOperation.In, api),
      createInternalPropertyFilter(ResourceProperty.MediaLibraryV2Multi, SearchOperation.In, api),
    ]).then((filters) => {
      const localOwner =
        owner ??
        (sources?.some((source) => !source.isLocal)
          ? sources.find((source) => source.isLocal)
          : undefined);
      const validFilters = filters
        .filter((f): f is SearchFilter => f !== null)
        .map((filter) => {
          if (!localOwner || (!owner && filter.propertyId !== ResourceProperty.MediaLibraryV2Multi))
            return filter;

          return {
            ...filter,
            owner: { serverId: localOwner.serverId, libraryEpoch: localOwner.libraryEpoch },
            property: filter.property ? { ...filter.property, owner: localOwner } : undefined,
            valueProperty: filter.valueProperty
              ? { ...filter.valueProperty, owner: localOwner }
              : undefined,
          };
        });

      if (validFilters.length > 0) {
        const newGroup: SearchFilterGroup = {
          combinator: GroupCombinator.And,
          disabled: false,
          filters: validFilters,
          groups: [],
        };

        onChange(newGroup);
      }
    });
  }, [autoCreateMediaLibraryFilter, group, onChange, api, owner, sources]);

  const hasFilters =
    group &&
    ((group.filters && group.filters.length > 0) || (group.groups && group.groups.length > 0));

  if (!hasFilters || !group) {
    return null;
  }

  return (
    <div className={className || undefined}>
      <FilterGroup
        isRoot
        externalNewFilterIndex={externalNewFilterIndex}
        filterDisplayMode={filterDisplayMode}
        filterLayout={filterLayout}
        group={group}
        isReadonly={isReadonly}
        onChange={onChange}
        onExternalNewFilterConsumed={onExternalNewFilterConsumed}
      />
    </div>
  );
};

/**
 * FilterGroup wrapper that ensures FilterProvider context exists.
 * Can be used standalone (with config prop) or inside ResourceFilterController.
 * When used standalone, it creates its own FilterProvider.
 */
const FilterGroupWithContext = ({ config, ...props }: FilterGroupWithContextProps) => {
  const hasContext = useHasFilterContext();
  const { createPortal } = useBakabaseContext();
  const api = useResourceApi();
  const owner = useResourceOwner();
  const sources = useResourceSources()?.sources;
  const defaultConfig = useMemo(
    () => createDefaultFilterConfig(createPortal, api, sources, owner),
    [createPortal, api, sources, owner],
  );

  // If already inside a FilterProvider, just render the content
  if (hasContext) {
    return <FilterGroupWithContextContent {...props} />;
  }

  // Otherwise, create our own provider
  const effectiveConfig = config ?? defaultConfig;

  return (
    <FilterProvider config={effectiveConfig}>
      <FilterGroupWithContextContent {...props} />
    </FilterProvider>
  );
};

FilterGroupWithContext.displayName = "FilterGroupWithContext";

export default FilterGroupWithContext;
