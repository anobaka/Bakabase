import type { StandardValueType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

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
    property?: IProperty;
    bizValue?: any;
  };
  createdAt: string;
};
