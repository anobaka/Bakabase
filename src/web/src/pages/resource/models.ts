import type { SearchFilterGroup } from "@/components/ResourceFilter";
import type { ResourceSearchSortableProperty, ResourceTag } from "@/sdk/constants";
import type { ManagementSource } from "@/features/management/api";

export type SearchForm = {
  /** Owner-bound predicates from a resource/property editor must never match another device's IDs. */
  owner?: Pick<ManagementSource, "serverId" | "libraryEpoch">;
  /** Undefined includes every managed source; normalize API null to undefined. An empty list selects none. */
  deviceIds?: string[];
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
