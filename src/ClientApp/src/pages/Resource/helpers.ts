import type { IFilter, IGroup } from './components/FilterPanel/FilterGroupsPanel/models';
import type {
  BakabaseAbstractionsModelsDtoResourceSearchDto,
  BakabaseInsideWorldModelsModelsAosResourceSearchFilterGroup,
} from '@/sdk/Api';
import type { ISearchForm } from '@/pages/Resource/models';

export const convertFilterGroupToDto = (group?: IGroup): BakabaseInsideWorldModelsModelsAosResourceSearchFilterGroup | undefined => {
  if (!group) {
    return undefined;
  }
  return {
    ...group,
    filters: group.filters,
    groups: group.groups?.map(g => convertFilterGroupToDto(g)!),
  };
};

export const convertSearchFormFromDto = (sf: BakabaseAbstractionsModelsDtoResourceSearchDto): ISearchForm => {
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
    filters: group.filters?.filter(f => f) as IFilter[],
    groups: group.groups?.map(g => convertFilterGroupFromDto(g)!),
    combinator: group.combinator!,
  };
};
