import type { EnhancerId } from '@/sdk/constants';
import type { EnhancerTargetOptionsItem } from '@/sdk/constants';

export interface CategoryEnhancerFullOptions {
  categoryId: number;
  enhancerId: EnhancerId;
  active: boolean;
  options?: EnhancerFullOptions;
}

export interface EnhancerFullOptions {
  targetOptions?: EnhancerTargetFullOptions[];
}

export interface EnhancerTargetFullOptions {
  propertyId?: number;
  integrateWithAlias?: boolean;
  autoMatchMultilevelString?: boolean;
  autoGenerateProperties?: boolean;
  target: number;
  dynamicTarget?: string;
}

export function defaultCategoryEnhancerTargetOptions(target: number, optionsItems?: EnhancerTargetOptionsItem[]): EnhancerTargetFullOptions {
  const eto: EnhancerTargetFullOptions = {
    propertyId: 0,
    target,
  };
  if (optionsItems != undefined) {
  }
  return eto;
}
