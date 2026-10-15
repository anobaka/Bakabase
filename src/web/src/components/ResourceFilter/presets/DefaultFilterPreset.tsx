"use client";

import type { FilterConfig, SearchFilter } from "../models";
import type { IProperty } from "@/components/Property/models";
import type { ManagementSource } from "@/features/management/api";
import type { FilterOwner } from "../models";

import FilterValueRenderer from "../components/Filter/FilterValueRenderer";

import BApi from "@/sdk/BApi";
import PropertySelector from "@/components/PropertySelector";
import { InternalProperty, PropertyPool, ResourceProperty } from "@/sdk/constants";
import { createResourceApi } from "@/features/management/resourceApi";
import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import { canReadSourceMetadata } from "@/features/management/metadata";

const ownerLocalProperties = new Set<number>([
  ResourceProperty.ParentResource,
  InternalProperty.Resource,
  ResourceProperty.MediaLibraryV2,
  ResourceProperty.MediaLibraryV2Multi,
  ResourceProperty.CollectionMulti,
]);
const needsOwner = (property: IProperty) =>
  property.pool === PropertyPool.Custom ||
  (property.pool === PropertyPool.Internal && ownerLocalProperties.has(property.id));
const sameOwner = (a?: FilterOwner, b?: FilterOwner) =>
  a?.serverId === b?.serverId && a?.libraryEpoch === b?.libraryEpoch;

/** Keep one slow/offline device from hiding every other device's property catalog. */
async function loadBySource<T>(
  sources: ManagementSource[],
  load: (source: ManagementSource) => Promise<T>,
) {
  const results: PromiseSettledResult<T>[] = [];

  for (let offset = 0; offset < sources.length; offset += 4) {
    results.push(...(await Promise.allSettled(sources.slice(offset, offset + 4).map(load))));
  }

  return results;
}

/**
 * 创建默认的 Filter 配置
 * 封装了 BApi.resource.* 和 BApi.options.* 的调用
 */
export function createDefaultFilterConfig(
  createPortal: (Component: any, props: any) => { destroy: () => void },
  api: typeof BApi = BApi,
  sources?: ManagementSource[],
  owner?: ManagementSource,
): FilterConfig {
  const mixed = !owner && !!sources?.some((source) => !source.isLocal);
  const findOwner = (identity?: FilterOwner) => {
    if (!identity) return owner;
    const found = [owner, ...(sources ?? [])].find(
      (source) => source && sameOwner(source, identity),
    );

    if (!found)
      throw new Error("The device or its library identity has changed. Refresh the device list.");

    return found;
  };
  const apiFor = (identity?: FilterOwner) =>
    identity ? createResourceApi(findOwner(identity)) : api;
  const pendingCatalogs = new Map<string, Promise<IProperty[]>>();

  return {
    api: {
      getProperty: async (filter) => {
        if (filter.propertyPool == null || filter.propertyId == null) return undefined;
        const selectedOwner = findOwner(filter.owner);
        const key = JSON.stringify([
          selectedOwner?.serverId,
          selectedOwner?.libraryEpoch,
          filter.propertyPool,
        ]);
        let pending = pendingCatalogs.get(key);
        if (!pending) {
          pending = apiFor(filter.owner)
            .property.getPropertiesByPool(filter.propertyPool)
            .then((response) => {
              if (response.code) throw new Error(response.message || String(response.code));
              return (response.data ?? []) as IProperty[];
            })
            .finally(() => pendingCatalogs.delete(key));
          pendingCatalogs.set(key, pending);
        }
        const property = (await pending).find(
          (property) => property.id === filter.propertyId && property.pool === filter.propertyPool,
        );
        return property
          ? { ...property, ...(selectedOwner ? { owner: selectedOwner } : {}) }
          : undefined;
      },

      getAvailableOperations: async (propertyPool, propertyId) => {
        const response = await api.resource.getSearchOperationsForProperty({
          propertyPool,
          propertyId,
        });

        return response.data || [];
      },

      getAvailableOperationsByPropertyType: async (propertyType) => {
        const response = await api.resource.getSearchOperationsByPropertyType({
          propertyType,
        });

        return response.data || [];
      },

      getValueProperty: async (filter) => {
        if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
          return undefined;
        }
        const selectedOwner = findOwner(filter.owner);
        const response = await apiFor(filter.owner).resource.getFilterValueProperty({
          propertyPool: filter.propertyPool,
          propertyId: filter.propertyId,
          operation: filter.operation,
        });

        return response.data
          ? ({ ...response.data, ...(selectedOwner ? { owner: selectedOwner } : {}) } as IProperty)
          : undefined;
      },

      saveRecentFilter: async (filter) => {
        const selectedOwner = findOwner(filter.owner);
        // Offline filtering remains usable; recents are an optional owner-side preference.
        if (selectedOwner && (!selectedOwner.available || selectedOwner.metadata?.denied)) return;
        if (filter.propertyPool && filter.propertyId && filter.operation) {
          await apiFor(filter.owner).options.addRecentResourceFilter({
            propertyPool: filter.propertyPool,
            propertyId: filter.propertyId,
            operation: filter.operation,
            dbValue: filter.dbValue,
          });
        }
      },

      getRecentFilters: async () => {
        if (mixed) {
          const results = await loadBySource(
            (sources ?? []).filter((source) => source.available),
            async (source) => {
              const response = await createResourceApi(source).options.getRecentResourceFilters();

              return (response.data ?? []).map((filter) => ({
                ...filter,
                owner: { serverId: source.serverId, libraryEpoch: source.libraryEpoch },
              }));
            },
          );
          const groups = results.map((result) => {
            if (result.status === "rejected") throw result.reason;
            return result.value;
          });

          return groups.flat() as SearchFilter[];
        }
        const response = await api.options.getRecentResourceFilters();

        return (response.data || []).map((filter) => ({
          ...filter,
          ...(owner ? { owner } : {}),
        })) as SearchFilter[];
      },
    },

    renderers: {
      openPropertySelector: async (currentSelection, onSelect, onCancel, disabledProperties) => {
        let catalog: IProperty[] | undefined;
        const unavailableSources: { name: string; message?: string }[] = [];

        if (mixed) {
          const availableSources = (sources ?? []).filter(canReadSourceMetadata);
          unavailableSources.push(
            ...(sources ?? [])
              .filter((source) => !canReadSourceMetadata(source))
              .map((source) => ({ name: source.name, message: source.error })),
          );
          const results = await loadBySource(availableSources, async (source) => {
            const response = await createResourceApi(source).property.getPropertiesByPool(
              PropertyPool.All,
            );
            if (response.code) throw new Error(response.message || String(response.code));

            return { source, properties: (response.data ?? []) as IProperty[] };
          });
          const groups: { source: ManagementSource; properties: IProperty[] }[] = [];
          results.forEach((result, index) => {
            if (result.status === "fulfilled") groups.push(result.value);
            else
              unavailableSources.push({
                name: availableSources[index].name,
                message:
                  result.reason instanceof Error ? result.reason.message : String(result.reason),
              });
          });
          const commonSource = groups.find(({ source }) => source.isLocal) ?? groups[0];

          catalog = [
            ...(commonSource?.properties.filter((property) => !needsOwner(property)) ?? []),
            ...groups.flatMap(({ source, properties }) =>
              properties.filter(needsOwner).map((property) => ({ ...property, owner: source })),
            ),
          ];
        }
        createPortal(PropertySelector, {
          v2: true,
          properties: catalog,
          unavailableSources,
          selection: currentSelection
            ? [
                {
                  id: currentSelection.id,
                  pool: currentSelection.pool,
                  ...(currentSelection.owner ? { owner: currentSelection.owner } : {}),
                },
              ]
            : undefined,
          onSubmit: async (selectedProperties: IProperty[]) => {
            const property = selectedProperties[0];

            if (property) {
              const selectedOwner = property.owner ?? owner;
              const response = await apiFor(selectedOwner).resource.getSearchOperationsForProperty({
                propertyPool: property.pool,
                propertyId: property.id,
              });
              const availableOperations = response.data || [];

              onSelect(
                selectedOwner ? { ...property, owner: selectedOwner } : property,
                availableOperations,
              );
            }
          },
          onCancel,
          multiple: false,
          pool: PropertyPool.All,
          addable: false,
          editable: false,
          isDisabled: disabledProperties?.length
            ? (p: IProperty) =>
                disabledProperties.some(
                  (dp) => dp.id === p.id && dp.pool === p.pool && sameOwner(dp.owner, p.owner),
                )
            : undefined,
          pinnedProperties: [
            { id: ResourceProperty.MediaLibraryV2Multi, pool: PropertyPool.Internal },
            { id: ResourceProperty.CollectionMulti, pool: PropertyPool.Internal },
            { id: ResourceProperty.Source, pool: PropertyPool.Internal },
            { id: ResourceProperty.HasLocalPath, pool: PropertyPool.Internal },
          ],
        });
      },

      renderValueInput: (property, dbValue, bizValue, onValueChange, options) => {
        const input = (
          <FilterValueRenderer
            bizValue={bizValue}
            dbValue={dbValue}
            defaultEditing={options?.defaultEditing}
            isEditing={options?.isEditing}
            isReadonly={options?.isReadonly}
            property={property}
            size={options?.size ?? "sm"}
            variant={options?.variant ?? "light"}
            onValueChange={onValueChange}
          />
        );

        return property.owner ? (
          <ResourceOwnerProvider source={property.owner}>{input}</ResourceOwnerProvider>
        ) : (
          input
        );
      },
    },
  };
}
