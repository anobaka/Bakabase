import type { SearchFilterGroup } from "@/components/ResourceFilter";
import type { ResourceSearchSortableProperty, ResourceTag } from "@/sdk/constants";

export type SearchForm = {
  group?: SearchFilterGroup;
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
