import type { EnhancerDescriptor, EnhancerTargetDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";
import type { PropertyPool } from "@/sdk/constants";
import { EnhancerId, EnhancerTag, PropertyType, PropertyValueScope } from "@/sdk/constants";

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
  Doujinshi = "doujinshi",
  Manga = "manga",
  Gallery = "gallery",
  GameCG = "gameCG",
  Anime = "anime",
  Movie = "movie",
  TV = "tv",
  Game = "game",
  Music = "music",
  VoiceWork = "voiceWork",
  Book = "book",
  AV = "av",
}

/** Many-to-many: each group maps to enhancers, an enhancer can appear in multiple groups */
export const groupEnhancerMap: Record<PropertyGroup, EnhancerId[]> = {
  [PropertyGroup.General]: [EnhancerId.Regex],
  [PropertyGroup.Doujinshi]: [EnhancerId.ExHentai, EnhancerId.DLsite, EnhancerId.Bakabase],
  [PropertyGroup.Manga]: [EnhancerId.ExHentai, EnhancerId.DLsite, EnhancerId.Bakabase],
  [PropertyGroup.Gallery]: [EnhancerId.ExHentai, EnhancerId.DLsite],
  [PropertyGroup.GameCG]: [EnhancerId.ExHentai, EnhancerId.Bakabase],
  [PropertyGroup.Anime]: [EnhancerId.Bangumi, EnhancerId.Kodi, EnhancerId.Tmdb],
  [PropertyGroup.Movie]: [EnhancerId.Tmdb, EnhancerId.Kodi, EnhancerId.Bangumi, EnhancerId.Av],
  [PropertyGroup.TV]: [EnhancerId.Tmdb, EnhancerId.Kodi, EnhancerId.Bangumi, EnhancerId.Av],
  [PropertyGroup.Game]: [EnhancerId.Bangumi, EnhancerId.DLsite, EnhancerId.ExHentai],
  [PropertyGroup.Music]: [EnhancerId.Bangumi, EnhancerId.Kodi],
  [PropertyGroup.VoiceWork]: [EnhancerId.DLsite],
  [PropertyGroup.Book]: [EnhancerId.Bangumi, EnhancerId.DLsite],
  [PropertyGroup.AV]: [EnhancerId.Av, EnhancerId.Tmdb, EnhancerId.Kodi],
};

export const groupOrder: PropertyGroup[] = [
  PropertyGroup.General,
  PropertyGroup.Anime,
  PropertyGroup.Movie,
  PropertyGroup.TV,
  PropertyGroup.Doujinshi,
  PropertyGroup.Manga,
  PropertyGroup.Gallery,
  PropertyGroup.GameCG,
  PropertyGroup.Game,
  PropertyGroup.VoiceWork,
  PropertyGroup.Music,
  PropertyGroup.Book,
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

/** Get the bound property mapping for a row (from any source that has one) */
export function getRowBinding(
  row: PropertyRow,
  sourceStates: Map<string, SourceState>
): { pool: PropertyPool; id: number } | undefined {
  for (const source of row.sources) {
    const state = sourceStates.get(source.stateKey);
    if (state?.targetMapping) return state.targetMapping;
  }
  return undefined;
}

/** Check if a row has any enabled source but no bound property */
export function isRowEnabledButUnbound(
  row: PropertyRow,
  sourceStates: Map<string, SourceState>
): boolean {
  const hasEnabled = row.sources.some((s) => s.enabled);
  if (!hasEnabled) return false;
  return !getRowBinding(row, sourceStates);
}

/** Composite key for aggregation: name + propertyType */
function propertyRowKey(name: string, propertyType: PropertyType): string {
  return `${name}::${propertyType}`;
}

/** Derive property rows for a group by filtering & merging same-name+type targets */
export function getGroupPropertyRows(
  group: PropertyGroup,
  sourceStates: Map<string, SourceState>
): PropertyRow[] {
  const enhancerIds = groupEnhancerMap[group];
  const rowMap = new Map<string, PropertyRow>();

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

    const rk = propertyRowKey(name, state.targetDescriptor.propertyType);
    const existing = rowMap.get(rk);
    if (existing) {
      existing.sources.push(source);
    } else {
      rowMap.set(rk, {
        propertyName: name,
        propertyType: state.targetDescriptor.propertyType,
        sources: [source],
      });
    }
  }

  return [...rowMap.values()].sort((a, b) => a.propertyName.localeCompare(b.propertyName));
}

/** Count properties (by unique name+type) with at least one enabled source in a group */
export function getGroupEnabledCount(
  group: PropertyGroup,
  sourceStates: Map<string, SourceState>
): number {
  const enhancerIds = groupEnhancerMap[group];
  const enabledKeys = new Set<string>();

  for (const [, state] of sourceStates) {
    if (!enhancerIds.includes(state.enhancerId) || !state.enabled) continue;
    const name = state.isDynamic ? (state.dynamicTarget ?? "") : state.targetDescriptor.name;
    if (name) enabledKeys.add(propertyRowKey(name, state.targetDescriptor.propertyType));
  }

  return enabledKeys.size;
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

/** Derive property rows for multiple selected groups (union of enhancers, deduped by name+type) */
export function getPropertyRowsForGroups(
  groups: PropertyGroup[],
  sourceStates: Map<string, SourceState>
): PropertyRow[] {
  const enhancerIds = getEnhancerIdsForGroups(groups);
  const rowMap = new Map<string, PropertyRow>();

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

    const rk = propertyRowKey(name, state.targetDescriptor.propertyType);
    const existing = rowMap.get(rk);
    if (existing) {
      existing.sources.push(source);
    } else {
      rowMap.set(rk, {
        propertyName: name,
        propertyType: state.targetDescriptor.propertyType,
        sources: [source],
      });
    }
  }

  return [...rowMap.values()].sort((a, b) => a.propertyName.localeCompare(b.propertyName));
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
