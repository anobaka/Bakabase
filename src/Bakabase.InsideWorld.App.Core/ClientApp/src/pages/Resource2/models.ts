import type { IGroup } from '@/pages/Resource2/components/FilterPanel/components/FilterGroupsPanel/models';
import type { BakabaseInsideWorldModelsConfigsResourceResourceSearchOptionsOrderModel } from '@/sdk/Api';
import type { ResourceSearchOrder } from '@/sdk/constants';

export interface ISearchForm {
  group?: IGroup;
  orders?: {order: ResourceSearchOrder; asc: boolean}[];
  pageIndex: number;
  pageSize: number;
}
