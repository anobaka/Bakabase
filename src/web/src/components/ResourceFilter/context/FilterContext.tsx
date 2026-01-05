"use client";

import type { FilterConfig } from "../models";

import { createContext, useContext, useMemo, type ReactNode } from "react";

interface FilterContextValue {
  config: FilterConfig;
}

const FilterContext = createContext<FilterContextValue | null>(null);

export interface FilterProviderProps {
  config: FilterConfig;
  children: ReactNode;
}

export function FilterProvider({ config, children }: FilterProviderProps) {
  const value = useMemo(() => ({ config }), [config]);

  return (
    <FilterContext.Provider value={value}>
      {children}
    </FilterContext.Provider>
  );
}

export function useFilterConfig(): FilterConfig {
  const context = useContext(FilterContext);
  if (!context) {
    throw new Error("useFilterConfig must be used within a FilterProvider");
  }
  return context.config;
}

/**
 * Returns true if currently inside a FilterProvider, false otherwise.
 * Useful for components that need to conditionally create their own provider.
 */
export function useHasFilterContext(): boolean {
  const context = useContext(FilterContext);
  return context !== null;
}
