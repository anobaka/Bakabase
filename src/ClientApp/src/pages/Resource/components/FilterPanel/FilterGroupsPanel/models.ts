import type { ResourcePropertyType, SearchOperation } from '@/sdk/constants';
import { StandardValueType } from '@/sdk/constants';

export type DataPoolCategory = {id: number; name: string};
export type DataPoolMediaLibrary = {id: number; name: string; categoryId: number; resourceCount: number};


export interface DataPool {
  categoryMap: Record<number, DataPoolCategory>;
  mediaLibraryMap: Record<number, DataPoolMediaLibrary>;
}

export enum GroupCombinator {
  And = 1,
  Or,
}

export interface IFilter {
  propertyId?: number;
  propertyType?: ResourcePropertyType;
  group?: IGroup;
  operation?: SearchOperation;
  dbValue?: string;
  bizValue?: string;
}

export interface IGroup {
  groups?: IGroup[];
  filters?: IFilter[];
  combinator: GroupCombinator;
}
