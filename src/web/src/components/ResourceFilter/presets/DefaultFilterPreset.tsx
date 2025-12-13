"use client";

import type { FilterConfig, SearchFilter } from "../models";
import type { IProperty } from "@/components/Property/models";

import BApi from "@/sdk/BApi";
import PropertySelector from "@/components/PropertySelector";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import ParentResourceValueRenderer from "../components/ParentResourceValueRenderer";
import { PropertyPool, InternalProperty } from "@/sdk/constants";

/**
 * 创建默认的 Filter 配置
 * 封装了 BApi.resource.* 和 BApi.options.* 的调用
 */
export function createDefaultFilterConfig(
  createPortal: (Component: any, props: any) => { destroy: () => void }
): FilterConfig {
  return {
    api: {
      getAvailableOperations: async (propertyPool, propertyId) => {
        const response = await BApi.resource.getSearchOperationsForProperty({
          propertyPool,
          propertyId,
        });
        return response.data || [];
      },

      getValueProperty: async (filter) => {
        if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
          return undefined;
        }
        const response = await BApi.resource.getFilterValueProperty(filter);
        return response.data;
      },

      saveRecentFilter: async (filter) => {
        if (filter.propertyPool && filter.propertyId && filter.operation) {
          await BApi.options.addRecentResourceFilter({
            propertyPool: filter.propertyPool,
            propertyId: filter.propertyId,
            operation: filter.operation,
            dbValue: filter.dbValue,
          });
        }
      },

      getRecentFilters: async () => {
        const response = await BApi.options.getRecentResourceFilters();
        return (response.data || []) as SearchFilter[];
      },
    },

    renderers: {
      openPropertySelector: (currentSelection, onSelect, onCancel) => {
        createPortal(PropertySelector, {
          v2: true,
          selection: currentSelection
            ? [{ id: currentSelection.id, pool: currentSelection.pool }]
            : undefined,
          onSubmit: async (selectedProperties: IProperty[]) => {
            const property = selectedProperties[0];
            if (property) {
              const response = await BApi.resource.getSearchOperationsForProperty({
                propertyPool: property.pool,
                propertyId: property.id,
              });
              const availableOperations = response.data || [];
              onSelect(property, availableOperations);
            }
          },
          onCancel,
          multiple: false,
          pool: PropertyPool.All,
          addable: false,
          editable: false,
        });
      },

      renderValueInput: (property, dbValue, bizValue, onValueChange, options) => {
        // Special handling for ParentResource property
        if (property.pool === PropertyPool.Internal && property.id === InternalProperty.ParentResource) {
          return (
            <ParentResourceValueRenderer
              property={property}
              bizValue={bizValue}
              dbValue={dbValue}
              defaultEditing={options?.defaultEditing}
              operation={options?.operation}
              size={options?.size ?? "sm"}
              variant={options?.variant ?? "light"}
              onValueChange={options?.isReadonly ? undefined : onValueChange}
              isReadonly={options?.isReadonly}
            />
          );
        }

        return (
          <PropertyValueRenderer
            bizValue={bizValue}
            dbValue={dbValue}
            defaultEditing={options?.defaultEditing}
            property={property}
            size={options?.size ?? "sm"}
            variant={options?.variant ?? "light"}
            onValueChange={options?.isReadonly ? undefined : onValueChange}
          />
        );
      },
    },
  };
}
