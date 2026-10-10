import type { WindowOptions } from "./types";

/** Keep the initial window and its footer within the viewport. On small screens
 * the effective minimum must also fit, rather than forcing a negative position. */
export function initialWindowBounds(
  options: WindowOptions | undefined,
  viewportWidth: number,
  viewportHeight: number,
) {
  const availableWidth = Math.max(1, viewportWidth - 32);
  const availableHeight = Math.max(1, viewportHeight - 32);
  const minWidth = Math.min(options?.minWidth ?? 400, availableWidth);
  const minHeight = Math.min(options?.minHeight ?? 300, availableHeight);
  const width = Math.min(availableWidth, Math.max(minWidth, options?.initialSize?.width ?? 1000));
  const height = Math.min(
    availableHeight,
    Math.max(minHeight, options?.initialSize?.height ?? 700),
  );
  const x = Math.max(
    0,
    Math.min(viewportWidth - width, options?.initialPosition?.x ?? (viewportWidth - width) / 2),
  );
  const y = Math.max(
    0,
    Math.min(viewportHeight - height, options?.initialPosition?.y ?? (viewportHeight - height) / 2),
  );
  return { x, y, width, height, minWidth, minHeight };
}
