import type { PropertyPool, StandardValueType } from '@/sdk/constants';
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
  propertyPool?: PropertyPool;
  propertyId?: number;
  customPropertyValue?: {
    id: number;
    value: any;
    bizValue?: any;
  };
  createdAt: string;
  property?: IProperty;
  reservedPropertyValue?: {
    rating?: number;
    introduction?: string;
  };
};
