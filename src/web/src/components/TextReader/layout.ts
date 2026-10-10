// Unmounted rows use an estimate; visible rows are measured by ResizeObserver.
// This keeps even a wrapped, very long line out of the DOM until it is visible.
export const buildLineOffsets = (
  lines: string[],
  lineHeight: number,
  fontSize: number,
  width: number,
  wrap: boolean,
  measured: ReadonlyMap<number, number>,
) => {
  const offsets = [0];
  const columns = Math.max(1, Math.floor(Math.max(1, width - 80) / (fontSize * 0.62)));

  for (let index = 0; index < lines.length; index++) {
    // Account for tabs and wide characters without measuring every source line
    // in the DOM. Actual browser line breaking replaces this estimate on view.
    let length = 0;

    if (wrap) {
      for (const character of lines[index]) {
        length +=
          character === "\t" ? 4 - (length % 4) : character.codePointAt(0)! >= 0x1100 ? 2 : 1;
      }
    }
    const estimate = lineHeight * (wrap ? Math.max(1, Math.ceil(length / columns)) : 1);

    offsets.push(offsets[index] + (measured.get(index) ?? estimate));
  }

  return offsets;
};

export const findLineAtOffset = (offsets: number[], offset: number) => {
  let low = 0;
  let high = Math.max(0, offsets.length - 2);

  while (low < high) {
    const middle = Math.ceil((low + high) / 2);

    if (offsets[middle] <= offset) low = middle;
    else high = middle - 1;
  }

  return low;
};

export const getVisibleLineRange = (offsets: number[], scrollTop: number, height: number) => ({
  start: Math.max(0, findLineAtOffset(offsets, scrollTop) - 6),
  end: Math.min(offsets.length - 1, findLineAtOffset(offsets, scrollTop + height) + 7),
});
