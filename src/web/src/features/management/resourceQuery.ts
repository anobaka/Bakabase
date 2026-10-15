import type { SearchForm } from "@/pages/resource/models";
import {
  GroupCombinator,
  type SearchFilter,
  type SearchFilterGroup,
} from "@/components/ResourceFilter/models";
import {
  InternalProperty,
  PropertyPool,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";
import type { ManagementSource } from "./api";
import {
  deserializeStandardValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import {
  isMediaLibraryFilter,
  MEDIA_LIBRARY_CHOICE_PREFIX,
  parseMediaLibraryChoiceValue,
} from "./mediaLibraryFilter";

export type QueryOwner = Pick<ManagementSource, "serverId" | "libraryEpoch">;
const owns = (a: QueryOwner, b: QueryOwner) =>
  a.serverId === b.serverId && a.libraryEpoch === b.libraryEpoch;
const ownerLocalReferences = new Set<number>([
  InternalProperty.Resource,
  InternalProperty.ParentResource,
  InternalProperty.MediaLibraryV2,
  InternalProperty.MediaLibraryV2Multi,
  InternalProperty.CollectionMulti,
]);

/** Carry inherited scope into editable filters so restored foreign IDs hydrate from their owner. */
export function normalizeQueryOwner<T extends SearchForm>(form: T): T {
  // The backend serializes an unset DeviceIds as null. Keep one frontend
  // representation of "all devices" while preserving [] as "no devices".
  if (form.deviceIds === null) form = { ...form, deviceIds: undefined };
  if (!form.owner || !form.group) return form;
  const owner = form.owner;
  const bind = (group: SearchFilterGroup): SearchFilterGroup => ({
    ...group,
    filters: group.filters?.map((filter) => ({ ...filter, owner: filter.owner ?? owner })),
    groups: group.groups?.map(bind),
  });
  return { ...form, group: bind(form.group) };
}

/** Resolve identity-bearing predicates before they reach an owner's normal search API.
 * A predicate for another device is FALSE, rather than removed from an AND/OR expression.
 */
export function compileResourceQuery(
  form: SearchForm,
  source: ManagementSource,
): SearchForm | false {
  if (form.owner && !owns(form.owner, source)) return false;
  if (form.deviceIds != null && !form.deviceIds.includes(source.serverId)) return false;
  const compileGroup = (group?: SearchFilterGroup): SearchFilterGroup | boolean | undefined => {
    if (!group || group.disabled) return undefined;
    const filters: SearchFilter[] = [];
    const groups: SearchFilterGroup[] = [];
    const constants: boolean[] = [];
    for (const filter of group.filters ?? []) {
      if (
        filter.disabled ||
        filter.propertyId == null ||
        filter.propertyPool == null ||
        filter.operation == null
      )
        continue;
      // Default/restored filter rows are placeholders until a value is chosen.
      // Native search ignores these before matching; they must not become FALSE
      // on other owners merely because their property belongs to this device.
      if (
        filter.operation !== SearchOperation.IsNull &&
        filter.operation !== SearchOperation.IsNotNull &&
        !filter.dbValue?.trim()
      )
        continue;
      const owner = (filter as SearchFilter & { owner?: QueryOwner }).owner ?? form.owner;
      let localReference =
        filter.propertyPool === PropertyPool.Custom ||
        (filter.propertyPool === PropertyPool.Internal &&
          ownerLocalReferences.has(filter.propertyId));
      let dbValue = filter.dbValue;
      if (owner && !owns(owner, source)) {
        constants.push(false);
        continue;
      }
      if (isMediaLibraryFilter(filter)) {
        if (
          filter.operation === SearchOperation.IsNull ||
          filter.operation === SearchOperation.IsNotNull
        ) {
          // An ownerless library emptiness condition has the same meaning on every device.
          localReference = false;
        } else if (dbValue?.includes(MEDIA_LIBRARY_CHOICE_PREFIX)) {
          const values: unknown = deserializeStandardValue(dbValue, StandardValueType.ListString);
          if (!Array.isArray(values) || values.some((value) => typeof value !== "string")) {
            constants.push(false);
            continue;
          }
          const choices = values.map(parseMediaLibraryChoiceValue);
          if (!choices.length || choices.some((choice) => !choice)) {
            // Never reinterpret a damaged qualified value as an owner-local numeric ID.
            constants.push(false);
            continue;
          }
          const ownedIds = [
            ...new Set(
              choices.flatMap((choice) =>
                choice && owns(choice.owner, source) ? [choice.id] : [],
              ),
            ),
          ];
          if (filter.operation === SearchOperation.In) {
            if (!ownedIds.length) {
              constants.push(false);
              continue;
            }
          } else if (filter.operation === SearchOperation.Contains) {
            if (choices.some((choice) => !owns(choice!.owner, source))) {
              constants.push(false);
              continue;
            }
          } else if (filter.operation === SearchOperation.NotContains) {
            if (!ownedIds.length) {
              constants.push(true);
              continue;
            }
          } else {
            constants.push(false);
            continue;
          }
          dbValue = serializeStandardValue(ownedIds, StandardValueType.ListString);
          localReference = false;
        }
      }
      if (owner ? !owns(owner, source) : localReference && !source.isLocal) {
        constants.push(false);
      } else {
        filters.push({
          propertyId: filter.propertyId,
          propertyPool: filter.propertyPool,
          operation: filter.operation,
          dbValue,
          disabled: false,
        });
      }
    }
    for (const child of group.groups ?? []) {
      const compiled = compileGroup(child);
      if (typeof compiled === "boolean") constants.push(compiled);
      else if (compiled) groups.push(compiled);
    }
    const and = group.combinator !== GroupCombinator.Or;
    if (constants.some((value) => value !== and)) return !and;
    if (!filters.length && !groups.length) return constants.length ? and : undefined;
    return { combinator: group.combinator, disabled: false, filters, groups };
  };
  const group = compileGroup(form.group);
  if (group === false) return false;
  return {
    page: form.page,
    pageSize: form.pageSize,
    orders: form.orders,
    keyword: form.keyword,
    tags: form.tags,
    group: typeof group === "object" ? group : undefined,
  };
}

/** Saved ownership is an identity, never cached connectivity or a trusted endpoint. */
export function resolveQuerySources(
  form: SearchForm,
  sources: ManagementSource[],
  scope?: ManagementSource,
) {
  const owner = scope ?? form.owner;
  if (owner) {
    const current = scope ?? sources.find((source) => owns(source, owner));
    return current
      ? [current]
      : [
          {
            ...owner,
            name: owner.serverId,
            isLocal: false,
            available: false,
            contractVersion: 0,
            error: "This device or library is no longer available",
          } as ManagementSource,
        ];
  }
  return sources.filter(
    (source) => form.deviceIds == null || form.deviceIds.includes(source.serverId),
  );
}
