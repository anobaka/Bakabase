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

export type KeyValue<TK = string, TV = number> = {
  key: TK;
  value: TV;
};

export type LabelValue<TL = string, TV = number> = {
  label: TL;
  value: TV;
};

export type PropertyMap = {[key in PropertyPool]?: Record<number, IProperty>};

export type Pageable = {
  page: number;
  pageSize: number;
  total: number;
  totalPage: number;
};
