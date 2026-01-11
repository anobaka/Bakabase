import { getFilterOptionsThreshold, DEFAULT_FILTER_OPTIONS_THRESHOLD } from "@/hooks/useFilterOptionsThreshold";

/** @deprecated Use getFilterOptionsThreshold() instead for dynamic threshold */
export const INLINE_OPTIONS_LIMIT = DEFAULT_FILTER_OPTIONS_THRESHOLD;

/**
 * Get the current inline options limit from user configuration.
 * This reads from localStorage and returns the user-configured threshold.
 */
export function getInlineOptionsLimit(): number {
  return getFilterOptionsThreshold();
}

/**
 * Build visible options for inline editing mode.
 *
 * Rules:
 * 1. Options in first N maintain original order (don't reorder selected items)
 * 2. Selected options beyond position N are appended at the end
 * 3. Total visible count = max(N, selectedCount)
 * 4. If total exceeds max, remove unselected items from first N to make room
 *
 * @param options - All options in original order
 * @param isSelected - Function to check if an option is selected
 * @param limit - Optional limit override (defaults to user-configured threshold)
 * @returns Visible options array
 */
export function buildVisibleOptions<T>(
  options: T[],
  isSelected: (item: T) => boolean,
  limit?: number
): T[] {
  if (options.length === 0) return [];

  const inlineLimit = limit ?? getInlineOptionsLimit();
  const selectedCount = options.filter(isSelected).length;
  const maxCount = Math.max(inlineLimit, selectedCount);

  // Split first N into selected and unselected
  const firstN = options.slice(0, inlineLimit);
  const selectedInFirstN = firstN.filter(isSelected);
  const unselectedInFirstN = firstN.filter((item) => !isSelected(item));

  // Selected options beyond position N
  const selectedBeyondN = options
    .slice(inlineLimit)
    .filter(isSelected);

  // Calculate how many unselected from first N we can show
  const totalSelected = selectedInFirstN.length + selectedBeyondN.length;
  const unselectedSlots = Math.max(0, maxCount - totalSelected);
  const unselectedToShow = unselectedInFirstN.slice(0, unselectedSlots);

  // Build first portion maintaining original order
  const includedItems = new Set([...selectedInFirstN, ...unselectedToShow]);
  const firstPortion = firstN.filter((item) => includedItems.has(item));

  // Combine: first portion (in original order) + selected beyond N
  return [...firstPortion, ...selectedBeyondN];
}

/**
 * Check if there are more options beyond the visible ones.
 * @param totalCount - Total number of options
 * @param limit - Optional limit override (defaults to user-configured threshold)
 */
export function hasMoreOptions(totalCount: number, limit?: number): boolean {
  const inlineLimit = limit ?? getInlineOptionsLimit();
  return totalCount > inlineLimit;
}

/**
 * Calculate remaining count of hidden options.
 */
export function getRemainingCount(totalCount: number, visibleCount: number): number {
  return totalCount - visibleCount;
}
