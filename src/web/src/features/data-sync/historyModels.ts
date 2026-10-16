import type { DataSyncHistoryCounts } from "./api";

import { DataSyncHistoryKind, DataSyncHistoryKindLabel } from "@/sdk/constants";

/*
 * The read-only history: how each entry is named and counted.
 */

/** The history kind's name, as its label key reads it (`FirstLink`, `AutoSync`, …). */
export const historyKindName = (kind: DataSyncHistoryKind) =>
  DataSyncHistoryKindLabel[kind] ?? DataSyncHistoryKindLabel[DataSyncHistoryKind.AutoSync];

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
