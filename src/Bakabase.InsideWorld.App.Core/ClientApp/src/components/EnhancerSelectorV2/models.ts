import type { EnhancerId, EnhancerTargetOptionsItem, StandardValueType } from '@/sdk/constants';

export interface EnhancerDescriptor {
  id: EnhancerId;
  name: string;
  description?: string;
  targets: {
    id: number;
    isDynamic: boolean;
    name: string;
    description?: string;
    valueType: StandardValueType;
    optionsItems?: EnhancerTargetOptionsItem[];
  }[];
}
