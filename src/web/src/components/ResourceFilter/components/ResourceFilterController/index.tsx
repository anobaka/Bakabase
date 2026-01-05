"use client";

import type { ReactNode } from "react";
import type { SearchFilter, SearchFilterGroup } from "../../models";
import type { FilterLayout } from "../Filter";
import type { ResourceTag } from "@/sdk/constants";

import { useMemo, useState, useCallback } from "react";
import { createPortal } from "react-dom";

import FilterPortal from "../FilterPortal";
import FilterGroupWithContext from "../FilterGroupWithContext";
import { FilterProvider } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";
import { GroupCombinator } from "../../models";
import ResourceKeywordAutocomplete from "@/components/ResourceKeywordAutocomplete";

import { FilterDisplayMode } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export interface ResourceFilterControllerProps {
  /** Current keyword value */
  keyword?: string;
  /** Called when keyword changes */
  onKeywordChange?: (keyword: string | undefined) => void;
  /** Called when Enter is pressed in keyword input */
  onSearch?: () => void;

  /** Current filter group */
  group?: SearchFilterGroup;
  /** Called when filter group changes */
  onGroupChange: (group: SearchFilterGroup) => void;

  /** Current selected tags */
  tags?: ResourceTag[];
  /** Called when tags change */
  onTagsChange?: (tags: ResourceTag[]) => void;

  /** Current filter display mode */
  filterDisplayMode?: FilterDisplayMode;
  /** Called when filter display mode changes */
  onFilterDisplayModeChange?: (mode: FilterDisplayMode) => void;

  /** Filter layout - horizontal or vertical */
  filterLayout?: FilterLayout;

  /** Index of filter that should auto-trigger property selector */
  externalNewFilterIndex?: number | null;
  /** Called when external new filter index is consumed */
  onExternalNewFilterConsumed?: () => void;

  /** Whether to show recent filters in filter portal */
  showRecentFilters?: boolean;
  /** Whether to show tags selection in filter portal */
  showTags?: boolean;

  /**
   * Container element for keyword search.
   * If provided, KeywordSearch will be rendered into this container via portal.
   * If not provided, KeywordSearch will be rendered inline.
   */
  keywordContainer?: HTMLElement | null;

  /**
   * Container element for filter portal (add filter button).
   * If provided, FilterPortal will be rendered into this container via portal.
   * If not provided, FilterPortal will be rendered inline.
   */
  filterPortalContainer?: HTMLElement | null;

  /**
   * Container element for filter groups.
   * If provided, FilterGroupWithContext will be rendered into this container via portal.
   * If not provided, FilterGroupWithContext will be rendered inline.
   */
  filterGroupsContainer?: HTMLElement | null;

  /** Custom class name for keyword search */
  keywordClassName?: string;
  /** Custom class name for filter groups */
  filterGroupsClassName?: string;

  /** Placeholder for keyword search */
  keywordPlaceholder?: string;

  /**
   * Whether to auto-create a media library filter when group is empty or has no content.
   * Defaults to false.
   */
  autoCreateMediaLibraryFilter?: boolean;

  /** Readonly mode - hides all action buttons and makes filters readonly */
  isReadonly?: boolean;
}

/**
 * Unified resource filter controller component.
 * Manages filter state and allows rendering different parts to different containers.
 *
 * This component combines:
 * - KeywordSearch: for text search
 * - FilterPortal: for adding filters (with mode switching)
 * - FilterGroupWithContext: for displaying filter groups
 *
 * Each part can be rendered to a different container via the *Container props,
 * allowing flexible layouts.
 */
const ResourceFilterControllerInner = ({
  keyword: externalKeyword,
  onKeywordChange: externalOnKeywordChange,
  onSearch,
  group,
  onGroupChange,
  tags,
  onTagsChange,
  filterDisplayMode = FilterDisplayMode.Simple,
  onFilterDisplayModeChange,
  filterLayout = "vertical",
  externalNewFilterIndex,
  onExternalNewFilterConsumed,
  showRecentFilters = true,
  showTags = true,
  keywordContainer,
  filterPortalContainer,
  filterGroupsContainer,
  keywordClassName = "",
  filterGroupsClassName = "",
  keywordPlaceholder,
  autoCreateMediaLibraryFilter = false,
  isReadonly = false,
}: ResourceFilterControllerProps) => {
  const [internalNewFilterIndex, setInternalNewFilterIndex] = useState<number | null>(null);

  // Internal keyword state for uncontrolled mode
  const [internalKeyword, setInternalKeyword] = useState<string | undefined>(undefined);

  // Use external keyword if provided, otherwise use internal state
  const keyword = externalKeyword !== undefined ? externalKeyword : internalKeyword;
  const onKeywordChange = externalOnKeywordChange ?? setInternalKeyword;

  // Combine internal and external new filter index
  const newFilterIndex = internalNewFilterIndex ?? externalNewFilterIndex ?? null;

  const addFilter = useCallback((filter: SearchFilter, autoTriggerPropertySelector = false) => {
    const newGroup: SearchFilterGroup = group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.filters) {
      newGroup.filters = [];
    }

    // Track the index if we need to auto-trigger property selector
    if (autoTriggerPropertySelector) {
      setInternalNewFilterIndex(newGroup.filters.length);
    }

    newGroup.filters.push(filter);
    onGroupChange({ ...newGroup });
  }, [group, onGroupChange]);

  const addFilters = useCallback((filters: SearchFilter[]) => {
    const newGroup: SearchFilterGroup = group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.filters) {
      newGroup.filters = [];
    }

    newGroup.filters.push(...filters);
    onGroupChange({ ...newGroup });
  }, [group, onGroupChange]);

  const addFilterGroup = useCallback(() => {
    const newSubGroup: SearchFilterGroup = {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    const newGroup: SearchFilterGroup = group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.groups) {
      newGroup.groups = [];
    }
    newGroup.groups.push(newSubGroup);

    onGroupChange({ ...newGroup });
  }, [group, onGroupChange]);

  const handleModeChange = useCallback((mode: FilterDisplayMode) => {
    if (onFilterDisplayModeChange) {
      // When switching from Advanced to Simple, clean up
      if (mode === FilterDisplayMode.Simple && filterDisplayMode === FilterDisplayMode.Advanced) {
        const currentGroup = group;
        if (currentGroup) {
          const cleanedFilters = (currentGroup.filters || [])
            .filter(f => !f.disabled)
            .map(f => ({ ...f, disabled: false }));

          onGroupChange({
            ...currentGroup,
            filters: cleanedFilters,
            groups: [], // Remove all sub-groups in Simple mode
            combinator: GroupCombinator.And,
            disabled: false,
          });
        }
      }
      onFilterDisplayModeChange(mode);
    }
  }, [onFilterDisplayModeChange, filterDisplayMode, group, onGroupChange]);

  // Render keyword search with autocomplete
  const keywordElement = (
    <ResourceKeywordAutocomplete
      isClearable
      value={keyword}
      onValueChange={onKeywordChange}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          onSearch?.();
        }
      }}
      placeholder={keywordPlaceholder}
      className={`${keywordClassName} ${!keywordContainer && !filterPortalContainer ? "flex-1 min-w-0" : ""}`}
    />
  );

  // Render filter portal
  const filterPortalElement = (
    <FilterPortal
      mode={filterDisplayMode}
      onModeChange={handleModeChange}
      onAddFilter={(autoTrigger) => addFilter({ disabled: false }, autoTrigger)}
      onAddFilterGroup={addFilterGroup}
      onSelectFilters={addFilters}
      showRecentFilters={showRecentFilters}
      showTags={showTags}
      selectedTags={tags}
      onTagsChange={onTagsChange}
      currentFilters={group?.filters}
    />
  );

  // Render filter groups
  const filterGroupsElement = (
    <FilterGroupWithContext
      group={group}
      onChange={onGroupChange}
      filterDisplayMode={filterDisplayMode}
      filterLayout={filterLayout}
      isReadonly={isReadonly}
      externalNewFilterIndex={newFilterIndex}
      onExternalNewFilterConsumed={() => {
        setInternalNewFilterIndex(null);
        onExternalNewFilterConsumed?.();
      }}
      className={filterGroupsClassName}
      autoCreateMediaLibraryFilter={autoCreateMediaLibraryFilter}
    />
  );

  // Helper to render element to container or inline
  const renderToContainer = (element: ReactNode, container: HTMLElement | null | undefined) => {
    if (container) {
      return createPortal(element, container);
    }
    return element;
  };

  // Determine if keyword and filter portal should be rendered together on same line
  const shouldRenderKeywordAndFilterPortalTogether = !keywordContainer && !filterPortalContainer;

  // Check if there's content in the filter groups (for gap spacing)
  const hasFilterGroupContent = group && ((group.filters && group.filters.length > 0) || (group.groups && group.groups.length > 0));

  return (
    <>
      {!isReadonly && (
        shouldRenderKeywordAndFilterPortalTogether ? (
          <div className={`flex items-center gap-2 ${hasFilterGroupContent && !filterGroupsContainer ? "mb-2" : ""}`}>
            {keywordElement}
            {filterPortalElement}
          </div>
        ) : (
          <>
            {renderToContainer(keywordElement, keywordContainer)}
            {renderToContainer(filterPortalElement, filterPortalContainer)}
          </>
        )
      )}
      {renderToContainer(filterGroupsElement, filterGroupsContainer)}
    </>
  );
};

/**
 * ResourceFilterController with FilterProvider wrapper.
 */
const ResourceFilterController = (props: ResourceFilterControllerProps) => {
  const { createPortal } = useBakabaseContext();
  const filterConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

  return (
    <FilterProvider config={filterConfig}>
      <ResourceFilterControllerInner {...props} />
    </FilterProvider>
  );
};

ResourceFilterController.displayName = "ResourceFilterController";

export default ResourceFilterController;
