import type {
  DataOrigin,
  DataStatus,
  PropertyType,
  PropertyValueScope,
  ResourceDataType,
  ResourceSource,
  ResourceStatus,
  ResourceTag,
  StandardValueType,
} from "@/sdk/constants";
import type { PropertyPool } from "@/sdk/constants";

export type Value = {
  scope: PropertyValueScope;
  value?: any;
  bizValue?: any;
  aliasAppliedBizValue?: any;
};

/**
 * Whether a scope's resolved value actually holds content. Plain truthiness is wrong here: an
 * empty array is truthy but empty, while 0 and false are falsy but valid. Used to skip empty
 * scopes when resolving a property's multi-scope values down to one displayed value.
 */
export const isNonEmptyValue = (value: any): boolean => {
  if (value == null) return false;
  if (Array.isArray(value)) return value.length > 0;
  if (typeof value === "string") return value.trim().length > 0;

  return true;
};

export type Property = {
  name?: string;
  type: PropertyType;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  values?: Value[];
  visible?: boolean;
  order: number;
};

export type PlayableItem = {
  origin: DataOrigin;
  key: string;
  displayName?: string;
};

export type ResourceDataState = {
  resourceId: number;
  dataType: ResourceDataType;
  origin: DataOrigin;
  status: DataStatus;
};

export type PropertyValueScopePriority = {
  scope: PropertyValueScope;
  /** When this scope is empty, whether to continue with the next scope or stop and render blank. Ignored on the last entry. */
  fallbackOnEmpty: boolean;
};

export type PropertyValueScopePreference = {
  resourceId: number;
  propertyPool: PropertyPool;
  propertyId: number;
  /** Ordered scope priorities with per-scope fallback flag; null = no override (falls through to profile/global) */
  priorities?: PropertyValueScopePriority[];
};

/**
 * Resolve the effective scope chain for one property. A per-resource preference, when it carries
 * priorities, fully replaces the global priority. Within a preference an entry with
 * `fallbackOnEmpty: false` truncates the chain there — later scopes become unreachable, so an
 * empty value at that scope renders blank instead of falling through. The global priority has no
 * flags and always falls through.
 */
export const buildEffectiveScopePriority = (
  globalPriority: PropertyValueScope[],
  preference?: PropertyValueScopePreference,
): PropertyValueScope[] => {
  if (!preference?.priorities || preference.priorities.length === 0) {
    return globalPriority;
  }

  const chain: PropertyValueScope[] = [];

  for (const p of preference.priorities) {
    chain.push(p.scope);
    if (!p.fallbackOnEmpty) break;
  }

  return chain;
};

/** Walk `effectivePriority` and return the first scope that holds a non-empty value. */
export const selectScopedValue = (
  values: Value[] | undefined,
  effectivePriority: PropertyValueScope[],
): Value | undefined => {
  for (const scope of effectivePriority) {
    const value = values?.find((v) => v.scope == scope);

    if (value && isNonEmptyValue(value.aliasAppliedBizValue ?? value.bizValue)) {
      return value;
    }
  }

  return undefined;
};

/** Resolve a property's per-scope values down to the single value to display. */
export const resolveScopedValue = (
  values: Value[] | undefined,
  globalPriority: PropertyValueScope[],
  preference?: PropertyValueScopePreference,
): Value | undefined =>
  selectScopedValue(values, buildEffectiveScopePriority(globalPriority, preference));

export type ResourceSourceLink = {
  id: number;
  resourceId: number;
  source: ResourceSource;
  sourceKey: string;
  coverUrls?: string[];
  localCoverPaths?: string[];
};

export type Resource = {
  id: number;
  mediaLibraryId: number;
  categoryId: number;
  status: ResourceStatus;
  sourceLinks?: ResourceSourceLink[];
  fileName?: string;
  directory?: string;
  displayName?: string;
  path?: string;
  parentId?: number;
  hasChildren: boolean;
  isFile: boolean;
  createdAt: string;
  updatedAt: string;
  fileCreatedAt: string;
  fileModifiedAt: string;
  parent?: Resource;
  properties?: { [key in PropertyPool]?: Record<number, Property> };
  /** @deprecated */
  mediaLibraryName?: string;
  /** @deprecated */
  mediaLibraryColor?: string;
  mediaLibraries?: { id: number; name: string; color?: string }[];
  /** @deprecated */
  category?: { id: number; name: string };
  pinned: boolean;
  tags: ResourceTag[];
  playedAt?: string;
  /** Aggregated health score; null when no profile has scored this resource. */
  healthScore?: number;
  dataStates?: ResourceDataState[];

  /** Final resolved cover paths, populated by backend using priority-based selection */
  covers?: string[];
  /** Final resolved playable items from all sources */
  playableItems?: PlayableItem[];

  /** Per-(propertyPool, propertyId) scope priority overrides for this resource */
  scopePreferences?: PropertyValueScopePreference[];

  /**
   * Client-only token, bumped when this resource is reloaded after a backend cache
   * refresh. Forces dependent UI to re-resolve from the fresh data even when a path is
   * unchanged: the cover thumbnail URL appends it to bypass the browser image cache,
   * and playable-item resolution treats a bump like a new resource so stale SSE
   * discovery results don't mask the rebuilt cache. Not sent by the backend.
   */
  reloadToken?: number;
};
