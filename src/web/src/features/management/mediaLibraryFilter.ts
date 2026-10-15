import type { FilterOwner } from "@/components/ResourceFilter/models";
import { InternalProperty, PropertyPool } from "@/sdk/constants";

export const MEDIA_LIBRARY_CHOICE_PREFIX = "bakabase:library:";

export type MediaLibraryChoice = { owner: FilterOwner; id: string };

/** A library ID is local to one device and one incarnation of its data directory. */
export function mediaLibraryChoiceValue(owner: FilterOwner, id: string | number): string {
  return (
    MEDIA_LIBRARY_CHOICE_PREFIX + JSON.stringify([owner.serverId, owner.libraryEpoch, String(id)])
  );
}

/** Qualified choices are UI/search state only, never IDs passed to an owner's API. */
export function parseMediaLibraryChoiceValue(value: string): MediaLibraryChoice | undefined {
  if (!value.startsWith(MEDIA_LIBRARY_CHOICE_PREFIX)) return undefined;
  try {
    const parts: unknown = JSON.parse(value.slice(MEDIA_LIBRARY_CHOICE_PREFIX.length));
    if (
      !Array.isArray(parts) ||
      parts.length !== 3 ||
      typeof parts[0] !== "string" ||
      typeof parts[1] !== "string" ||
      !/^[a-zA-Z0-9_-]{1,128}$/.test(parts[0]) ||
      !/^[a-zA-Z0-9_-]{1,128}$/.test(parts[1]) ||
      typeof parts[2] !== "string" ||
      !/^[1-9]\d*$/.test(parts[2]) ||
      Number(parts[2]) > 2_147_483_647
    )
      return undefined;
    return { owner: { serverId: parts[0], libraryEpoch: parts[1] }, id: parts[2] };
  } catch {
    return undefined;
  }
}

export const isMediaLibraryProperty = (property: { pool?: number | null; id?: number | null }) =>
  property.pool === PropertyPool.Internal && property.id === InternalProperty.MediaLibraryV2Multi;

export const isMediaLibraryFilter = (filter: {
  propertyPool?: number | null;
  propertyId?: number | null;
}) => isMediaLibraryProperty({ pool: filter.propertyPool, id: filter.propertyId });

/** Retained saved searches still need compilation after the last remote device is removed. */
type LibraryFilterGroup = {
  filters?:
    | { propertyPool?: number | null; propertyId?: number | null; dbValue?: string | null }[]
    | null;
  groups?: LibraryFilterGroup[] | null;
};

export function hasQualifiedMediaLibraryChoices(group?: LibraryFilterGroup | null): boolean {
  return (
    !!group &&
    (!!group.filters?.some(
      (filter) =>
        isMediaLibraryFilter(filter) && filter.dbValue?.includes(MEDIA_LIBRARY_CHOICE_PREFIX),
    ) ||
      !!group.groups?.some(hasQualifiedMediaLibraryChoices))
  );
}
