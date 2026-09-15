import type { BakabaseServiceModelsInputResourceSearchInputModel as ResourceSearch } from "@/sdk/Api";

import {
  PropertyPool,
  ResourceProperty,
  ResourceSearchSortableProperty,
  ResourceTag,
  SearchCombinator,
  SearchOperation,
} from "@/sdk/constants";

export type RecentResourceTab = "added" | "played" | "pinned";
export const RECENT_RESOURCE_LIMIT = 12;

/** The same criteria drive the dashboard preview and the full resource search. */
export function buildRecentResourceSearch(
  tab: RecentResourceTab,
  pageSize = RECENT_RESOURCE_LIMIT,
): ResourceSearch {
  return {
    page: 1,
    pageSize,
    orders: [
      {
        property:
          tab === "played"
            ? ResourceSearchSortableProperty.PlayedAt
            : ResourceSearchSortableProperty.AddDt,
        asc: false,
      },
    ],
    ...(tab === "played"
      ? {
          group: {
            combinator: SearchCombinator.And,
            disabled: false,
            filters: [
              {
                propertyPool: PropertyPool.Internal,
                propertyId: ResourceProperty.PlayedAt,
                operation: SearchOperation.IsNotNull,
                disabled: false,
              },
            ],
          },
        }
      : {}),
    ...(tab === "pinned" ? { tags: [ResourceTag.Pinned] } : {}),
  };
}
