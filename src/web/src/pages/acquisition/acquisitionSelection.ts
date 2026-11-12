/** Selection is independent of each operation's server-side limit. */
export const acquisitionSelectionLimit = 10_000;
export const acquisitionActionLimits = {
  continue: 1_000,
  adaptive: 500,
  checkSources: 2_000,
  downloadDestination: 1_000,
  storagePolicy: 10_000,
  workspaceCleanup: 10_000,
  delete: 10_000,
} as const;

export function validSelectionSnapshot(ids: readonly number[]) {
  return ids.length > 0 && ids.length <= acquisitionSelectionLimit &&
    ids.every(id => Number.isSafeInteger(id) && id > 0) && new Set(ids).size === ids.length;
}

/** Never widen a selection later when polling returns new matches. */
export function captureFilteredSelection(ids: readonly number[], truncated: boolean, first?: number): number[] | undefined {
  if (!validSelectionSnapshot(ids) || (first == null && truncated)) return undefined;
  if (first != null && (!Number.isSafeInteger(first) || first < 1)) return undefined;
  return first == null ? [...ids] : ids.slice(0, first);
}
