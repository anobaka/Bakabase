import type {
  BakabaseModulesHealthScoreModelsViewHealthScoreProfileViewModel as ApiHealthScoreProfile,
  BakabaseModulesHealthScoreModelsViewFilePredicateDescriptorViewModel as ApiFilePredicate,
  BakabaseModulesHealthScoreModelsInputHealthScoreRuleInputModel as ApiHealthScoreRuleInput,
  BakabaseModulesHealthScoreModelsInputResourceMatcherInputModel as ApiResourceMatcherInput,
  BakabaseModulesHealthScoreModelsInputResourceMatcherLeafInputModel as ApiResourceMatcherLeafInput,
  BakabaseModulesSearchModelsDbResourceSearchFilterGroupDbModel as ApiFilterGroup,
  BakabaseModulesSearchModelsDbResourceSearchFilterDbModel as ApiFilter,
} from "@/sdk/Api";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";

// View shape (server returns this on GET)
export type HealthScoreProfile = ApiHealthScoreProfile;
export type FilePredicateDescriptor = ApiFilePredicate;

// Membership filter: DB shape (mirrors GET response and PATCH input)
export type ResourceSearchFilterGroup = ApiFilterGroup;

/**
 * The HealthScore API exposes the membership filter in its raw DB shape, where
 * filter values live under `value`. ResourceFilterController works in the
 * frontend shape, where they live under `dbValue`. These helpers convert
 * between the two so the round-trip GET → edit → PATCH preserves values.
 */
export const apiFilterGroupToInternal = (
  g: ApiFilterGroup | null | undefined,
): SearchFilterGroup | undefined => {
  if (!g) return undefined;
  return {
    combinator: g.combinator as unknown as SearchFilterGroup["combinator"],
    disabled: g.disabled ?? false,
    groups: g.groups?.map((sub) => apiFilterGroupToInternal(sub)!).filter(Boolean),
    filters: g.filters?.map((f) => ({
      propertyId: f.propertyId,
      propertyPool: f.propertyPool as any,
      operation: f.operation as any,
      dbValue: f.value,
      disabled: f.disabled ?? false,
    })),
  };
};

export const internalFilterGroupToApi = (
  g: SearchFilterGroup | null | undefined,
): ApiFilterGroup | undefined => {
  if (!g) return undefined;
  return {
    combinator: g.combinator as any,
    disabled: g.disabled ?? false,
    groups: g.groups?.map((sub) => internalFilterGroupToApi(sub)!).filter(Boolean),
    filters: g.filters?.map((f: any): ApiFilter => ({
      propertyId: f.propertyId,
      propertyPool: f.propertyPool,
      operation: f.operation,
      value: f.dbValue,
      disabled: f.disabled ?? false,
    })),
  };
};

// Input shapes (sent on PATCH; property values are pre-serialized StandardValue strings)
// Both GET and PATCH use these structurally identical shapes — the server's
// view model and input model are the same shape too.
export type HealthScoreRule = ApiHealthScoreRuleInput;
export type ResourceMatcher = ApiResourceMatcherInput;
export type ResourceMatcherLeaf = ApiResourceMatcherLeafInput;

export enum ResourceMatcherLeafKind {
  Property = 1,
  File = 2,
}

export enum SearchCombinator {
  And = 1,
  Or = 2,
}
