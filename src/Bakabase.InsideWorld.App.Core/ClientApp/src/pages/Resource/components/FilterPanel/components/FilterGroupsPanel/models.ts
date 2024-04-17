import type { SearchOperation, StandardValueType } from '@/sdk/constants';

export enum GroupCombinator {
  And = 1,
  Or,
}

export interface IFilter {
  propertyId?: number;
  isReservedProperty?: boolean;
  group?: IGroup;
  operation?: SearchOperation;
  value?: any;
}

export interface IGroup {
  groups?: IGroup[];
  filters?: IFilter[];
  combinator: GroupCombinator;
}
