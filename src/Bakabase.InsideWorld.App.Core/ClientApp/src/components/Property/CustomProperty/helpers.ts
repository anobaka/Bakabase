import type { ICustomProperty } from '@/pages/CustomProperty/models';
import type { IProperty } from '@/components/Property/models';
import type { StandardValueType } from '@/sdk/constants';
import type {
  BakabaseInsideWorldModelsModelsDtosCustomPropertyAbstractionsCustomPropertyDto,
} from '@/sdk/Api';

export function convertCustomPropertyToProperty(cp: ICustomProperty): IProperty {
  return {
    id: cp.id,
    name: cp.name,
    type: cp.type as unknown as StandardValueType,
    isReserved: false,
  };
}
