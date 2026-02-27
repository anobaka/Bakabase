import type { EnhancerDescriptor, EnhancerTargetDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";
import type { PropertyPool, PropertyType } from "@/sdk/constants";
import { EnhancerId, PropertyValueScope } from "@/sdk/constants";

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

/** Single enhancer target view model */
export interface EnhancementTargetItem {
  enhancerId: EnhancerId;
  enhancerName: string;
  targetId: number;
  targetName: string;
  propertyType: PropertyType;
  isDynamic: boolean;
  dynamicTarget?: string;
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

/**
 * Build target items from enhancer descriptors, grouped by enhancer.
 * Each group is sorted by target name alphabetically.
 */
export function buildTargetItems(
  descriptors: EnhancerDescriptor[]
): Map<EnhancerId, EnhancementTargetItem[]> {
  const result = new Map<EnhancerId, EnhancementTargetItem[]>();

  for (const desc of descriptors) {
    const items: EnhancementTargetItem[] = desc.targets.map((target: EnhancerTargetDescriptor) => ({
      enhancerId: desc.id,
      enhancerName: desc.name,
      targetId: target.id,
      targetName: target.name,
      propertyType: target.propertyType,
      isDynamic: target.isDynamic,
      enabled: false,
      config: {},
    }));

    // Sort by target name alphabetically
    items.sort((a, b) => a.targetName.localeCompare(b.targetName));
    result.set(desc.id, items);
  }

  // Sort the map by enhancer name
  const sorted = new Map(
    [...result.entries()].sort(([, a], [, b]) => {
      const nameA = a[0]?.enhancerName ?? "";
      const nameB = b[0]?.enhancerName ?? "";
      return nameA.localeCompare(nameB);
    })
  );

  return sorted;
}

/**
 * Merge existing enhancer config into target items.
 */
export function mergeWithExistingConfig(
  items: Map<EnhancerId, EnhancementTargetItem[]>,
  config: ApiEnhancerOptions[]
): Map<EnhancerId, EnhancementTargetItem[]> {
  const result = new Map<EnhancerId, EnhancementTargetItem[]>();

  for (const [enhancerId, targets] of items) {
    const enhancerConfig = config.find((c) => c.enhancerId === enhancerId);
    const newTargets = targets.map((target) => {
      if (!enhancerConfig?.targetOptions) return { ...target };

      const targetOption = enhancerConfig.targetOptions.find((to) => {
        if (target.isDynamic) {
          return to.target === target.targetId && to.dynamicTarget === target.dynamicTarget;
        }
        return to.target === target.targetId;
      });

      if (!targetOption) return { ...target };

      // Use type assertion since SDK types may not include all fields sent via API
      const to = targetOption as any;
      return {
        ...target,
        enabled: true,
        targetMapping:
          targetOption.propertyPool != null && targetOption.propertyId != null
            ? { pool: targetOption.propertyPool, id: targetOption.propertyId }
            : undefined,
        config: {
          autoBindProperty: to.autoBindProperty,
          autoMatchMultilevelString: to.autoMatchMultilevelString,
          coverSelectOrder: to.coverSelectOrder,
        },
      };
    });
    result.set(enhancerId, newTargets);
  }

  return result;
}

/**
 * Convert target items back to EnhancerFullOptions[] for storage.
 * Only includes enhancers that have at least one enabled target.
 */
export function convertToEnhancerOptions(
  items: Map<EnhancerId, EnhancementTargetItem[]>,
  enhancerLevelConfigs: Map<EnhancerId, Partial<ApiEnhancerOptions>>
): ApiEnhancerOptions[] {
  const result: ApiEnhancerOptions[] = [];

  for (const [enhancerId, targets] of items) {
    const enabledTargets = targets.filter((t) => t.enabled);
    if (enabledTargets.length === 0) continue;

    const levelConfig = enhancerLevelConfigs.get(enhancerId) ?? {};

    const enhancerOptions: ApiEnhancerOptions = {
      enhancerId,
      ...levelConfig,
      targetOptions: enabledTargets.map((t) => ({
        target: t.targetId,
        dynamicTarget: t.dynamicTarget,
        propertyPool: t.targetMapping?.pool ?? 0 as any,
        propertyId: t.targetMapping?.id ?? 0,
        coverSelectOrder: t.config?.coverSelectOrder,
      })),
    };

    result.push(enhancerOptions);
  }

  return result;
}

/**
 * Extract enhancer-level config from existing options (everything except targetOptions).
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
 * Get the relevant PropertyValueScopes for a property based on which enhancers target it.
 */
export function getRelevantScopes(
  items: Map<EnhancerId, EnhancementTargetItem[]>,
  propertyPool: PropertyPool,
  propertyId: number
): PropertyValueScope[] {
  const scopes: PropertyValueScope[] = [
    PropertyValueScope.Manual,
    PropertyValueScope.Synchronization,
  ];

  for (const [enhancerId, targets] of items) {
    const hasTarget = targets.some(
      (t) =>
        t.enabled &&
        t.targetMapping?.pool === propertyPool &&
        t.targetMapping?.id === propertyId
    );
    if (hasTarget) {
      scopes.push(enhancerIdToScope(enhancerId));
    }
  }

  return scopes;
}
