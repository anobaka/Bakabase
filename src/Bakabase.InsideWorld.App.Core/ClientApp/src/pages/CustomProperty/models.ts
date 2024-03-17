import type { CustomPropertyType } from '@/sdk/constants';

export interface ICustomProperty {
  id: string;
  name: string;
  type: CustomPropertyType;
}
export interface IChoice {
  value?: string;
  color?: string;
  hide?: boolean;
}
