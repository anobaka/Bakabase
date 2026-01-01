/**
 * Filter input model for API calls
 * DbValue is serialized string format
 */
export type SearchFilterInputModel = {
  propertyPool?: number;
  propertyId?: number;
  operation?: number;
  dbValue?: string;
  disabled: boolean;
};

/**
 * Filter group input model for API calls
 */
export type SearchFilterGroupInputModel = {
  combinator: number;
  groups?: SearchFilterGroupInputModel[];
  filters?: SearchFilterInputModel[];
  disabled: boolean;
};

/**
 * Search input model for API calls
 */
export type ResourceSearchInputModel = {
  group?: SearchFilterGroupInputModel;
  orders?: any[];
  keyword?: string;
  pageSize: number;
  page: number;
  tags?: any[];
};

// Generic filter type that accepts both frontend SearchFilter and backend response format
type FilterLike = {
  propertyPool?: number;
  propertyId?: number;
  operation?: number;
  dbValue?: string;
  disabled?: boolean;
};

// Generic filter group type that accepts both formats
type FilterGroupLike = {
  combinator?: number;
  groups?: FilterGroupLike[];
  filters?: FilterLike[];
  disabled?: boolean;
};

/**
 * Convert any filter-like object to SearchFilterInputModel
 * Ensures dbValue is in string format
 */
export const toFilterInputModel = (filter: FilterLike): SearchFilterInputModel => {
  return {
    propertyPool: filter.propertyPool,
    propertyId: filter.propertyId,
    operation: filter.operation,
    dbValue: filter.dbValue,
    disabled: filter.disabled ?? false,
  };
};

/**
 * Convert any filter-group-like object to SearchFilterGroupInputModel
 */
export const toFilterGroupInputModel = (
  group: FilterGroupLike
): SearchFilterGroupInputModel => {
  return {
    combinator: group.combinator ?? 1,
    groups: group.groups?.map(toFilterGroupInputModel),
    filters: group.filters?.map(toFilterInputModel),
    disabled: group.disabled ?? false,
  };
};

/**
 * Convert search data to ResourceSearchInputModel for API calls
 * Accepts both frontend search model and backend response format
 */
export const toSearchInputModel = (search: {
  group?: FilterGroupLike;
  orders?: any[];
  keyword?: string;
  pageSize?: number;
  page?: number;
  pageIndex?: number;
  tags?: any[];
  skipCount?: number;
}): ResourceSearchInputModel => {
  return {
    group: search.group ? toFilterGroupInputModel(search.group) : undefined,
    orders: search.orders,
    keyword: search.keyword,
    pageSize: search.pageSize ?? 100,
    page: search.page ?? search.pageIndex ?? 1,
    tags: search.tags,
  };
};
