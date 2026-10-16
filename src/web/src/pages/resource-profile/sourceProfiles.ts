import type {
  BakabaseServiceModelsViewResourceProfileViewModel,
  BakabaseServiceModelsInputResourceSearchInputModel as SearchInput,
} from "@/sdk/Api";
import type { IProperty } from "@/components/Property/models";
import type { SearchFilter } from "@/components/ResourceFilter/models";
import type { Resource } from "@/core/models/Resource";
import type { ContentType } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import {
  toFilterInputModel,
  toSearchInputModel,
} from "@/components/ResourceFilter/utils/toInputModel";

export type ResourceProfile = Omit<
  BakabaseServiceModelsViewResourceProfileViewModel,
  "resourceScopeId" | "sourceDeviceId" | "sourceLibraryEpoch" | "openingMode"
> & {
  resourceScopeId?: number | null;
  sourceDeviceId?: string | null;
  sourceLibraryEpoch?: string | null;
  openingMode?: string | null;
};
export type ProfileSource = {
  deviceId: string;
  name: string;
  libraryEpoch?: string | null;
  available: boolean;
  kind: "shared" | "managed";
};
export type SourceScope = Pick<ResourceProfile, "sourceDeviceId" | "sourceLibraryEpoch">;
type Envelope<T> = { code?: number; message?: string; data?: T; totalCount?: number };

async function request<T>(
  path: string,
  body?: unknown,
  signal?: AbortSignal,
  query?: Record<string, unknown>,
) {
  const response = await BApi.request<Envelope<T>>({
    path,
    method: body === undefined ? "GET" : "POST",
    type: "application/json" as ContentType,
    format: "json",
    body,
    query,
    signal,
    showErrorToast: false,
  });

  if (response.code) throw new Error(response.message || "Resource source unavailable");

  return response;
}
const sourceRequest = <T>(
  scope: SourceScope,
  operation: string,
  body?: unknown,
  signal?: AbortSignal,
) =>
  request<T>(
    `/resource-profile/sources/${encodeURIComponent(scope.sourceDeviceId!)}/${operation}`,
    body,
    signal,
    scope.sourceLibraryEpoch ? { libraryEpoch: scope.sourceLibraryEpoch } : undefined,
  );

export const profileSourceApi = {
  list: async () => (await request<ProfileSource[]>("/resource-profile/sources")).data ?? [],
  info: async (scope: SourceScope) =>
    (
      await sourceRequest<{ deviceId: string; name: string; libraryEpoch?: string | null }>(
        scope,
        "info",
      )
    ).data,
  properties: async (scope: SourceScope, signal?: AbortSignal) =>
    (await sourceRequest<IProperty[]>(scope, "properties", undefined, signal)).data ?? [],
  resolve: async (scope: SourceScope, search: ResourceProfile["search"], signal?: AbortSignal) =>
    (
      await sourceRequest<NonNullable<ResourceProfile["search"]>>(
        scope,
        "search/resolve",
        toSearchInputModel(search ?? {}),
        signal,
      )
    ).data,
  valueProperty: async (scope: SourceScope, filter: SearchFilter) =>
    (await sourceRequest<IProperty>(scope, "filter-value", toFilterInputModel(filter))).data,
  preview: (scope: SourceScope, search: SearchInput, signal?: AbortSignal) =>
    sourceRequest<Resource[]>(scope, "preview", search, signal),
};
