import type { IGroup } from './components/FilterPanel/FilterGroupsPanel/models';
import type { ResourceSearchSortableProperty } from '@/sdk/constants';

export interface ISearchForm {
  group?: IGroup;
  orders?: ISearchFormOrderModel[];
  keyword?: string;
  pageIndex: number;
  pageSize: number;
}

export interface ISearchFormOrderModel {
  property: ResourceSearchSortableProperty; asc: boolean;
}
