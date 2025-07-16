import type {
  IBulkModificationFilter,
  IBulkModificationFilterGroup,
} from '@/pages/BulkModification/components/BulkModification';

export const buildFilterReactKey = (filter: IBulkModificationFilter) => {
  return `${filter.property}-${filter.propertyKey}-${filter.operation}-${filter.target}`;
};

export const buildFilterGroupReactKey = (group: IBulkModificationFilterGroup) => {
  return `${group.groups?.map(g => buildFilterGroupReactKey(g)).join('|')}-${group.filters?.map(f => buildFilterReactKey(f)).join('|')}-${group.operation}`;
};
