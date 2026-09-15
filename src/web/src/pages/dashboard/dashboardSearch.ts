import type { BakabaseServiceModelsInputResourceSearchInputModel } from "@/sdk/Api";

import {
  InternalProperty,
  PropertyPool,
  SearchCombinator,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";
import { serializeStandardValue } from "@/components/StandardValue/helpers";

export function dashboardResourceSearch(
  options: { keyword?: string; localOnly?: boolean; libraryId?: number } = {},
): BakabaseServiceModelsInputResourceSearchInputModel {
  const filters = [];

  if (options.localOnly) {
    filters.push({
      propertyPool: PropertyPool.Internal,
      propertyId: InternalProperty.HasLocalPath,
      operation: SearchOperation.Equals,
      dbValue: serializeStandardValue(true, StandardValueType.Boolean),
      disabled: false,
    });
  }
  if (options.libraryId !== undefined) {
    filters.push({
      propertyPool: PropertyPool.Internal,
      propertyId: InternalProperty.MediaLibraryV2Multi,
      operation: SearchOperation.In,
      dbValue: serializeStandardValue([String(options.libraryId)], StandardValueType.ListString),
      disabled: false,
    });
  }

  return {
    page: 1,
    pageSize: 100,
    keyword: options.keyword?.trim() || undefined,
    ...(filters.length
      ? { group: { combinator: SearchCombinator.And, filters, disabled: false } }
      : {}),
  };
}
