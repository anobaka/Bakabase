import type { ResourceSearchFilterGroup } from './components/FilterPanel/FilterGroupsPanel/models';
import type { ResourceSearchSortableProperty, ResourceTag } from '@/sdk/constants';

export type SearchForm = {
  group?: ResourceSearchFilterGroup;
  orders?: SearchFormOrderModel[];
  keyword?: string;
  page: number;
  pageSize: number;
  tags?: ResourceTag[];
};

export type SearchFormOrderModel = {
  property: ResourceSearchSortableProperty;
  asc: boolean;
};
