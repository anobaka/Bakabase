import { ResourceSource } from "@/sdk/constants";

export type MoveEligibilityResource = {
  id: number;
  path?: string | null;
  hasLocalPath?: boolean | null;
  sourceLinks?: { source?: number | null }[] | null;
};

export type KnownMoveBlockReason = "steamManaged" | "noLocalFiles";

/** Source ownership is authoritative here; Steam metadata alone does not imply ownership. */
export const getKnownMoveBlockReason = (
  resource: MoveEligibilityResource,
): KnownMoveBlockReason | undefined => {
  if (resource.sourceLinks?.some((link) => link.source === ResourceSource.Steam)) {
    return "steamManaged";
  }
  if (resource.hasLocalPath === false || resource.path === null || resource.path === "") {
    return "noLocalFiles";
  }

  // Missing fields can be from progressive loading. The server checks these and child resources.
  return undefined;
};

/** Mixed or unloaded selections must reach preview intact, including the blocked members. */
export const getSelectionMoveBlockReasons = (
  ids: readonly number[],
  resources: readonly MoveEligibilityResource[],
): KnownMoveBlockReason[] => {
  const byId = new Map(resources.map((resource) => [resource.id, resource]));
  const reasons = new Set<KnownMoveBlockReason>();

  for (const id of ids) {
    const resource = byId.get(id);
    const reason = resource && getKnownMoveBlockReason(resource);

    if (!reason) return [];
    reasons.add(reason);
  }

  return [...reasons];
};
