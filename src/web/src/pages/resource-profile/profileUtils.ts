import type { BakabaseServiceModelsViewResourceProfileViewModel as ResourceProfile } from "@/sdk/Api";

import { toSearchInputModel } from "@/components/ResourceFilter/utils/toInputModel";
import { ResourceAdditionalItem } from "@/sdk/constants";

// Profile updates replace the full configuration. Preserve untouched categories and
// use explicit nulls when clearing one so lower-priority profiles can supply it.
export const toProfileInputModel = (profile: Partial<ResourceProfile>) => ({
  name: profile.name ?? "",
  priority: profile.priority ?? 0,
  search: profile.search ? toSearchInputModel(profile.search) : null,
  nameTemplate: profile.nameTemplate || null,
  enhancerOptions: profile.enhancerOptions ?? null,
  playableFileOptions: profile.playableFileOptions ?? null,
  playerOptions: profile.playerOptions ?? null,
  propertyOptions: profile.propertyOptions ?? null,
});

export const profilePreviewSearch = (profile: ResourceProfile, page: number) => ({
  // Match ResourceProfileService: only filter groups and tags define applicability.
  group: toSearchInputModel(profile.search ?? {}).group,
  tags: profile.search?.tags,
  page,
  pageSize: 25,
  additionalItems: ResourceAdditionalItem.DisplayName,
});

export const checkProfileResponse = (
  response: { code?: number; message?: string },
  fallback: string,
) => {
  if (response.code && response.code !== 0) throw new Error(response.message || fallback);
};

export const hasProfileConditions = (search: ResourceProfile["search"]): boolean => {
  const hasGroup = (group: NonNullable<ResourceProfile["search"]>["group"]): boolean =>
    !!group &&
    !group.disabled &&
    (!!group.filters?.some((filter) => !filter.disabled) || !!group.groups?.some(hasGroup));

  return !!search?.tags?.length || hasGroup(search?.group);
};
