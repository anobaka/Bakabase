import type { DataSyncHistoryCounts, DataSyncHistoryEntry, DataSyncUndoPreviewItem } from "./api";

import {
  DataSyncHistoryKind,
  DataSyncHistoryKindLabel,
  DataSyncUndoAction,
  DataSyncUndoState,
} from "@/sdk/constants";

/*
 * The history (spec §11.3, §8.11), pure: how each entry is named and counted, and how an undo
 * preview is grouped.
 */

/** The history kind's name, as its label key reads it (`FirstLink`, `AutoSync`, …). */
export const historyKindName = (kind: DataSyncHistoryKind) =>
  DataSyncHistoryKindLabel[kind] ?? DataSyncHistoryKindLabel[DataSyncHistoryKind.AutoSync];

/** Every kind can be undone while it is still available, except an undo: there is no redo. */
export const isUndoable = (entry: DataSyncHistoryEntry) =>
  entry.kind !== DataSyncHistoryKind.Undo && entry.undoState === DataSyncUndoState.Available;

/** The counts a row names, in this order, those above zero. */
export const historyCountKeys = [
  "created",
  "updated",
  "linked",
  "deleted",
  "typeChanged",
  "reordered",
  "resolved",
  "skipped",
  "held",
  "changedSinceReview",
  "changedDuringApply",
] as const satisfies readonly (keyof DataSyncHistoryCounts)[];

export type HistoryCountKey = (typeof historyCountKeys)[number];

export const historyCounts = (counts: DataSyncHistoryCounts) =>
  historyCountKeys.filter((key) => counts[key] > 0).map((key) => ({ key, count: counts[key] }));

// ---- the undo preview ------------------------------------------------------------------------------

/** How the undo dialog groups what it will do: by what happens, and what it has to keep. */
export type UndoGroup = "remove" | "revert" | "unlink" | "recreate" | "exclude" | "keep";

export const undoGroupOrder: UndoGroup[] = [
  "remove",
  "revert",
  "recreate",
  "unlink",
  "exclude",
  "keep",
];

const groupOfAction: Record<DataSyncUndoAction, UndoGroup> = {
  [DataSyncUndoAction.Remove]: "remove",
  [DataSyncUndoAction.Revert]: "revert",
  [DataSyncUndoAction.RemoveAliases]: "unlink",
  [DataSyncUndoAction.Recreate]: "recreate",
  [DataSyncUndoAction.Exclude]: "exclude",
};

/** The preview's rows by group, in {@link undoGroupOrder}; a blocked row is one it keeps. */
export const undoGroups = (items: readonly DataSyncUndoPreviewItem[]) => {
  const groups = new Map<UndoGroup, DataSyncUndoPreviewItem[]>();

  for (const group of undoGroupOrder) {
    const inGroup = items.filter((item) =>
      group === "keep"
        ? item.blocked != null
        : item.blocked == null && (groupOfAction[item.action] ?? "revert") === group,
    );

    if (inGroup.length) groups.set(group, inGroup);
  }

  return groups;
};
