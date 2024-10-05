import type { ResourceSearchFilterGroup } from './components/FilterPanel/FilterGroupsPanel/models';
import type {
  BakabaseServiceModelsInputResourceSearchFilterGroupInputModel,
} from '@/sdk/Api';

export const convertFilterGroupToDto = (group?: ResourceSearchFilterGroup): BakabaseServiceModelsInputResourceSearchFilterGroupInputModel | undefined => {
  if (!group) {
    return undefined;
  }
  return {
    ...group,
    filters: group.filters,
    groups: group.groups?.map(g => convertFilterGroupToDto(g)!),
  };
};
