import type { IGroup } from '@/pages/Resource/components/FilterPanel/components/FilterGroupsPanel/models';
import type { ResourceSearchSortableProperty } from '@/sdk/constants';

export interface ISearchForm {
  group?: IGroup;
  orders?: ISearchFormOrderModel[];
  pageIndex: number;
  pageSize: number;
}

export interface ISearchFormOrderModel {
  property: ResourceSearchSortableProperty; asc: boolean;
}
