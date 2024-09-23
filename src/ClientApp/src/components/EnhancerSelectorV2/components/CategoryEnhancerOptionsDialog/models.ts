import type { EnhancerId, ReservedResourceProperty } from '@/sdk/constants';
import { ResourcePropertyType } from '@/sdk/constants';
import type { EnhancerTargetDescriptor } from '@/components/EnhancerSelectorV2/models';

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
  autoBindProperty?: boolean;
  target: number;
  dynamicTarget?: string;
  reservedResourcePropertyCandidate?: ReservedResourceProperty;
  propertyType?: ResourcePropertyType;
}

export function defaultCategoryEnhancerTargetOptions(descriptor: EnhancerTargetDescriptor): EnhancerTargetFullOptions {
  const eto: EnhancerTargetFullOptions = {
    propertyId: descriptor.reservedResourcePropertyCandidate,
    propertyType: descriptor.reservedResourcePropertyCandidate == undefined ? undefined : ResourcePropertyType.Reserved,
    target: descriptor.id,
  };
  if (descriptor.optionsItems != undefined) {
  }
  return eto;
}
