import type { SearchOperation, StandardValueType } from '@/sdk/constants';

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
  isCustomProperty?: boolean;
  group?: IGroup;
  operation?: SearchOperation;
  value?: string;
}

export interface IGroup {
  groups?: IGroup[];
  filters?: IFilter[];
  combinator: GroupCombinator;
}
