import { createContext, useContext, type ReactNode } from "react";
import type { ManagementSource } from "./api";

export interface ResourceSourcesContextValue {
  sources: ManagementSource[];
  loading: boolean;
  error?: Error;
  refresh: () => void;
}

const ResourceSourcesContext = createContext<ResourceSourcesContextValue | undefined>(undefined);
export const useResourceSources = () => useContext(ResourceSourcesContext);
export function ResourceSourcesProvider({
  value,
  children,
}: {
  value: ResourceSourcesContextValue;
  children: ReactNode;
}) {
  return (
    <ResourceSourcesContext.Provider value={value}>{children}</ResourceSourcesContext.Provider>
  );
}
