import type { BulkModificationProcessorValueType, PropertyPool, PropertyType, PropertyValueScope } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

export type BulkModificationProcessStep = {
  operation: number;
  options?: any;
};

export type BulkModificationVariable = {
  scope: PropertyValueScope;
  propertyPool: PropertyPool;
  propertyId: number;
  name: string;
  key: string;

  preprocesses?: BulkModificationProcessStep[];

  property: IProperty;
};

export type BulkModificationProcess = {
  propertyPool: PropertyPool;
  propertyId: number;

  steps?: BulkModificationProcessStep[];
  property: IProperty;
};

export type BulkModificationProcessValue = {
  type: BulkModificationProcessorValueType;
  editorPropertyType?: PropertyType;
  propertyPool?: PropertyPool;
  propertyId?: number;
  // serialized
  value?: string;
  followPropertyChanges?: boolean;
  property?: IProperty;
};
