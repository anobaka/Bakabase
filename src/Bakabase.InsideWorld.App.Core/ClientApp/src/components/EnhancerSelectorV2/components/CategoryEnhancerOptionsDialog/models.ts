import type { EnhancerId } from '@/sdk/constants';

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
  propertyId: number;
  integrateWithAlias?: boolean;
  autoMatchMultilevelString?: boolean;
}

export function defaultCategoryEnhancerTargetOptions(): EnhancerTargetFullOptions {
  return {
    integrateWithAlias: true,
    propertyId: 0,
  };
}
