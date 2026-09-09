import type { CollectionModel } from "@/stores/collections";

import {
  InternalProperty,
  PropertyPool,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";
import { serializeStandardValue } from "@/components/StandardValue";

/**
 * The same collection, asked for on the resource page. Membership is an ordinary property, so
 * "show me these" is an ordinary filter — which is the whole point of having made it one.
 */
export const buildCollectionSearch = (collection: CollectionModel) => ({
  group: {
    combinator: 1,
    disabled: false,
    filters: [
      {
        propertyPool: PropertyPool.Internal,
        propertyId: InternalProperty.CollectionMulti,
        operation: SearchOperation.In,
        dbValue: serializeStandardValue([String(collection.id)], StandardValueType.ListString),
        bizValue: serializeStandardValue([collection.name], StandardValueType.ListString),
        disabled: false,
      },
    ],
  },
  page: 1,
  pageSize: 100,
});

/** What is left to get: everything counted that is neither had nor on its way. */
export const missingCount = (collection: CollectionModel): number => {
  const p = collection.progress;

  if (!p) return 0;

  return Math.max(0, p.total - p.owned - p.acquiring);
};

export const percent = (collection: CollectionModel): number =>
  Math.round((collection.progress?.ratio ?? 1) * 100);
