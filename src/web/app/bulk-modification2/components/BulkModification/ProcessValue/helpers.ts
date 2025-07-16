import type { IProperty } from '@/components/Property/models';
import type { PropertyType, StandardValueType } from '@/sdk/constants';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import { BulkModificationStringProcessOperation } from '@/sdk/constants';
import type { RecursivePartial } from '@/components/types';
import type {
  StringProcessOptions,
} from '@/pages/BulkModification2/components/BulkModification/Processes/StringValueProcess/models';
import type { BulkModificationProcessValue } from '@/pages/BulkModification2/components/BulkModification/models';

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

export const validate = (
  value?: Partial<BulkModificationProcessValue>,
): string | undefined => {
  if (!value) {
    return 'Please provide a valid value';
  }

  if (value.type == undefined) {
    return 'Please provide a valid value type';
  }

  switch (value.type) {
    case BulkModificationProcessorValueType.ManuallyInput: {
      if (value.editorPropertyType == undefined) {
        return 'Please provide a valid property type';
      }
      if (value.value == undefined || value.value.length == 0) {
        return 'Please provide a valid value';
      }
      break;
    }
    case BulkModificationProcessorValueType.Variable:
      if (value.value == undefined) {
        return 'Please provide a valid variable';
      }
      break;
  }

  return;
};
