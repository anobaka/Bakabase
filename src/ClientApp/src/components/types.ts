import type { PropertyPool } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

export type RecursivePartial<T> = {
  [P in keyof T]?: RecursivePartial<T[P]>;
};

export type PartialExcept<T, K extends keyof T> = RecursivePartial<T> & Pick<T, K>;

export type IdName<T = number> = {
  id: T;
  name: string;
};

export type KeyValue<TK, TV> = {
  key: TK;
  value: TV;
};

export type LabelValue<TK, TV> = {
  label: TK;
  value: TV;
};

export type PropertyMap = {[key in PropertyPool]?: Record<number, IProperty>};
