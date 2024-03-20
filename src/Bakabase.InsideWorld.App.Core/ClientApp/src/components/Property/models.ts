import type { StandardValueType } from '@/sdk/constants';

export interface IProperty {
  id: number;
  type?: StandardValueType;
  name: string;
}
