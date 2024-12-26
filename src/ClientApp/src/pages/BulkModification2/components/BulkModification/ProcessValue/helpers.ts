import type { IProperty } from '@/components/Property/models';
import type { PropertyType, StandardValueType } from '@/sdk/constants';

export const buildFakeProperty = (type: PropertyType, dbValueType: StandardValueType, bizValueType: StandardValueType): IProperty => {
  return {
    id: 0,
    name: '',
    type: type,
    typeName: '',
    pool: 0,
    poolName: '',
    dbValueType: dbValueType,
    bizValueType: bizValueType,
  };
};
