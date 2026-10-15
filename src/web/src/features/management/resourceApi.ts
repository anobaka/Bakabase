import type { Api, ApiConfig, ContentType, FullRequestParams } from "@/sdk/Api";
import type { ManagementSource } from "./api";

import toast from "react-hot-toast";
import i18next from "i18next";

import { sourceKey } from "./api";
import { canReadSourceMetadata, metadataReadError, metadataReadOperations } from "./metadata";

import BApi from "@/sdk/BApi";
import envConfig from "@/config/env";

type OwnerChangeListener = (resourceIds?: readonly number[]) => void;
const ownerChangeListeners = new Map<string, Set<OwnerChangeListener>>();

export function subscribeResourceOwnerChanges(
  source: Readonly<ManagementSource>,
  listener: OwnerChangeListener,
) {
  const key = sourceKey(source);
  const listeners = ownerChangeListeners.get(key) ?? new Set<OwnerChangeListener>();

  listeners.add(listener);
  ownerChangeListeners.set(key, listeners);

  return () => {
    listeners.delete(listener);
    if (!listeners.size) ownerChangeListeners.delete(key);
  };
}

export function notifyResourceOwnerChanged(
  source: Readonly<ManagementSource>,
  resourceIds?: readonly number[],
) {
  for (const listener of ownerChangeListeners.get(sourceKey(source)) ?? []) {
    try {
      listener(resourceIds);
    } catch (error) {
      console.error("Failed to refresh managed resource view", error);
    }
  }
}

const readOperations = new Set([
  "resourceSearch",
  "propertyValueResourceCounts",
  "resourceMappings",
  "previewCustomPropertyTypeConversion",
  "previewCollectionRule",
  "findDataCardByIdentity",
]);

type Route = readonly [method: string, template: string, operation: string];

/** The generated SDK still supplies request shapes; only these library operations may cross owners. */
const routes: readonly Route[] = [
  ["POST", "/resource/search", "resourceSearch"],
  ["GET", "/resource/keys", "resourceGet"],
  ["GET", "/resource/{id}/playable-items", "resourcePlayableItems"],
  ["GET", "/resource/{id}/previewer", "getResourceDataForPreviewer"],
  ["GET", "/resource/{id}/effective-player-options", "getResourceEffectivePlayerOptions"],
  ["GET", "/resource/{id}/source-links", "getResourceSourceLinks"],
  ["GET", "/resource/search/keyword-recommendation", "getResourceSearchKeywordRecommendation"],
  ["GET", "/property/pool/{pool}", "properties"],
  [
    "GET",
    "/property/property-types-for-manually-setting-value",
    "getAvailablePropertyTypesForManuallySettingValue",
  ],
  ["GET", "/property/pool/{pool}/id/{id}/biz-value", "getPropertyBizValue"],
  ["GET", "/property/pool/{pool}/id/{id}/db-value", "getPropertyDbValue"],
  ["POST", "/property/pool/{pool}/id/{id}/value-resource-counts", "propertyValueResourceCounts"],
  ["GET", "/custom-property/all", "customProperties"],
  ["GET", "/custom-property/ids", "getCustomPropertyByKeys"],
  ["GET", "/custom-property/conversion-rule", "getCustomPropertyConversionRules"],
  [
    "POST",
    "/custom-property/{id}/{targetType}/conversion-preview",
    "previewCustomPropertyTypeConversion",
  ],
  ["PUT", "/custom-property/{id}/{type}", "changeCustomPropertyType"],
  ["PUT", "/custom-property/order", "sortCustomProperties"],
  ["POST", "/custom-property", "customPropertyAdd"],
  ["PUT", "/custom-property/{id}", "customPropertyPut"],
  ["DELETE", "/custom-property/{id}", "customPropertyDelete"],
  ["GET", "/media-library-v2", "libraries"],
  ["POST", "/media-library-v2", "libraryAdd"],
  ["PUT", "/media-library-v2/{id}", "libraryPut"],
  ["PATCH", "/media-library-v2/{id}", "libraryPatch"],
  ["DELETE", "/media-library-v2/{id}", "libraryDelete"],
  ["PUT", "/resource/{id}/property-value", "resourcePropertyPut"],
  ["PUT", "/resource/media-libraries", "resourceLibrariesPut"],
  ["POST", "/resource/media-library-mappings", "resourceMappings"],
  ["GET", "/resource/{id}/property-value-scope-preference", "resourceScopePreferencesGet"],
  ["PUT", "/resource/{id}/property-value-scope-preference", "resourceScopePreferencesPut"],
  ["DELETE", "/resource/{id}/property-value-scope-preference", "resourceScopePreferencesDelete"],
  ["PUT", "/resource/{id}/pin", "resourcePin"],
  ["POST", "/resource/{id}/played-at", "resourceMarkPlayed"],
  ["DELETE", "/resource/{id}/played-at", "markResourceAsNotPlayed"],
  ["PUT", "/resource/bulk/property-value", "bulkPutResourcePropertyValue"],
  ["POST", "/resource/bulk-delete", "bulkDeleteResources"],
  ["GET", "/resource/{id}/conflicts", "getResourceConflicts"],
  ["POST", "/resource/merge", "mergeResources"],
  ["PUT", "/resource/transfer", "transferResourceData"],
  ["GET", "/resource/paths", "searchResourcePaths"],
  ["GET", "/resource/{id}/hierarchy-context", "getResourceHierarchyContext"],
  ["GET", "/resource/collections", "getCollectionIdsByResourceIds"],
  ["GET", "/resource/filter-value-property", "getFilterValueProperty"],
  ["GET", "/resource/search-operation/by-type", "getSearchOperationsByPropertyType"],
  ["GET", "/resource/search-operation", "getSearchOperationsForProperty"],
  ["GET", "/resource/saved-search", "getSavedSearch"],
  ["POST", "/resource/saved-search", "saveNewResourceSearch"],
  ["PUT", "/resource/saved-search", "putSavedSearchName"],
  ["DELETE", "/resource/saved-search", "deleteSavedSearch"],
  ["PUT", "/resource/saved-search/display-mode", "putSavedSearchDisplayMode"],
  ["POST", "/resource/placeholder", "createPlaceholderResources"],
  ["POST", "/resource/{id}/materialize", "materializeResource"],
  ["GET", "/resource/{id}/enhancement", "getResourceEnhancements"],
  ["GET", "/enhancer/descriptor", "getAllEnhancerDescriptors"],
  ["POST", "/resource/{id}/enhancer/{enhancerId}/enhancement", "enhanceResourceByEnhancer"],
  [
    "POST",
    "/resource/{id}/enhancer/{enhancerId}/enhancement/apply",
    "applyEnhancementContextDataForResourceByEnhancer",
  ],
  ["PUT", "/resource/{id}/cover", "saveCover"],
  ["DELETE", "/resources/enhancements", "deleteEnhancementsByResources"],
  ["POST", "/resource/{id}/acquisition-leads", "addResourceAcquisitionLead"],
  ["GET", "/resource/{id}/acquisition-leads", "getResourceAcquisitionLeads"],
  ["DELETE", "/resource/{id}/acquisition-leads/{leadId}", "deleteResourceAcquisitionLead"],
  ["GET", "/acquisition/candidates/{id}", "getAcquisitionCandidate"],
  ["POST", "/acquisition", "createAcquisition"],
  ["POST", "/ai/resource/{id}/translate", "aiTranslateResourceProperties"],
  ["POST", "/cache/resource/{id}/refresh", "refreshResourceCache"],
  ["POST", "/cache/resources/refresh", "refreshResourcesCache"],
  ["GET", "/collection", "getAllCollections"],
  ["GET", "/collection/{id}", "getCollection"],
  ["POST", "/collection", "addCollection"],
  ["PUT", "/collection/{id}", "putCollection"],
  ["DELETE", "/collection/{id}", "deleteCollection"],
  ["GET", "/collection/{id}/members", "searchCollectionMembers"],
  ["GET", "/collection/{id}/memberships", "getCollectionMemberships"],
  ["PUT", "/collection/{id}/members/{resourceId}/ignored", "setCollectionMemberIgnored"],
  ["POST", "/collection/{id}/members/placeholder", "addCollectionPlaceholderMember"],
  ["POST", "/collection/rule/preview", "previewCollectionRule"],
  ["POST", "/collection/{id}/acquire-missing", "acquireMissingCollectionMembers"],
  ["GET", "/subscription", "searchSubscriptions"],
  ["GET", "/subscription/providers", "getSubscriptionProviders"],
  ["POST", "/subscription", "addSubscription"],
  ["PATCH", "/subscription/{id}", "patchSubscription"],
  ["POST", "/subscription/{id}/run", "runSubscriptionCheck"],
  ["GET", "/extension-group", "extensionGroups"],
  ["POST", "/extension-group", "extensionGroupAdd"],
  ["PUT", "/extension-group/{id}", "extensionGroupPut"],
  ["DELETE", "/extension-group/{id}", "extensionGroupDelete"],
  ["POST", "/collection/{id}/members", "addCollectionMembers"],
  ["DELETE", "/collection/{id}/members", "removeCollectionMembers"],
  ["GET", "/playlist", "getAllPlaylists"],
  ["POST", "/playlist", "addPlaylist"],
  ["GET", "/playlist/{id}", "getPlaylist"],
  ["PUT", "/playlist/{id}", "putPlaylist"],
  ["PATCH", "/playlist/{id}", "patchPlaylist"],
  ["DELETE", "/playlist/{id}", "deletePlaylist"],
  ["GET", "/health-score/profiles", "getAllHealthScoreProfiles"],
  ["GET", "/health-score/resource/{id}/diagnosis", "getHealthScoreDiagnosisForResource"],
  ["GET", "/media-library-resource-mapping/by-resource/{id}", "getMappingsByResourceId"],
  ["POST", "/media-library-resource-mapping", "addMediaLibraryResourceMapping"],
  ["DELETE", "/media-library-resource-mapping/{id}", "deleteMediaLibraryResourceMapping"],
  ["GET", "/resource-profile/by-resource/{id}", "getMatchingProfilesForResource"],
  ["POST", "/resource-profile/by-resource/{id}/bind-property", "bindPropertyToMatchingProfiles"],
  ["PUT", "/resource-profile/{id}", "updateResourceProfile"],
  ["GET", "/options/resource/recent-filters", "getRecentResourceFilters"],
  ["POST", "/options/resource/recent-filters", "addRecentResourceFilter"],
  ["POST", "/options/ui/latest-used-property", "addLatestUsedProperty"],
  ["GET", "/data-card/resource/{id}/associated", "getAssociatedDataCards"],
  ["POST", "/data-card/find-by-identity", "findDataCardByIdentity"],
  ["PUT", "/data-card/{id}", "updateDataCard"],
  ["POST", "/data-card", "addDataCard"],
  ["GET", "/data-card-type", "getAllDataCardTypes"],
  ["PUT", "/data-card-type/{id}/display-template", "updateDataCardTypeDisplayTemplate"],
  ["PUT", "/data-card-type/{id}", "updateDataCardType"],
  ["POST", "/data-card-type", "addDataCardType"],
  ["GET", "/file/children/iwfs-info", "getChildrenIwFsInfo"],
];

const compiledRoutes = routes.map(([method, template, operation]) => {
  const names: string[] = [];
  const expression = template
    .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
    .replace(/\\\{(\w+)\\\}/g, (_, name) => {
      names.push(name);

      return "(\\d+)";
    });

  return { method, operation, names, pattern: new RegExp(`^${expression}$`) };
});

export class UnsupportedManagedResourceOperationError extends Error {
  constructor(operation: string) {
    super(`This resource action is not available through managed-device access: ${operation}`);
    this.name = "UnsupportedManagedResourceOperationError";
  }
}

export class ManagedResourceBatchError extends Error {
  constructor(
    readonly confirmedCount: number,
    readonly totalCount: number,
    readonly cause: unknown,
  ) {
    super(
      `Confirmed ${confirmedCount} of ${totalCount} items. The remaining items were not retried. ${cause instanceof Error ? cause.message : String(cause)}`,
    );
    this.name = "ManagedResourceBatchError";
  }
}

function serializeQuery(query: FullRequestParams["query"]): Record<string, string[]> {
  const result: Record<string, string[]> = {};

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === undefined || value === null) continue;
    const values = Array.isArray(value) ? value : [value];

    if (values.some((item) => !["string", "number", "boolean"].includes(typeof item)))
      throw new UnsupportedManagedResourceOperationError(`query.${key}`);
    result[key] = values.map(String);
  }

  return result;
}

/** Media URLs are selected by resource and cover index, never by an arbitrary owner's filesystem path. */
export function managedResourceCoverUrl(
  source: Readonly<ManagementSource>,
  resourceId: number,
  index = 0,
  width = 600,
  height = 600,
) {
  const query = new URLSearchParams({
    libraryEpoch: source.libraryEpoch,
    resourceId: String(resourceId),
    index: String(index),
    w: String(width),
    h: String(height),
  });

  return `${envConfig.apiEndpoint}/federation/local/management/${encodeURIComponent(source.serverId)}/cover?${query}`;
}

export function managedResourceAssetUrl(
  source: Readonly<ManagementSource>,
  resourceId: number,
  key: string,
  thumbnail = false,
  width = 600,
  height = 600,
) {
  const query = new URLSearchParams({
    libraryEpoch: source.libraryEpoch,
    resourceId: String(resourceId),
    key,
    thumbnail: String(thumbnail),
    w: String(width),
    h: String(height),
  });

  return `${envConfig.apiEndpoint}/federation/local/management/${encodeURIComponent(source.serverId)}/asset?${query}`;
}

function encodeUpload(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
    reader.onerror = () => reject(reader.error || new Error("Unable to read file"));
    reader.readAsDataURL(file);
  });
}

/**
 * A private generated SDK instance retains its normal typed methods and response handling.
 * Every request is bound to the captured owner; unknown requests fail before any I/O.
 * The application's singleton SDK and its URL are never changed.
 */
export function createResourceApi(
  source?: Readonly<ManagementSource>,
  resourceId?: number,
): typeof BApi {
  if (!source || source.isLocal) return BApi;
  const owner = Object.freeze({ ...source });
  const ApiConstructor = BApi.constructor as new (config: ApiConfig) => Api<unknown>;
  const api = new ApiConstructor({ baseUrl: "" });

  api.request = async <T = any>(request: FullRequestParams): Promise<T> => {
    if (!owner.libraryEpoch || owner.metadata?.denied) throw new Error(metadataReadError(owner));
    if (request.type === "multipart/form-data") {
      if (!owner.available) throw new Error(owner.error || "Device unavailable");
      const torrent = /^\/resource\/(\d+)\/acquisition-leads\/torrent$/.exec(request.path);
      const uploadId = torrent
        ? Number(torrent[1])
        : request.path === "/file/upload"
          ? resourceId
          : undefined;
      const file =
        request.body instanceof FormData
          ? request.body.get("file")
          : (request.body as { file?: File })?.file;

      if (request.method !== "POST" || !uploadId || !(file instanceof File))
        throw new UnsupportedManagedResourceOperationError("unbound multipart upload");
      const limit = torrent ? 4 : 16;

      if (file.size > limit * 1024 * 1024)
        throw new Error(`Managed-device uploads support files up to ${limit} MiB.`);
      const base64 = await encodeUpload(file);

      return BApi.request<T>({
        path: `/federation/local/management/${encodeURIComponent(owner.serverId)}/upload`,
        method: "POST",
        type: "application/json" as ContentType,
        format: "json",
        body: {
          libraryEpoch: owner.libraryEpoch,
          operation: torrent ? "addResourceAcquisitionTorrent" : "uploadFile",
          id: uploadId,
          body: { fileName: file.name, base64 },
        },
        signal: request.signal,
        showErrorToast: request.showErrorToast,
      });
    }

    // Visual preferences belong to the viewer, even while a resource belongs elsewhere.
    if (request.method === "PATCH" && ["/options/ui", "/options/resource"].includes(request.path))
      return BApi.request<T>(request);

    const query = serializeQuery(request.query);
    const nativePlay = /^\/resource\/(\d+)\/play-item$/.exec(request.path);

    if (
      request.method === "POST" &&
      ["/player/batch-play/candidates", "/player/batch-play"].includes(request.path)
    ) {
      if (!owner.available) throw new Error(owner.error || "Device unavailable");
      const data = request.body as {
        resourceIds?: number[];
        playerKey?: string;
        fileSelectionMode?: number;
      };

      if (
        !Array.isArray(data?.resourceIds) ||
        data.resourceIds.length === 0 ||
        data.resourceIds.length > 100 ||
        data.resourceIds.some((id) => !Number.isSafeInteger(id) || id <= 0)
      ) {
        throw new Error(
          "Select between 1 and 100 resources from the same device for batch playback.",
        );
      }

      return BApi.request<T>({
        path: `/federation/local/management/${encodeURIComponent(owner.serverId)}${request.path.slice("/player".length)}`,
        method: "POST",
        type: "application/json" as ContentType,
        format: "json",
        body: { ...data, libraryEpoch: owner.libraryEpoch },
        signal: request.signal,
        cancelToken: request.cancelToken,
        showErrorToast: request.showErrorToast,
      });
    }

    if (request.method === "GET" && request.path === "/resource/directory") {
      if (!owner.available) throw new Error(owner.error || "Device unavailable");

      return BApi.request<T>({
        path: `/federation/local/management/${encodeURIComponent(owner.serverId)}/directory`,
        method: "POST",
        type: "application/json" as ContentType,
        format: "json",
        body: { libraryEpoch: owner.libraryEpoch, resourceId: Number(query.id?.[0]) },
        signal: request.signal,
        showErrorToast: request.showErrorToast,
      });
    }

    if (request.method === "GET" && nativePlay) {
      if (!owner.available) throw new Error(owner.error || "Device unavailable");

      return BApi.request<T>({
        path: `/federation/local/management/${encodeURIComponent(owner.serverId)}/play`,
        method: "POST",
        type: "application/json" as ContentType,
        format: "json",
        body: {
          libraryEpoch: owner.libraryEpoch,
          resourceId: Number(nativePlay[1]),
          origin: Number(query.origin?.[0]),
          key: query.key?.[0],
          explicitChoice: query.explicitChoice?.[0] === "true",
        },
        signal: request.signal,
        showErrorToast: request.showErrorToast,
      });
    }

    const route = compiledRoutes.find(
      ({ method, pattern }) => method === request.method && pattern.test(request.path),
    );

    if (!route)
      throw new UnsupportedManagedResourceOperationError(`${request.method} ${request.path}`);
    if (
      metadataReadOperations.has(route.operation) ? !canReadSourceMetadata(owner) : !owner.available
    )
      throw new Error(metadataReadError(owner));
    const parameters = route.pattern.exec(request.path)!;
    let id: number | undefined;

    route.names.forEach((name, index) => {
      const value = decodeURIComponent(parameters[index + 1]);

      if (name === "id") id = Number(value);
      else query[name] = [value];
    });
    if (route.operation === "getChildrenIwFsInfo") {
      if (!resourceId) throw new UnsupportedManagedResourceOperationError("unbound resource files");
      id = resourceId;
    }
    let ids: number[] | undefined;

    if (route.operation === "resourceGet" || route.operation === "getCustomPropertyByKeys") {
      ids = query.ids?.map(Number);
      delete query.ids;
    }
    if (["getCollectionIdsByResourceIds", "getCollectionMemberships"].includes(route.operation)) {
      ids = query.resourceIds?.map(Number);
      delete query.resourceIds;
    }

    const body = {
      libraryEpoch: owner.libraryEpoch,
      operation: route.operation,
      ...(id !== undefined ? { id } : {}),
      ...(ids !== undefined ? { ids } : {}),
      ...(request.body !== undefined ? { body: request.body } : {}),
      ...(Object.keys(query).length ? { query } : {}),
    };
    const mutation = request.method !== "GET" && !readOperations.has(route.operation);
    const dispatch = async (payload: typeof body, changedIds?: readonly number[]) => {
      const response = await BApi.request<T>({
        path: `/federation/local/management/${encodeURIComponent(owner.serverId)}/request`,
        method: "POST",
        type: "application/json" as ContentType,
        format: "json",
        body: payload,
        signal: request.signal,
        cancelToken: request.cancelToken,
        showErrorToast: request.showErrorToast,
      });

      if (mutation && !(response as any)?.code) {
        if ((response as any)?.metadataPending)
          toast(i18next.t("management.metadata.savedRefreshing"), {
            id: `metadata-refresh-${owner.serverId}`,
          });
        const resourceMutation = /^\/(?:cache\/)?resource\/\d+(?:\/|$)/.test(request.path);

        notifyResourceOwnerChanged(
          owner,
          changedIds ?? (resourceMutation && id ? [id] : undefined),
        );
      }

      return response;
    };
    const idsField =
      route.operation === "resourceLibrariesPut" ||
      route.operation === "bulkDeleteResources" ||
      route.operation === "refreshResourcesCache"
        ? "ids"
        : [
              "bulkPutResourcePropertyValue",
              "addCollectionMembers",
              "removeCollectionMembers",
            ].includes(route.operation)
          ? "resourceIds"
          : undefined;
    const bodyArray = ["resourceMappings", "deleteEnhancementsByResources"].includes(
      route.operation,
    );
    const items: number[] | undefined =
      ids ?? (bodyArray ? request.body : idsField ? (request.body as any)?.[idsField] : undefined);

    if (!Array.isArray(items) || items.length <= 100)
      return dispatch(body, mutation ? items : undefined);
    const responses: any[] = [];
    let confirmedCount = 0;

    for (let offset = 0; offset < items.length; offset += 100) {
      const chunk = items.slice(offset, offset + 100);
      const payload = ids
        ? { ...body, ids: chunk }
        : bodyArray
          ? { ...body, body: chunk }
          : { ...body, body: { ...(request.body as object), [idsField!]: chunk } };

      try {
        const response = await dispatch(payload, mutation ? chunk : undefined);

        if ((response as any)?.code)
          throw new Error((response as any).message || String((response as any).code));
        responses.push(response);
        confirmedCount += chunk.length;
      } catch (failure) {
        const error = new ManagedResourceBatchError(confirmedCount, items.length, failure);

        if (confirmedCount && mutation) toast.error(error.message);
        throw error;
      }
    }
    const last = responses.at(-1);

    if (responses.every((response) => Array.isArray(response.data)))
      return { ...last, data: responses.flatMap((response) => response.data) };
    if (responses.every((response) => response.data && typeof response.data === "object"))
      return { ...last, data: Object.assign({}, ...responses.map((response) => response.data)) };

    return last;
  };

  // Generated URL builders bypass request(). Do not let one accidentally address
  // this manager's file/resource endpoints with another owner's path or numeric ID.
  for (const group of Object.values(api)) {
    if (!group || typeof group !== "object") continue;
    for (const name of Object.keys(group)) {
      if (name.endsWith("Url") && typeof group[name] === "function") {
        group[name] = () => {
          throw new UnsupportedManagedResourceOperationError(name);
        };
      }
    }
  }

  return api;
}
