import type { SearchOperation } from '@/sdk/constants';

export enum GroupCombinator {
  And = 1,
  Or,
}

export interface IFilter {
  propertyId?: number;
  propertyName?: string;
  isReservedProperty?: boolean;
  group?: IGroup;
  operation?: SearchOperation;
}

export interface IGroup {
  groups?: IGroup[];
  filters?: IFilter[];
  combinator: GroupCombinator;
}
