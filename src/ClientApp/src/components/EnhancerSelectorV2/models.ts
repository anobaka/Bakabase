import type {
  EnhancerId,
  EnhancerTargetOptionsItem,
  ReservedProperty,
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
  valueType: StandardValueType;
  optionsItems?: EnhancerTargetOptionsItem[];
}
