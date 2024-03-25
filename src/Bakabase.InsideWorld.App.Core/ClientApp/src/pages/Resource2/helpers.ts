import type { IGroup } from '@/pages/Resource2/components/FilterPanel/components/FilterGroupsPanel/models';

export const convertGroupToDto = (group?: IGroup) => {
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
    groups: group.groups?.map(convertGroupToDto),
  };
};
