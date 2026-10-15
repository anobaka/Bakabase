import type { IProperty } from "./models";
import type { SearchForm } from "@/pages/resource/models";

import { PropertyType, SearchOperation, StandardValueType } from "@/sdk/constants";
import { GroupCombinator } from "@/components/ResourceFilter/models";
import { serializeStandardValue } from "@/components/StandardValue/helpers";

export type ReferenceProperty = Pick<
  IProperty,
  "id" | "pool" | "type" | "name" | "unifiedMediaLibraries"
>;

/** Use reference IDs, never labels, so case handling stays inside the property module. */
export function buildReferenceValueSearch(property: ReferenceProperty, value: string): SearchForm {
  const single = property.type === PropertyType.SingleChoice;

  return {
    page: 1,
    pageSize: 50,
    group: {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [
        {
          propertyId: property.id,
          propertyPool: property.pool,
          operation: single ? SearchOperation.Equals : SearchOperation.Contains,
          dbValue: serializeStandardValue(
            single ? value : [value],
            single ? StandardValueType.String : StandardValueType.ListString,
          ),
          disabled: false,
        },
      ],
    },
  };
}
