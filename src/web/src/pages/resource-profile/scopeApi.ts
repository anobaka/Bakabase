import type { ResourceProfile } from "./sourceProfiles";
import type { ContentType } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import { ResourceProfileRuleKind } from "@/sdk/constants";
import { toSearchInputModel } from "@/components/ResourceFilter/utils/toInputModel";

export interface ResourceScope {
  id: number;
  name: string;
  search?: ResourceProfile["search"];
  sourceDeviceId?: string | null;
  sourceLibraryEpoch?: string | null;
  revision: number;
  ruleCount: number;
}
export const ruleFields = {
  nameTemplate: ResourceProfileRuleKind.NameTemplate,
  enhancerOptions: ResourceProfileRuleKind.Enhancers,
  playableFileOptions: ResourceProfileRuleKind.PlayableFiles,
  playerOptions: ResourceProfileRuleKind.Players,
  propertyOptions: ResourceProfileRuleKind.Properties,
  openingMode: ResourceProfileRuleKind.OpeningMode,
} as const;
export type RuleField = keyof typeof ruleFields;

async function request<T>(path: string, method = "GET", body?: unknown) {
  return BApi.request<T>({
    path: `/resource-scope${path}`,
    method,
    body,
    format: "json",
    type: "application/json" as ContentType,
    showErrorToast: false,
  });
}
const scopeInput = (
  scope: Pick<ResourceScope, "name" | "search" | "sourceDeviceId" | "sourceLibraryEpoch">,
) => ({
  name: scope.name,
  sourceDeviceId: scope.sourceDeviceId ?? null,
  sourceLibraryEpoch: scope.sourceLibraryEpoch ?? null,
  search: scope.search ? toSearchInputModel(scope.search) : null,
});

export const resourceScopeApi = {
  list: () => request<ResourceScope[]>(""),
  add: (scope: Pick<ResourceScope, "name" | "search" | "sourceDeviceId" | "sourceLibraryEpoch">) =>
    request<{ id: number; revision: number }>("", "POST", scopeInput(scope)),
  update: (scope: ResourceScope) =>
    request<{ revision: number }>("/" + scope.id, "PUT", {
      ...scopeInput(scope),
      expectedRevision: scope.revision,
    }),
  remove: (scope: ResourceScope) =>
    request(`/${scope.id}?expectedRevision=${scope.revision}`, "DELETE"),
  bind: (profile: ResourceProfile, scopeId: number | null) =>
    request<ResourceProfile>(`/profile/${profile.id}`, "PATCH", {
      scopeId,
      expectedUpdatedAt: profile.updatedAt,
    }),
  rule: (profile: ResourceProfile, field: RuleField, value: unknown) =>
    request<ResourceProfile>(`/profile/${profile.id}/rule`, "PATCH", {
      kind: ruleFields[field],
      valueJson: value === undefined || value === null ? null : JSON.stringify(value),
      expectedUpdatedAt: profile.updatedAt,
    }),
};
