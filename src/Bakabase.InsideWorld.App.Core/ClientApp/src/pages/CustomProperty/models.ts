import type { CustomPropertyType } from '@/sdk/constants';

export interface CustomProperty {
  id: string;
  name: string;
  type: CustomPropertyType;
}
