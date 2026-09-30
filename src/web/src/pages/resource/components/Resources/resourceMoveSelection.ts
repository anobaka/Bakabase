export type MoveResource = { id: number; path?: string | null; displayName?: string | null };

/** A plain press on a selected card belongs to native resource dragging. */
export const shouldStartResourceMove = (
  panelEnabled: boolean,
  selected: boolean,
  modifiers: { altKey: boolean; ctrlKey: boolean; metaKey: boolean; shiftKey: boolean },
  allSelectedKnownBlocked = false,
) =>
  panelEnabled &&
  selected &&
  !allSelectedKnownBlocked &&
  !modifiers.altKey &&
  !modifiers.ctrlKey &&
  !modifiers.metaKey &&
  !modifiers.shiftKey;

/** Keep unloaded selections in the payload; the preview resolves their paths on the server. */
export const snapshotMoveSelection = (ids: number[], resources: MoveResource[]): MoveResource[] => {
  const byId = new Map(resources.map((resource) => [resource.id, resource]));

  return [...new Set(ids)].map((id) => {
    const resource = byId.get(id);

    return {
      id,
      path: resource?.path,
      ...(resource?.displayName ? { displayName: resource.displayName } : {}),
    };
  });
};

/** Submitting one batch must never wipe a more recently selected batch. */
export const removeSubmittedSelection = (
  selectedIds: number[],
  tabId: string,
  submitted: { resourceIds: number[]; sourceTabId?: string },
): number[] => {
  if (submitted.sourceTabId !== tabId) return selectedIds;
  const submittedIds = new Set(submitted.resourceIds);

  return selectedIds.filter((id) => !submittedIds.has(id));
};
