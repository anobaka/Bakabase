import type { ReactNode } from "react";
import type { ManagementSource } from "./api";
import type BApi from "@/sdk/BApi";

import { createContext, useContext, useMemo } from "react";

import { sourceKey } from "./api";
import { createResourceApi } from "./resourceApi";

export interface ResourceOwnerScope {
  readonly source: Readonly<ManagementSource>;
  readonly resourceId?: number;
  readonly key: string;
  readonly api: typeof BApi;
}

const ResourceOwnerContext = createContext<ResourceOwnerScope | undefined>(undefined);

/** Used by detached modals/windows to restore the exact owner captured when they opened. */
export function ResourceOwnerScopeProvider({
  scope,
  children,
}: {
  scope: ResourceOwnerScope | undefined;
  children: ReactNode;
}) {
  return <ResourceOwnerContext.Provider value={scope}>{children}</ResourceOwnerContext.Provider>;
}

export function ResourceOwnerProvider({
  source,
  resourceId,
  children,
}: {
  source?: ManagementSource;
  resourceId?: number;
  children: ReactNode;
}) {
  const scope = useMemo<ResourceOwnerScope | undefined>(() => {
    if (!source) return undefined;
    const capturedSource = Object.freeze({ ...source });

    return Object.freeze({
      source: capturedSource,
      resourceId,
      key: sourceKey(capturedSource),
      api: createResourceApi(capturedSource, resourceId),
    });
  }, [source, resourceId]);

  return <ResourceOwnerScopeProvider scope={scope}>{children}</ResourceOwnerScopeProvider>;
}

export const useResourceOwnerScope = () => useContext(ResourceOwnerContext);
export const useResourceOwner = () => useResourceOwnerScope()?.source;
export const useResourceOwnerKey = () => useResourceOwnerScope()?.key ?? "local";
export const useResourceApi = (): typeof BApi =>
  useResourceOwnerScope()?.api ?? createResourceApi();
