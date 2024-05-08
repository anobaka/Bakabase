import type { EnhancerId } from '@/sdk/constants';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';

export interface CategoryEnhancerFullOptions {
  categoryId: number;
  enhancerId: EnhancerId;
  active: boolean;
  options?: EnhancerFullOptions;
}

export interface EnhancerFullOptions {
  targetOptionsMap?: Record<number, EnhancerTargetFullOptions>;
}

export interface EnhancerTargetFullOptions {
  propertyId?: number;
  integrateWithAlias?: boolean;
  autoMatchMultilevelString?: boolean;
}

export function defaultCategoryEnhancerTargetOptions(optionsItems?: EnhancerTargetOptionsItem[]): EnhancerTargetFullOptions {
  const eto: EnhancerTargetFullOptions = {
    propertyId: 0,
  };
  if (optionsItems != undefined) {
    if (optionsItems.includes(EnhancerTargetOptionsItem.IntegrateWithAlias)) {
      eto.integrateWithAlias = true;
    }
  }
  return eto;
}
