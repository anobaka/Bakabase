import type { IProperty } from "@/components/Property/models";
import type { FilterConfig } from "@/components/ResourceFilter/models";
import type { SourceScope } from "./sourceProfiles";

import { profileSourceApi } from "./sourceProfiles";
import SourceParentResourceInput from "./components/SourceParentResourceInput";

import PropertySelector from "@/components/PropertySelector";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import BApi from "@/sdk/BApi";
import { InternalProperty, PropertyPool } from "@/sdk/constants";

/** Keep source property/choice IDs out of this device's history, counters and resource selectors. */
export function createSourceFilterConfig(
  scope: SourceScope,
  properties: IProperty[],
  createPortal: (Component: any, props: any) => { destroy: () => void },
): FilterConfig {
  const operations = async (property: IProperty | undefined) =>
    property
      ? ((await BApi.resource.getSearchOperationsByPropertyType({ propertyType: property.type }))
          .data ?? [])
      : [];

  return {
    api: {
      getAvailableOperations: (pool, id) =>
        operations(properties.find((p) => p.pool === pool && p.id === id)),
      getAvailableOperationsByPropertyType: async (propertyType) =>
        (await BApi.resource.getSearchOperationsByPropertyType({ propertyType })).data ?? [],
      getValueProperty: (filter) => profileSourceApi.valueProperty(scope, filter),
      getRecentFilters: async () => [],
      saveRecentFilter: async () => {},
    },
    renderers: {
      openPropertySelector: (selection, onSelect, onCancel, disabled) => {
        createPortal(PropertySelector, {
          properties,
          selection: selection ? [selection] : undefined,
          onSubmit: async (selected: IProperty[]) => {
            if (selected[0]) onSelect(selected[0], await operations(selected[0]));
          },
          onCancel,
          multiple: false,
          pool: PropertyPool.All,
          addable: false,
          editable: false,
          removable: false,
          v2: true,
          isDisabled: (property: IProperty) =>
            disabled?.some((p) => p.pool === property.pool && p.id === property.id) ?? false,
        });
      },
      renderValueInput: (property, dbValue, bizValue, onValueChange, options) => {
        const props = { property, dbValue, bizValue, onValueChange, ...options };

        return property.pool === PropertyPool.Internal &&
          property.id === InternalProperty.ParentResource ? (
          <SourceParentResourceInput {...props} scope={scope} />
        ) : (
          <PropertyValueRenderer {...props} />
        );
      },
    },
  };
}
