'use strict';

import type { IProperty } from '@/components/Property/models';
import type { PropertyPool, SearchOperation } from '@/sdk/constants';

export enum GroupCombinator {
  And = 1,
  Or,
}

export type ResourceSearchFilter = {
  propertyId?: number;
  propertyPool?: PropertyPool;
  operation?: SearchOperation;
  dbValue?: string;
  bizValue?: string;
  availableOperations?: SearchOperation[];
  property?: IProperty;
  valueProperty?: IProperty;
  disabled: boolean;
};

export type ResourceSearchFilterGroup = {
  groups?: ResourceSearchFilterGroup[];
  filters?: ResourceSearchFilter[];
  combinator: GroupCombinator;
  disabled: boolean;
};
