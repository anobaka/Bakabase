import type { FilterConfig, SearchFilter } from "@/components/ResourceFilter/models";
import type { IProperty } from "@/components/Property/models";

import React from "react";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import {
  getBizValueType,
  getDbValueType,
  isReferenceValueType,
} from "@/components/Property/PropertySystem";
import ChoiceResourceCount from "@/components/ResourceFilter/components/Filter/ChoiceResourceCount";
import styles from "@/components/ResourceFilter/components/Filter/value.module.scss";
import { createChoiceResourceCountsStore } from "@/components/ResourceFilter/hooks/choiceResourceCountsStore";
import { PropertyType, SearchOperation } from "@/sdk/constants";

const emptyOperations = [SearchOperation.IsNull, SearchOperation.IsNotNull];
const equalityOperations = [SearchOperation.Equals, SearchOperation.NotEquals];
const rangeOperations = [
  ...equalityOperations,
  SearchOperation.GreaterThan,
  SearchOperation.LessThan,
  SearchOperation.GreaterThanOrEquals,
  SearchOperation.LessThanOrEquals,
  ...emptyOperations,
];

/** A local catalog for visual fixtures; no mocked ID is sent to the application's database. */
export function getFixtureOperations(type: PropertyType): SearchOperation[] {
  switch (type) {
    case PropertyType.SingleChoice:
      return [...equalityOperations, ...emptyOperations, SearchOperation.In, SearchOperation.NotIn];
    case PropertyType.MultipleChoice:
    case PropertyType.Tags:
    case PropertyType.Multilevel:
      return [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.In,
        ...emptyOperations,
      ];
    case PropertyType.Boolean:
      return [...equalityOperations, ...emptyOperations];
    case PropertyType.Number:
    case PropertyType.Rating:
    case PropertyType.Percentage:
    case PropertyType.Date:
    case PropertyType.DateTime:
    case PropertyType.Time:
      return [...rangeOperations];
    default:
      return [
        ...equalityOperations,
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.StartsWith,
        SearchOperation.NotStartsWith,
        SearchOperation.EndsWith,
        SearchOperation.NotEndsWith,
        SearchOperation.Matches,
        SearchOperation.NotMatches,
        ...emptyOperations,
      ];
  }
}

export function getFixtureValueProperty(
  property: IProperty | undefined,
  operation?: SearchOperation,
) {
  if (!property) return undefined;
  let type = property.type;
  if (
    type === PropertyType.SingleChoice &&
    [SearchOperation.In, SearchOperation.NotIn].includes(operation!)
  ) {
    type = PropertyType.MultipleChoice;
  } else if (
    [
      PropertyType.SingleLineText,
      PropertyType.MultilineText,
      PropertyType.Link,
      PropertyType.Formula,
    ].includes(type)
  ) {
    type = PropertyType.SingleLineText;
  } else if (type === PropertyType.Rating) {
    type = PropertyType.Number;
  }

  return {
    ...property,
    type,
    dbValueType: getDbValueType(type),
    bizValueType: getBizValueType(type),
  };
}

type FixtureOption = { value: string; children?: FixtureOption[] };

export function getFixtureCounts(property: IProperty): Record<string, number> {
  const options = property.options as
    | {
        choices?: FixtureOption[];
        tags?: FixtureOption[];
        data?: FixtureOption[];
      }
    | undefined;
  const counts: Record<string, number> = {};
  let index = 0;
  const visit = (values: FixtureOption[]) =>
    values.forEach((option) => {
      counts[option.value] = index++ % 7 === 6 ? 0 : ((index * 11) % 47) + 1;
      if (option.children) visit(option.children);
    });
  visit(options?.choices ?? options?.tags ?? options?.data ?? []);

  return counts;
}

export function createResourceFilterFixtureConfig(
  properties: IProperty[],
  openPropertySelector: FilterConfig["renderers"]["openPropertySelector"],
): FilterConfig {
  let recent: SearchFilter[] = [];
  const find = (pool?: number, id?: number) =>
    properties.find((property) => property.pool === pool && property.id === id);
  const stores = new Map<string, ReturnType<typeof createChoiceResourceCountsStore>>();

  return {
    api: {
      getAvailableOperations: async (pool, id) => {
        const property = find(pool, id);

        return property ? getFixtureOperations(property.type) : [];
      },
      getAvailableOperationsByPropertyType: async (type) => getFixtureOperations(type),
      getValueProperty: async (filter) =>
        getFixtureValueProperty(
          find(filter.propertyPool, filter.propertyId) ?? filter.property ?? filter.valueProperty,
          filter.operation,
        ),
      saveRecentFilter: async (filter) => {
        recent = [filter, ...recent].slice(0, 10);
      },
      getRecentFilters: async () => recent,
    },
    renderers: {
      openPropertySelector,
      renderValueInput: (property, dbValue, bizValue, onValueChange, options) => {
        const reference = isReferenceValueType(property.type);
        const key = `${property.pool}:${property.id}`;
        let store = stores.get(key);
        if (!store) {
          store = createChoiceResourceCountsStore();
          store.publish({ counts: getFixtureCounts(property) });
          stores.set(key, store);
        }
        const counts = store.getSnapshot().counts ?? {};
        const disabledKeys = new Set(Object.keys(counts).filter((id) => counts[id] === 0));

        return (
          <div className={styles.value}>
            <PropertyValueRenderer
              property={property}
              dbValue={dbValue}
              bizValue={bizValue}
              onValueChange={onValueChange}
              defaultEditing={options?.defaultEditing}
              isEditing={options?.isEditing}
              isReadonly={options?.isReadonly}
              size={options?.size ?? "sm"}
              variant={options?.variant ?? "light"}
              disabledKeys={reference ? disabledKeys : undefined}
              renderOptionExtra={
                reference
                  ? (option) => <ChoiceResourceCount choiceId={option.value} store={store!} />
                  : undefined
              }
            />
          </div>
        );
      },
    },
  };
}
