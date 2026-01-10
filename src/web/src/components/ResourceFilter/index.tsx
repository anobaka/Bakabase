"use client";

// Types
export type {
  SearchFilter,
  SearchFilterGroup,
  FilterConfig,
  FilterApiAdapter,
  FilterComponentRenderers,
  // Backward compatibility aliases
  ResourceSearchFilter,
  ResourceSearchFilterGroup,
} from "./models";

export { GroupCombinator } from "./models";

// Context
export { FilterProvider, useFilterConfig } from "./context/FilterContext";

// Hooks
export { useFilterCriteria } from "./hooks/useFilterCriteria";
export type {
  SearchCriteria,
  UseFilterCriteriaOptions,
  UseFilterCriteriaReturn,
} from "./hooks/useFilterCriteria";

// Components
export { default as Filter } from "./components/Filter";
export type { FilterLayout } from "./components/Filter";
export { default as FilterGroup } from "./components/FilterGroup";
export { default as FilterGroupWithContext } from "./components/FilterGroupWithContext";
export { default as FilterGroups } from "./components/FilterGroupWithContext"; // Backward compatibility alias
export type {
  FilterGroupWithContextProps,
  FilterGroupWithContextProps as FilterGroupsProps,
} from "./components/FilterGroupWithContext";
export { default as RecentFilters } from "./components/RecentFilters";
export { default as ResourceSearchPanel } from "./components/ResourceSearchPanel";
export type { KeywordSearchProps } from "./components/KeywordSearch";
export { default as FilterPortal, ResourceFilterPortal } from "./components/FilterPortal";
export type { FilterPortalProps, ResourceFilterPortalProps } from "./components/FilterPortal";
export { default as ResourceFilterController } from "./components/ResourceFilterController";
export type { ResourceFilterControllerProps } from "./components/ResourceFilterController";

// Presets
export { createDefaultFilterConfig } from "./presets/DefaultFilterPreset";

// Utils for API input models
export {
  toFilterInputModel,
  toFilterGroupInputModel,
  toSearchInputModel,
} from "./utils/toInputModel";
export type {
  SearchFilterInputModel,
  SearchFilterGroupInputModel,
  ResourceSearchInputModel,
} from "./utils/toInputModel";
