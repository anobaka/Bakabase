import type { StandardValueType } from '@/sdk/constants';

export type Enhancement = {
  id: number;
  resourceId: number;
  enhancerId: number;
  enhancerName?: string;
  target: number;
  targetName?: string;
  dynamicTarget?: string;
  valueType: StandardValueType;
  value?: any;
  customPropertyValue?: {
    id: number;
    value: any;
  };
  createdAt: string;
};
