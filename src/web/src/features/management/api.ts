import BApi from "@/sdk/BApi";
import envConfig from "@/config/env";
import { PropertyPool, ResourceAdditionalItem } from "@/sdk/constants";
import type { ContentType } from "@/sdk/Api";
import {
  canReadSourceMetadata,
  metadataReadError,
  metadataReadOperations,
  type ManagedMetadataStatus,
} from "./metadata";

// List and editor metadata must not expand every playable file or cover in a large resource.
const resourceMetadataItems = (ResourceAdditionalItem.Properties |
  ResourceAdditionalItem.Alias |
  ResourceAdditionalItem.DisplayName |
  ResourceAdditionalItem.MediaLibraryName) as ResourceAdditionalItem;

export interface ManagementSource {
  serverId: string;
  name: string;
  libraryEpoch: string;
  isLocal: boolean;
  available: boolean;
  errorCode?: string;
  error?: string;
  contractVersion: number;
  metadata?: ManagedMetadataStatus;
}
export interface Envelope<T> {
  code?: number;
  message?: string;
  data?: T;
  totalCount?: number;
  pageIndex?: number;
  pageSize?: number;
}
export type Operation =
  | "resourceSearch"
  | "resourceGet"
  | "properties"
  | "customProperties"
  | "customPropertyAdd"
  | "customPropertyPut"
  | "customPropertyDelete"
  | "propertyValueResourceCounts"
  | "libraries"
  | "libraryAdd"
  | "libraryPut"
  | "libraryPatch"
  | "libraryDelete"
  | "resourcePropertyPut"
  | "resourceLibrariesPut"
  | "resourceMappings"
  | "resourcePin"
  | "resourcePlayableItems"
  | "resourceCovers"
  | "resourceDisplayOptions"
  | "resourceDisplayOptionsPatch"
  | "resourceBrowserFiles"
  | "resourceMarkPlayed";
export interface RequestInput {
  id?: number;
  ids?: number[];
  body?: unknown;
  query?: Record<string, string[]>;
}
export const sourceKey = (source: ManagementSource) =>
  JSON.stringify([source.serverId, source.libraryEpoch]);
export const ownedResourceKey = (source: ManagementSource, id: number) =>
  JSON.stringify([source.serverId, source.libraryEpoch, id]);

export async function getManagementSources(signal?: AbortSignal): Promise<ManagementSource[]> {
  const response = await fetch(`${envConfig.apiEndpoint}/federation/local/management/sources`, {
    signal,
    cache: "no-store",
  });

  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  const body = await response.json();

  if (!Array.isArray(body.sources)) throw new Error("Invalid management sources");

  return body.sources;
}

export async function setMetadataSyncPaused(source: ManagementSource, paused: boolean) {
  const response = await BApi.request({
    path: `/federation/local/management/${encodeURIComponent(source.serverId)}/metadata/control`,
    method: "POST",
    type: "application/json" as ContentType,
    format: "json",
    body: { libraryEpoch: source.libraryEpoch, paused },
  });
  if ((response as Envelope<unknown>)?.code)
    throw new Error((response as Envelope<unknown>).message || "Unable to update sync settings");
}

/** An immutable binding to the owner. No global SDK URL, selection or credentials are changed. */
export function ownerClient(source: ManagementSource) {
  const owner = { ...source };

  return async function request<T>(
    operation: Operation,
    input: RequestInput = {},
  ): Promise<Envelope<T>> {
    if (
      !owner.libraryEpoch ||
      (metadataReadOperations.has(operation)
        ? !canReadSourceMetadata(owner)
        : !owner.available || owner.metadata?.denied)
    )
      throw new Error(metadataReadError(owner));
    let result: Envelope<T>;

    if (owner.isLocal) {
      // The local path preserves existing SDK behavior and the original all-in-one APIs.
      const body = input.body as any;
      const id = input.id!;
      const calls: Record<Operation, () => Promise<any>> = {
        resourceSearch: () =>
          BApi.resource.searchResources(body, {
            saveSearch: false,
            additionalItems: resourceMetadataItems,
          }),
        resourceMarkPlayed: () =>
          BApi.resource.markResourceAsPlayed(id, { item: input.query?.item?.[0] }),
        resourcePlayableItems: () => BApi.resource.getResourcePlayableItems(id),
        resourceDisplayOptions: () =>
          BApi.request({
            path: "/federation/local/management/resource-display-options",
            method: "GET",
            format: "json",
          }),
        resourceDisplayOptionsPatch: () => BApi.options.patchResourceOptions(input.body as any),
        resourceCovers: async () => {
          const response = await BApi.resource.getResourcesByKeys({
            ids: [id],
            additionalItems: ResourceAdditionalItem.Cover,
          });

          return { ...response, data: response.data?.[0]?.covers ?? [] };
        },
        resourceBrowserFiles: () =>
          BApi.request({
            path: `/management/library/resources/${id}/browser-files`,
            method: "GET",
            format: "json",
            query: {
              offset: input.query?.offset?.[0],
              pageSize: input.query?.pageSize?.[0],
            },
          }),
        resourceGet: () =>
          BApi.resource.getResourcesByKeys({
            ids: input.ids,
            additionalItems: resourceMetadataItems,
          }),
        properties: () => BApi.property.getPropertiesByPool(PropertyPool.All),
        customProperties: () => BApi.customProperty.getAllCustomProperties(),
        customPropertyAdd: () => BApi.customProperty.addCustomProperty(body),
        customPropertyPut: () => BApi.customProperty.putCustomProperty(id, body),
        customPropertyDelete: () => BApi.customProperty.removeCustomProperty(id),
        propertyValueResourceCounts: () =>
          BApi.property.getPropertyValueResourceCounts(
            Number(input.query?.pool?.[0]) as PropertyPool,
            id,
            body,
          ),
        libraries: () => BApi.mediaLibraryV2.getAllMediaLibraryV2(),
        libraryAdd: () => BApi.mediaLibraryV2.addMediaLibraryV2(body),
        libraryPut: () => BApi.mediaLibraryV2.putMediaLibraryV2(id, body),
        libraryPatch: () => BApi.mediaLibraryV2.patchMediaLibraryV2(id, body),
        libraryDelete: () => BApi.mediaLibraryV2.deleteMediaLibraryV2(id),
        resourcePropertyPut: () => BApi.resource.putResourcePropertyValue(id, body),
        resourceLibrariesPut: () => BApi.resource.setResourceMediaLibraries(body),
        resourceMappings: () => BApi.resource.getResourceMediaLibraryMappings(body),
        resourcePin: () => BApi.resource.pinResource(id, { pin: input.query?.pin?.[0] === "true" }),
      };

      result = await calls[operation]();
    } else {
      const response = await fetch(
        `${envConfig.apiEndpoint}/federation/local/management/${encodeURIComponent(owner.serverId)}/request`,
        {
          method: "POST",
          cache: "no-store",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ ...input, libraryEpoch: owner.libraryEpoch, operation }),
        },
      );
      const payload = await response.json().catch(() => undefined);

      if (!response.ok)
        throw new Error(
          payload?.message ||
            payload?.error?.message ||
            `${response.status} ${response.statusText}`,
        );
      result = payload;
    }
    if (result?.code) throw new Error(result.message || String(result.code));

    return result;
  };
}
export type OwnerClient = ReturnType<typeof ownerClient>;
