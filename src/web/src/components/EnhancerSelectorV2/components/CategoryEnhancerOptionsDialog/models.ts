import type { EnhancerId } from "@/sdk/constants";
import type { PropertyPool } from "@/sdk/constants";
import type { EnhancerTargetDescriptor } from "@/components/EnhancerSelectorV2/models";

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
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  target: number;
  dynamicTarget?: string;
  propertyPool?: PropertyPool;
}

export function defaultCategoryEnhancerTargetOptions(
  descriptor: EnhancerTargetDescriptor,
): EnhancerTargetFullOptions {
  const eto: EnhancerTargetFullOptions = {
    target: descriptor.id,
  };

  if (descriptor.optionsItems != undefined) {
  }

  return eto;
}

export type RegexEnhancerFullOptions = EnhancerFullOptions & {
  expressions: string[];
};
