import type {
  EnhancerId,
  EnhancerTargetOptionsItem,
  EnhancerTargetType,
  ReservedResourceProperty,
  StandardValueType,
} from '@/sdk/constants';

export interface EnhancerDescriptor {
  id: EnhancerId;
  name: string;
  description?: string;
  targets: EnhancerTargetDescriptor[];
}

export interface EnhancerTargetDescriptor {
  id: number;
  isDynamic: boolean;
  name: string;
  description?: string;
  type: EnhancerTargetType;
  valueType: StandardValueType;
  optionsItems?: EnhancerTargetOptionsItem[];
  reservedResourcePropertyCandidate?: ReservedResourceProperty;
}
