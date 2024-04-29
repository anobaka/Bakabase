import type { EnhancerId, StandardValueType } from '@/sdk/constants';

export interface EnhancerDescriptor {
  id: EnhancerId;
  name: string;
  description?: string;
  targets: {
    id: number;
    name: string;
    description?: string;
    valueType: StandardValueType;
  }[];
}
