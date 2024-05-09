import type { SearchOperation, StandardValueType } from '@/sdk/constants';


export interface DataPool {
  categoryMap: Record<number, any>;
  mediaLibraryMap: Record<number, any>;
}

export enum GroupCombinator {
  And = 1,
  Or,
}

export interface IFilter {
  propertyId?: number;
  isReservedProperty?: boolean;
  group?: IGroup;
  operation?: SearchOperation;
  value?: string;
}

export interface IGroup {
  groups?: IGroup[];
  filters?: IFilter[];
  combinator: GroupCombinator;
}
