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

// ─── Scenario definitions ────────────────────────────────────────────

export enum EnhancementScenario {
  Anime = "anime",
  Movie = "movie",
  Doujinshi = "doujinshi",
  Game = "game",
  AV = "av",
  General = "general",
}

/** Which enhancers belong to which scenario */
export const scenarioEnhancerMap: Record<EnhancementScenario, EnhancerId[]> = {
  [EnhancementScenario.Anime]: [EnhancerId.Bangumi],
  [EnhancementScenario.Movie]: [EnhancerId.Tmdb, EnhancerId.Kodi],
  [EnhancementScenario.Doujinshi]: [EnhancerId.ExHentai],
  [EnhancementScenario.Game]: [EnhancerId.DLsite],
  [EnhancementScenario.AV]: [EnhancerId.Av],
  [EnhancementScenario.General]: [EnhancerId.Bakabase, EnhancerId.Regex],
};

/** All scenarios in display order */
export const scenarioOrder: EnhancementScenario[] = [
  EnhancementScenario.General,
  EnhancementScenario.Anime,
  EnhancementScenario.Movie,
  EnhancementScenario.Doujinshi,
  EnhancementScenario.Game,
  EnhancementScenario.AV,
];

/** Reverse lookup: enhancerId → scenario */
export function getEnhancerScenario(enhancerId: EnhancerId): EnhancementScenario {
  for (const [scenario, ids] of Object.entries(scenarioEnhancerMap)) {
    if (ids.includes(enhancerId)) return scenario as EnhancementScenario;
  }
  return EnhancementScenario.General;
}

// ─── Property-centric data model ─────────────────────────────────────

/** One enhancer source for a property row */
export interface EnhancerSource {
  enhancerId: EnhancerId;
  enhancerName: string;
  targetId: number;
  targetDescriptor: EnhancerTargetDescriptor;
  enabled: boolean;
  targetMapping?: {
    pool: PropertyPool;
    id: number;
  };
  config?: {
    coverSelectOrder?: number;
    autoBindProperty?: boolean;
    autoMatchMultilevelString?: boolean;
  };
}

/** A single property row - may have multiple enhancer sources */
export interface PropertyRow {
  /** Canonical property name (target name) */
  propertyName: string;
  propertyType: PropertyType;
  /** All enhancers that can provide this property */
  sources: EnhancerSource[];
}

/** A scenario group containing property rows */
export interface ScenarioGroup {
  scenario: EnhancementScenario;
  /** Enhancer descriptors in this scenario */
  enhancers: EnhancerDescriptor[];
  /** Static property rows (non-dynamic targets) */
  staticProperties: PropertyRow[];
  /** Enhancers that support dynamic targets */
  dynamicEnhancers: EnhancerDescriptor[];
  /** Dynamic property rows (from configured dynamic targets) */
  dynamicProperties: PropertyRow[];
}

// ─── Build functions ─────────────────────────────────────────────────

/**
 * Build scenario groups from enhancer descriptors.
 * Static targets with the same name within a scenario are merged into one row.
 */
export function buildScenarioGroups(
  descriptors: EnhancerDescriptor[],
  existingConfig: ApiEnhancerOptions[]
): ScenarioGroup[] {
  const groups: ScenarioGroup[] = [];

  for (const scenario of scenarioOrder) {
    const enhancerIds = scenarioEnhancerMap[scenario];
    const enhancers = descriptors.filter((d) => enhancerIds.includes(d.id));
    if (enhancers.length === 0) continue;

    // Collect static properties, merging same-name targets
    const staticMap = new Map<string, PropertyRow>();
    // Collect dynamic enhancers and dynamic properties
    const dynamicEnhancers: EnhancerDescriptor[] = [];
    const dynamicMap = new Map<string, PropertyRow>();

    for (const desc of enhancers) {
      let hasDynamic = false;

      for (const target of desc.targets) {
        if (target.isDynamic) {
          hasDynamic = true;
          // Dynamic targets: build from existing config (already configured dynamic targets)
          const enhancerConfig = existingConfig.find((c) => c.enhancerId === desc.id);
          if (enhancerConfig?.targetOptions) {
            for (const to of enhancerConfig.targetOptions) {
              if (to.target === target.id && to.dynamicTarget) {
                const key = `${desc.id}:${target.id}:${to.dynamicTarget}`;
                const toAny = to as any;
                if (!dynamicMap.has(key)) {
                  dynamicMap.set(key, {
                    propertyName: to.dynamicTarget,
                    propertyType: target.propertyType,
                    sources: [{
                      enhancerId: desc.id,
                      enhancerName: desc.name,
                      targetId: target.id,
                      targetDescriptor: target,
                      enabled: true,
                      targetMapping:
                        to.propertyPool != null && to.propertyId != null
                          ? { pool: to.propertyPool, id: to.propertyId }
                          : undefined,
                      config: {
                        autoBindProperty: toAny.autoBindProperty,
                        autoMatchMultilevelString: toAny.autoMatchMultilevelString,
                        coverSelectOrder: toAny.coverSelectOrder,
                      },
                    }],
                  });
                }
              }
            }
          }
          continue;
        }

        // Static target: merge by target name
        const source = buildSource(desc, target, existingConfig);
        const existing = staticMap.get(target.name);
        if (existing) {
          existing.sources.push(source);
        } else {
          staticMap.set(target.name, {
            propertyName: target.name,
            propertyType: target.propertyType,
            sources: [source],
          });
        }
      }

      if (hasDynamic) {
        dynamicEnhancers.push(desc);
      }
    }

    // Sort properties alphabetically
    const staticProperties = [...staticMap.values()].sort((a, b) =>
      a.propertyName.localeCompare(b.propertyName)
    );
    const dynamicProperties = [...dynamicMap.values()].sort((a, b) =>
      a.propertyName.localeCompare(b.propertyName)
    );

    groups.push({
      scenario,
      enhancers,
      staticProperties,
      dynamicEnhancers,
      dynamicProperties,
    });
  }

  return groups;
}

function buildSource(
  desc: EnhancerDescriptor,
  target: EnhancerTargetDescriptor,
  existingConfig: ApiEnhancerOptions[]
): EnhancerSource {
  const enhancerConfig = existingConfig.find((c) => c.enhancerId === desc.id);
  const targetOption = enhancerConfig?.targetOptions?.find((to) => to.target === target.id);

  const source: EnhancerSource = {
    enhancerId: desc.id,
    enhancerName: desc.name,
    targetId: target.id,
    targetDescriptor: target,
    enabled: false,
    config: {},
  };

  if (targetOption) {
    const toAny = targetOption as any;
    source.enabled = true;
    source.targetMapping =
      targetOption.propertyPool != null && targetOption.propertyId != null
        ? { pool: targetOption.propertyPool, id: targetOption.propertyId }
        : undefined;
    source.config = {
      autoBindProperty: toAny.autoBindProperty,
      autoMatchMultilevelString: toAny.autoMatchMultilevelString,
      coverSelectOrder: toAny.coverSelectOrder,
    };
  }

  return source;
}

// ─── Convert back to API format ──────────────────────────────────────

/**
 * Convert scenario groups back to EnhancerFullOptions[] for storage.
 * Only includes enhancers with at least one enabled source.
 */
export function convertGroupsToEnhancerOptions(
  groups: ScenarioGroup[],
  enhancerLevelConfigs: Map<EnhancerId, Partial<ApiEnhancerOptions>>
): ApiEnhancerOptions[] {
  // Collect all enabled sources by enhancer
  const enhancerTargets = new Map<EnhancerId, {
    target: number;
    dynamicTarget?: string;
    pool?: PropertyPool;
    propertyId?: number;
    coverSelectOrder?: number;
  }[]>();

  for (const group of groups) {
    const allProperties = [...group.staticProperties, ...group.dynamicProperties];
    for (const prop of allProperties) {
      for (const source of prop.sources) {
        if (!source.enabled) continue;
        const targets = enhancerTargets.get(source.enhancerId) ?? [];
        targets.push({
          target: source.targetId,
          dynamicTarget: source.targetDescriptor.isDynamic ? prop.propertyName : undefined,
          pool: source.targetMapping?.pool,
          propertyId: source.targetMapping?.id,
          coverSelectOrder: source.config?.coverSelectOrder,
        });
        enhancerTargets.set(source.enhancerId, targets);
      }
    }
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

/**
 * Extract enhancer-level config from existing options.
 */
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

/**
 * Get enhancer IDs that have at least one enabled source across all groups.
 */
export function getUsedEnhancerIds(groups: ScenarioGroup[]): EnhancerId[] {
  const ids = new Set<EnhancerId>();
  for (const group of groups) {
    for (const prop of [...group.staticProperties, ...group.dynamicProperties]) {
      for (const source of prop.sources) {
        if (source.enabled) ids.add(source.enhancerId);
      }
    }
  }
  return [...ids];
}

/**
 * Check if an enhancer needs additional configuration (e.g. Regex needs expressions).
 */
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
