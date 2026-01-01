import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import _ from "lodash";

/**
 * Compare two marks for equality based on type + configJson only.
 * Ignores id, path, priority, syncStatus, and other metadata fields.
 */
export function areMarksEqual(
  markA: BakabaseAbstractionsModelsDomainPathMark,
  markB: BakabaseAbstractionsModelsDomainPathMark,
): boolean {
  if (markA.type !== markB.type) {
    return false;
  }

  try {
    const configA = JSON.parse(markA.configJson || "{}");
    const configB = JSON.parse(markB.configJson || "{}");
    return _.isEqual(configA, configB);
  } catch {
    // If JSON parsing fails, fall back to string comparison
    return markA.configJson === markB.configJson;
  }
}

/**
 * Get marks from source that don't exist in target.
 * Returns marks from sourceMarks that are NOT equal to any mark in targetMarks.
 */
export function getNewMarks(
  sourceMarks: BakabaseAbstractionsModelsDomainPathMark[],
  targetMarks: BakabaseAbstractionsModelsDomainPathMark[],
): BakabaseAbstractionsModelsDomainPathMark[] {
  return sourceMarks.filter(
    (sourceMark) => !targetMarks.some((targetMark) => areMarksEqual(sourceMark, targetMark)),
  );
}

/**
 * Get count of marks from source that already exist in target.
 */
export function getExistingMarksCount(
  sourceMarks: BakabaseAbstractionsModelsDomainPathMark[],
  targetMarks: BakabaseAbstractionsModelsDomainPathMark[],
): number {
  return sourceMarks.filter((sourceMark) =>
    targetMarks.some((targetMark) => areMarksEqual(sourceMark, targetMark)),
  ).length;
}
