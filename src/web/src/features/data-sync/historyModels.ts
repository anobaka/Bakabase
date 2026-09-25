import type { DataSyncHistoryCounts, DataSyncHistoryEntry, DataSyncUndoPreviewItem } from "./api";

import { millisecondsSince, serverTime } from "./times";

import {
  DataSyncHistoryKind,
  DataSyncHistoryKindLabel,
  DataSyncUndoAction,
  DataSyncUndoState,
} from "@/sdk/constants";

/*
 * The history (spec §11.3, §8.11), pure: how each entry is named and counted, who the drawing
 * above the list shows as sending definitions here, and how an undo preview is grouped.
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

/** One device the drawing shows sending definitions here, with its last month summed up. */
export interface HistorySource {
  nodeId: string;
  name: string;
  syncs: number;
  created: number;
  updated: number;
  linked: number;
  deleted: number;
  lastAt: string;
}

/** How far back the drawing sums. */
export const HISTORY_DRAWING_DAYS = 30;
/** How many devices the drawing shows before "+N more". */
export const HISTORY_DRAWING_ROWS = 3;

/**
 * Who sent definitions here in the last 30 days, busiest-recent first: every entry that came
 * from a device (first syncs, copies, automatic syncs, restores), summed per device. Undone
 * entries and undos do not count — what they did is no longer here.
 */
export const historySources = (
  entries: readonly DataSyncHistoryEntry[],
  now: number = Date.now(),
): HistorySource[] => {
  const window = HISTORY_DRAWING_DAYS * 24 * 60 * 60 * 1000;
  const sources = new Map<string, HistorySource>();

  for (const entry of entries) {
    if (!entry.peerNodeId || entry.kind === DataSyncHistoryKind.Undo) continue;
    if (entry.undoState === DataSyncUndoState.Undone) continue;
    const since = millisecondsSince(entry.appliedAt, now);

    if (since === null || since > window) continue;
    const source = sources.get(entry.peerNodeId) ?? {
      nodeId: entry.peerNodeId,
      name: entry.peerName ?? entry.peerNodeId,
      syncs: 0,
      created: 0,
      updated: 0,
      linked: 0,
      deleted: 0,
      lastAt: entry.appliedAt,
    };

    source.syncs += 1;
    source.created += entry.counts.created;
    source.updated += entry.counts.updated;
    source.linked += entry.counts.linked;
    source.deleted += entry.counts.deleted;
    if (
      (serverTime(entry.appliedAt)?.getTime() ?? 0) >= (serverTime(source.lastAt)?.getTime() ?? 0)
    ) {
      source.lastAt = entry.appliedAt;
      source.name = entry.peerName ?? source.name;
    }
    sources.set(entry.peerNodeId, source);
  }

  return Array.from(sources.values()).sort(
    (a, b) =>
      (serverTime(b.lastAt)?.getTime() ?? 0) - (serverTime(a.lastAt)?.getTime() ?? 0) ||
      a.name.localeCompare(b.name),
  );
};

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
