/** Unknown/offline owners retain their positions; new libraries append in catalog order. */
export function mergeMediaLibraryOrder(
  saved: readonly string[],
  keys: readonly string[],
): string[] {
  return [...new Set([...saved, ...keys])];
}

export function orderMediaLibraryChoices<T>(
  choices: readonly T[],
  saved: readonly string[],
  key: (choice: T) => string,
): T[] {
  const positions = new Map(saved.map((value, index) => [value, index]));

  return choices
    .map((choice, index) => ({
      choice,
      index,
      order: positions.get(key(choice)) ?? saved.length + index,
    }))
    .sort((a, b) => a.order - b.order || a.index - b.index)
    .map(({ choice }) => choice);
}

/** Reorder visible slots only, retaining temporarily hidden/unavailable libraries. */
export function reorderVisibleMediaLibraries(
  saved: readonly string[],
  visible: readonly string[],
  active: string,
  over: string,
): string[] {
  const from = visible.indexOf(active);
  const to = visible.indexOf(over);

  if (from < 0 || to < 0 || from === to) return [...saved];
  const reordered = [...visible];

  reordered.splice(to, 0, reordered.splice(from, 1)[0]!);
  const shown = new Set(visible);
  let index = 0;

  return mergeMediaLibraryOrder(saved, visible).map((value) =>
    shown.has(value) ? reordered[index++]! : value,
  );
}
