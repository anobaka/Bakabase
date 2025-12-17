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

// Components
export { default as Filter } from "./components/Filter";
export { default as FilterGroup } from "./components/FilterGroup";
export { default as FilterModal } from "./components/FilterModal";
export { default as RecentFilters } from "./components/RecentFilters";

// Presets
export { createDefaultFilterConfig } from "./presets/DefaultFilterPreset";
