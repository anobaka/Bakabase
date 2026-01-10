"use client";

import type { FilterConfig, SearchFilter, SearchFilterGroup } from "../../models";
import type { FilterLayout } from "../Filter";
import type { FilterDisplayMode } from "@/sdk/constants";

import { useEffect, useMemo, useRef } from "react";

import FilterGroup from "../FilterGroup";
import { FilterProvider, useHasFilterContext } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";
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
 * Creates a media library filter by fetching property info from API.
 */
const createMediaLibraryFilter = async (): Promise<SearchFilter | null> => {
  try {
    const propertyId = ResourceProperty.MediaLibraryV2Multi;
    const pool = PropertyPool.Internal;
    const operation = SearchOperation.In;

    // Fetch property definition
    const propertyResponse = await BApi.property.getPropertiesByPool(pool);
    const property = (propertyResponse.data || []).find(p => p.id === propertyId);
    if (!property) return null;

    // Fetch value property
    const valuePropertyResponse = await BApi.resource.getFilterValueProperty({
      propertyPool: pool,
      propertyId: propertyId,
      operation: operation,
    });
    const valueProperty = valuePropertyResponse.data;

    // Fetch available operations
    const operationsResponse = await BApi.resource.getSearchOperationsForProperty({
      propertyPool: pool,
      propertyId: propertyId,
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

  // Auto-create media library filter when group is empty
  useEffect(() => {
    if (!autoCreateMediaLibraryFilter) return;
    if (autoCreateTriggeredRef.current) return;
    if (!isGroupEmpty(group)) return;

    autoCreateTriggeredRef.current = true;

    createMediaLibraryFilter().then((filter) => {
      if (filter) {
        const newGroup: SearchFilterGroup = {
          combinator: GroupCombinator.And,
          disabled: false,
          filters: [filter],
          groups: [],
        };
        onChange(newGroup);
      }
    });
  }, [autoCreateMediaLibraryFilter, group, onChange]);

  const hasFilters =
    group &&
    ((group.filters && group.filters.length > 0) ||
      (group.groups && group.groups.length > 0));

  if (!hasFilters || !group) {
    return null;
  }

  return (
    <div className={className || undefined}>
      <FilterGroup
        isRoot
        group={group}
        filterDisplayMode={filterDisplayMode}
        filterLayout={filterLayout}
        isReadonly={isReadonly}
        externalNewFilterIndex={externalNewFilterIndex}
        onExternalNewFilterConsumed={onExternalNewFilterConsumed}
        onChange={onChange}
      />
    </div>
  );
};

/**
 * FilterGroup wrapper that ensures FilterProvider context exists.
 * Can be used standalone (with config prop) or inside ResourceFilterController.
 * When used standalone, it creates its own FilterProvider.
 */
const FilterGroupWithContext = ({
  config,
  ...props
}: FilterGroupWithContextProps) => {
  const hasContext = useHasFilterContext();
  const { createPortal } = useBakabaseContext();
  const defaultConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

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
