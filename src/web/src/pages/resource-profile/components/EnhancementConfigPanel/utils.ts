import type { EnhancerDescriptor, EnhancerTargetDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";
import type { PropertyPool, PropertyType } from "@/sdk/constants";
import { EnhancerId, EnhancerTag, PropertyValueScope } from "@/sdk/constants";

type ApiEnhancerOptions = BakabaseAbstractionsModelsDomainEnhancerFullOptions;

/** Map EnhancerId to PropertyValueScope */
export const enhancerIdToScope = (id: EnhancerId): PropertyValueScope => {
  const map: Record<EnhancerId, PropertyValueScope> = {
    [EnhancerId.Bakabase]: PropertyValueScope.BakabaseEnhancer,
    [EnhancerId.ExHentai]: PropertyValueScope.ExHentaiEnhancer,
    [EnhancerId.Bangumi]: PropertyValueScope.BangumiEnhancer,
    [EnhancerId.DLsite]: PropertyValueScope.DLsiteEnhancer,
    [EnhancerId.Regex]: PropertyValueScope.RegexEnhancer,
    [EnhancerId.Kodi]: PropertyValueScope.KodiEnhancer,
    [EnhancerId.Tmdb]: PropertyValueScope.TmdbEnhancer,
    [EnhancerId.Av]: PropertyValueScope.AvEnhancer,
  };
  return map[id];
};

// ─── Property Group definitions (many-to-many with enhancers) ────────

export enum PropertyGroup {
  General = "general",
  Anime = "anime",
  Movie = "movie",
  TV = "tv",
  Doujinshi = "doujinshi",
  Game = "game",
  AV = "av",
}

/** Many-to-many: each group maps to enhancers, an enhancer can appear in multiple groups */
export const groupEnhancerMap: Record<PropertyGroup, EnhancerId[]> = {
  [PropertyGroup.General]: [EnhancerId.Bakabase, EnhancerId.Regex],
  [PropertyGroup.Anime]: [EnhancerId.Bangumi],
  [PropertyGroup.Movie]: [EnhancerId.Tmdb, EnhancerId.Kodi],
  [PropertyGroup.TV]: [EnhancerId.Tmdb, EnhancerId.Kodi],
  [PropertyGroup.Doujinshi]: [EnhancerId.ExHentai],
  [PropertyGroup.Game]: [EnhancerId.DLsite],
  [PropertyGroup.AV]: [EnhancerId.Av],
};

export const groupOrder: PropertyGroup[] = [
  PropertyGroup.General,
  PropertyGroup.Anime,
  PropertyGroup.Movie,
  PropertyGroup.TV,
  PropertyGroup.Doujinshi,
  PropertyGroup.Game,
  PropertyGroup.AV,
];

// ─── Flat source state (single source of truth) ─────────────────────

export function sourceKey(enhancerId: EnhancerId, targetId: number, dynamicTarget?: string): string {
  return dynamicTarget ? `${enhancerId}:${targetId}:${dynamicTarget}` : `${enhancerId}:${targetId}`;
}

export interface SourceState {
  enhancerId: EnhancerId;
  enhancerName: string;
  targetId: number;
  targetDescriptor: EnhancerTargetDescriptor;
  enabled: boolean;
  isDynamic: boolean;
  dynamicTarget?: string;
  targetMapping?: { pool: PropertyPool; id: number };
  config?: {
    coverSelectOrder?: number;
    autoBindProperty?: boolean;
    autoMatchMultilevelString?: boolean;
  };
}

/** Build the flat source state map from descriptors + existing config */
export function buildSourceStates(
  descriptors: EnhancerDescriptor[],
  existingConfig: ApiEnhancerOptions[]
): Map<string, SourceState> {
  const states = new Map<string, SourceState>();

  for (const desc of descriptors) {
    const enhancerConfig = existingConfig.find((c) => c.enhancerId === desc.id);

    for (const target of desc.targets) {
      if (target.isDynamic) {
        // Dynamic targets: only create states for already-configured ones
        if (enhancerConfig?.targetOptions) {
          for (const to of enhancerConfig.targetOptions) {
            if (to.target === target.id && to.dynamicTarget) {
              const key = sourceKey(desc.id, target.id, to.dynamicTarget);
              const toAny = to as any;
              states.set(key, {
                enhancerId: desc.id,
                enhancerName: desc.name,
                targetId: target.id,
                targetDescriptor: target,
                enabled: true,
                isDynamic: true,
                dynamicTarget: to.dynamicTarget,
                targetMapping:
                  to.propertyPool != null && to.propertyId != null
                    ? { pool: to.propertyPool, id: to.propertyId }
                    : undefined,
                config: {
                  autoBindProperty: toAny.autoBindProperty,
                  autoMatchMultilevelString: toAny.autoMatchMultilevelString,
                  coverSelectOrder: toAny.coverSelectOrder,
                },
              });
            }
          }
        }
        continue;
      }

      // Static target
      const key = sourceKey(desc.id, target.id);
      const targetOption = enhancerConfig?.targetOptions?.find((to) => to.target === target.id);
      const toAny = targetOption as any;

      states.set(key, {
        enhancerId: desc.id,
        enhancerName: desc.name,
        targetId: target.id,
        targetDescriptor: target,
        enabled: !!targetOption,
        isDynamic: false,
        targetMapping:
          targetOption?.propertyPool != null && targetOption?.propertyId != null
            ? { pool: targetOption.propertyPool, id: targetOption.propertyId }
            : undefined,
        config: targetOption
          ? {
              autoBindProperty: toAny?.autoBindProperty,
              autoMatchMultilevelString: toAny?.autoMatchMultilevelString,
              coverSelectOrder: toAny?.coverSelectOrder,
            }
          : {},
      });
    }
  }

  return states;
}

// ─── Derived views per group ─────────────────────────────────────────

export interface EnhancerSource {
  stateKey: string;
  enhancerId: EnhancerId;
  enhancerName: string;
  targetId: number;
  targetDescriptor: EnhancerTargetDescriptor;
  enabled: boolean;
  isDynamic: boolean;
  dynamicTarget?: string;
}

export interface PropertyRow {
  propertyName: string;
  propertyType: PropertyType;
  sources: EnhancerSource[];
}

/** Derive property rows for a group by filtering & merging same-name targets */
export function getGroupPropertyRows(
  group: PropertyGroup,
  sourceStates: Map<string, SourceState>
): PropertyRow[] {
  const enhancerIds = groupEnhancerMap[group];
  const nameMap = new Map<string, PropertyRow>();

  for (const [key, state] of sourceStates) {
    if (!enhancerIds.includes(state.enhancerId)) continue;

    const name = state.isDynamic ? (state.dynamicTarget ?? "") : state.targetDescriptor.name;
    if (!name) continue;

    const source: EnhancerSource = {
      stateKey: key,
      enhancerId: state.enhancerId,
      enhancerName: state.enhancerName,
      targetId: state.targetId,
      targetDescriptor: state.targetDescriptor,
      enabled: state.enabled,
      isDynamic: state.isDynamic,
      dynamicTarget: state.dynamicTarget,
    };

    const existing = nameMap.get(name);
    if (existing) {
      existing.sources.push(source);
    } else {
      nameMap.set(name, {
        propertyName: name,
        propertyType: state.targetDescriptor.propertyType,
        sources: [source],
      });
    }
  }

  return [...nameMap.values()].sort((a, b) => a.propertyName.localeCompare(b.propertyName));
}

/** Count properties (by unique name) with at least one enabled source in a group */
export function getGroupEnabledCount(
  group: PropertyGroup,
  sourceStates: Map<string, SourceState>
): number {
  const enhancerIds = groupEnhancerMap[group];
  const enabledNames = new Set<string>();

  for (const [, state] of sourceStates) {
    if (!enhancerIds.includes(state.enhancerId) || !state.enabled) continue;
    const name = state.isDynamic ? (state.dynamicTarget ?? "") : state.targetDescriptor.name;
    if (name) enabledNames.add(name);
  }

  return enabledNames.size;
}

/** Collect unique enhancer IDs from multiple groups */
export function getEnhancerIdsForGroups(groups: PropertyGroup[]): Set<EnhancerId> {
  const ids = new Set<EnhancerId>();
  for (const group of groups) {
    for (const id of groupEnhancerMap[group]) {
      ids.add(id);
    }
  }
  return ids;
}

/** Derive property rows for multiple selected groups (union of enhancers, deduped) */
export function getPropertyRowsForGroups(
  groups: PropertyGroup[],
  sourceStates: Map<string, SourceState>
): PropertyRow[] {
  const enhancerIds = getEnhancerIdsForGroups(groups);
  const nameMap = new Map<string, PropertyRow>();

  for (const [key, state] of sourceStates) {
    if (!enhancerIds.has(state.enhancerId)) continue;

    const name = state.isDynamic ? (state.dynamicTarget ?? "") : state.targetDescriptor.name;
    if (!name) continue;

    const source: EnhancerSource = {
      stateKey: key,
      enhancerId: state.enhancerId,
      enhancerName: state.enhancerName,
      targetId: state.targetId,
      targetDescriptor: state.targetDescriptor,
      enabled: state.enabled,
      isDynamic: state.isDynamic,
      dynamicTarget: state.dynamicTarget,
    };

    const existing = nameMap.get(name);
    if (existing) {
      existing.sources.push(source);
    } else {
      nameMap.set(name, {
        propertyName: name,
        propertyType: state.targetDescriptor.propertyType,
        sources: [source],
      });
    }
  }

  return [...nameMap.values()].sort((a, b) => a.propertyName.localeCompare(b.propertyName));
}

/** Get dynamic enhancers from multiple selected groups */
export function getDynamicEnhancersForGroups(
  groups: PropertyGroup[],
  descriptors: EnhancerDescriptor[]
): EnhancerDescriptor[] {
  const enhancerIds = getEnhancerIdsForGroups(groups);
  return descriptors.filter(
    (d) => enhancerIds.has(d.id) && d.targets.some((t) => t.isDynamic)
  );
}

/** Get enhancers in a group that have dynamic targets */
export function getGroupDynamicEnhancers(
  group: PropertyGroup,
  descriptors: EnhancerDescriptor[]
): EnhancerDescriptor[] {
  const enhancerIds = groupEnhancerMap[group];
  return descriptors.filter(
    (d) => enhancerIds.includes(d.id) && d.targets.some((t) => t.isDynamic)
  );
}

// ─── Convert back to API format ──────────────────────────────────────

/** Convert flat source states back to EnhancerFullOptions[] for storage */
export function convertStatesToEnhancerOptions(
  sourceStates: Map<string, SourceState>,
  enhancerLevelConfigs: Map<EnhancerId, Partial<ApiEnhancerOptions>>
): ApiEnhancerOptions[] {
  const enhancerTargets = new Map<EnhancerId, {
    target: number;
    dynamicTarget?: string;
    pool?: PropertyPool;
    propertyId?: number;
    coverSelectOrder?: number;
  }[]>();

  for (const [, state] of sourceStates) {
    if (!state.enabled) continue;
    const targets = enhancerTargets.get(state.enhancerId) ?? [];
    targets.push({
      target: state.targetId,
      dynamicTarget: state.isDynamic ? state.dynamicTarget : undefined,
      pool: state.targetMapping?.pool,
      propertyId: state.targetMapping?.id,
      coverSelectOrder: state.config?.coverSelectOrder,
    });
    enhancerTargets.set(state.enhancerId, targets);
  }

  const result: ApiEnhancerOptions[] = [];
  for (const [enhancerId, targets] of enhancerTargets) {
    const levelConfig = enhancerLevelConfigs.get(enhancerId) ?? {};
    result.push({
      enhancerId,
      ...levelConfig,
      targetOptions: targets.map((t) => ({
        target: t.target,
        dynamicTarget: t.dynamicTarget,
        propertyPool: t.pool ?? 0 as any,
        propertyId: t.propertyId ?? 0,
        coverSelectOrder: t.coverSelectOrder,
      })),
    });
  }

  return result;
}

/** Extract enhancer-level config from existing options */
export function extractEnhancerLevelConfigs(
  config: ApiEnhancerOptions[]
): Map<EnhancerId, Partial<ApiEnhancerOptions>> {
  const result = new Map<EnhancerId, Partial<ApiEnhancerOptions>>();
  for (const c of config) {
    const { targetOptions, ...rest } = c;
    result.set(c.enhancerId as EnhancerId, rest);
  }
  return result;
}

/** Check if an enhancer needs additional configuration */
export function enhancerNeedsConfig(
  descriptor: EnhancerDescriptor,
  levelConfig: Partial<ApiEnhancerOptions> | undefined
): boolean {
  if (descriptor.tags.includes(EnhancerTag.UseRegex)) {
    const expressions = levelConfig?.expressions;
    if (!expressions || expressions.filter((e: string) => e.trim()).length === 0) {
      return true;
    }
  }
  return false;
}
