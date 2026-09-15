import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type {
  BakabaseModulesSearchModelsDbResourceSearchFilterGroupDbModel as ApiFilterGroup,
  BakabaseModulesSearchModelsDbResourceSearchFilterDbModel as ApiFilter,
} from "@/sdk/Api";
import type { MultilevelData } from "@/components/StandardValue/models";
import type { PropertyPool } from "@/sdk/constants";

import { collectSubtreeValues, serializeStandardValue } from "@/components/StandardValue/helpers";
import { SearchOperation, StandardValueType } from "@/sdk/constants";

/**
 * A collection's rule is stored as a serialized `ResourceSearchDbModel` — the same
 * shape a resource profile's search is stored in, so the two mean the same thing by
 * the same filter. Only the group is used: a rule has no page, keyword or order.
 */
type RuleSearch = {
  group?: ApiFilterGroup;
};

const apiGroupToInternal = (
  g: ApiFilterGroup | null | undefined,
): SearchFilterGroup | undefined => {
  if (!g) return undefined;

  return {
    combinator: g.combinator as unknown as SearchFilterGroup["combinator"],
    disabled: g.disabled ?? false,
    groups: g.groups?.map((sub) => apiGroupToInternal(sub)!).filter(Boolean),
    filters: g.filters?.map((f) => ({
      propertyId: f.propertyId,
      propertyPool: f.propertyPool as any,
      operation: f.operation as any,
      // The controller keeps the serialized value under `dbValue`; the DB shape calls
      // the same thing `value`.
      dbValue: f.value,
      disabled: f.disabled ?? false,
    })),
  };
};

const internalGroupToApi = (
  g: SearchFilterGroup | null | undefined,
): ApiFilterGroup | undefined => {
  if (!g) return undefined;

  return {
    combinator: g.combinator as any,
    disabled: g.disabled ?? false,
    groups: g.groups?.map((sub) => internalGroupToApi(sub)!).filter(Boolean),
    filters: g.filters?.map(
      (f): ApiFilter => ({
        propertyId: f.propertyId,
        propertyPool: f.propertyPool,
        operation: f.operation,
        value: f.dbValue,
        disabled: f.disabled ?? false,
      }),
    ),
  };
};

/** Whether a group would match everything — which as a rule means "no rule". */
export const isEmptyGroup = (g: SearchFilterGroup | null | undefined): boolean =>
  !g || ((g.filters ?? []).length === 0 && (g.groups ?? []).length === 0);

export const parseRuleSearchJson = (json: string | null | undefined) => {
  if (!json) return undefined;

  try {
    return apiGroupToInternal((JSON.parse(json) as RuleSearch).group);
  } catch {
    // A rule saved by a newer build, or one a hand edit broke. Showing an empty editor
    // beats showing nothing at all, and the user can rewrite it.
    return undefined;
  }
};

/**
 * Back to what the server stores. An empty group serializes to null rather than to a
 * filter that matches everything — a collection whose rule is "anything" would swallow
 * the whole library.
 */
export const buildRuleSearchJson = (group: SearchFilterGroup | null | undefined): string | null =>
  isEmptyGroup(group) ? null : JSON.stringify({ group: internalGroupToApi(group) } as RuleSearch);

/**
 * "Everything under this node" as a rule.
 *
 * A multilevel value is stored as the chosen node's id alone, so a filter naming a
 * branch matches only resources filed on the branch itself. Expanding the subtree here
 * is what makes "make a collection of this category" mean what the user expects.
 */
export const buildMultilevelSubtreeRule = <V>(
  propertyId: number,
  propertyPool: PropertyPool,
  data: MultilevelData<V>[],
  nodeValue: V,
): string | null => {
  const values = collectSubtreeValues(data, nodeValue).map(String);

  if (values.length === 0) return null;

  return buildRuleSearchJson({
    combinator: 1 as SearchFilterGroup["combinator"],
    disabled: false,
    filters: [
      {
        propertyId,
        propertyPool,
        operation: SearchOperation.In,
        dbValue: serializeStandardValue(values, StandardValueType.ListString),
        disabled: false,
      },
    ],
  });
};
