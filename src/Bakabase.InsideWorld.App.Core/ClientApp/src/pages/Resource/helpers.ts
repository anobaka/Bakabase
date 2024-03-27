import type { IGroup } from '@/pages/Resource/components/FilterPanel/components/FilterGroupsPanel/models';
import type {
  BakabaseInsideWorldModelsConfigsResourceResourceSearchOptionsV2,
  BakabaseInsideWorldModelsModelsAosResourceSearchFilterGroup,
} from '@/sdk/Api';
import type { ISearchForm } from '@/pages/Resource/models';

export const convertFilterGroupToDto = (group?: IGroup): BakabaseInsideWorldModelsModelsAosResourceSearchFilterGroup | undefined => {
  if (!group) {
    return undefined;
  }
  return {
    ...group,
    filters: group.filters?.map((filter) => {
      return {
        ...filter,
        value: JSON.stringify(filter.value),
      };
    }),
    groups: group.groups?.map(g => convertFilterGroupToDto(g)!),
  };
};

export const convertSearchFormFromDto = (sf: BakabaseInsideWorldModelsConfigsResourceResourceSearchOptionsV2): ISearchForm => {
  return {
    group: convertFilterGroupFromDto(sf.group),
    orders: sf.orders?.map((order) => {
      return {
        property: order.property!,
        asc: order.asc!,
      };
    }),
    pageIndex: sf.pageIndex!,
    pageSize: sf.pageSize!,
  };
};

export const convertFilterGroupFromDto = (group?: BakabaseInsideWorldModelsModelsAosResourceSearchFilterGroup): IGroup | undefined => {
  if (!group) {
    return undefined;
  }
  return {
    filters: group.filters?.map((filter) => {
      return {
        ...filter,
        value: filter.value == undefined ? undefined : JSON.parse(filter.value),
      };
    }),
    groups: group.groups?.map(g => convertFilterGroupFromDto(g)!),
    combinator: group.combinator!,
  };
};
