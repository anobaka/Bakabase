/** Maximum number of options to show inline before showing "more" button */
export const INLINE_OPTIONS_LIMIT = 30;

/**
 * Build visible options for inline editing mode.
 *
 * Rules:
 * 1. Options in first 30 maintain original order (don't reorder selected items)
 * 2. Selected options beyond position 30 are appended at the end
 * 3. Total visible count = max(30, selectedCount)
 * 4. If total exceeds max, remove unselected items from first 30 to make room
 *
 * @param options - All options in original order
 * @param isSelected - Function to check if an option is selected
 * @returns Visible options array
 */
export function buildVisibleOptions<T>(
  options: T[],
  isSelected: (item: T) => boolean
): T[] {
  if (options.length === 0) return [];

  const selectedCount = options.filter(isSelected).length;
  const maxCount = Math.max(INLINE_OPTIONS_LIMIT, selectedCount);

  // Split first 30 into selected and unselected
  const first30 = options.slice(0, INLINE_OPTIONS_LIMIT);
  const selectedInFirst30 = first30.filter(isSelected);
  const unselectedInFirst30 = first30.filter((item) => !isSelected(item));

  // Selected options beyond position 30
  const selectedBeyond30 = options
    .slice(INLINE_OPTIONS_LIMIT)
    .filter(isSelected);

  // Calculate how many unselected from first 30 we can show
  const totalSelected = selectedInFirst30.length + selectedBeyond30.length;
  const unselectedSlots = Math.max(0, maxCount - totalSelected);
  const unselectedToShow = unselectedInFirst30.slice(0, unselectedSlots);

  // Build first portion maintaining original order
  const includedItems = new Set([...selectedInFirst30, ...unselectedToShow]);
  const firstPortion = first30.filter((item) => includedItems.has(item));

  // Combine: first portion (in original order) + selected beyond 30
  return [...firstPortion, ...selectedBeyond30];
}

/**
 * Check if there are more options beyond the visible ones.
 */
export function hasMoreOptions(totalCount: number): boolean {
  return totalCount > INLINE_OPTIONS_LIMIT;
}

/**
 * Calculate remaining count of hidden options.
 */
export function getRemainingCount(totalCount: number, visibleCount: number): number {
  return totalCount - visibleCount;
}
