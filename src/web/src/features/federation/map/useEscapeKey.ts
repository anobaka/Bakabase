import type { RefObject } from "react";

import { useEffect, useRef } from "react";

/**
 * Escape pressed anywhere inside `ref` calls `onEscape` — the way out of a selection or a
 * panel without reaching for the mouse. A listener on the element rather than a key handler
 * on it: the element is a region, not a control, and has no keyboard behaviour of its own.
 */
export function useEscapeKey<T extends HTMLElement>(
  ref: RefObject<T>,
  onEscape: () => void,
  enabled = true,
) {
  const latest = useRef(onEscape);

  latest.current = onEscape;

  useEffect(() => {
    const element = ref.current;

    if (!element || !enabled) return;
    const listener = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || event.defaultPrevented) return;
      event.preventDefault();
      latest.current();
    };

    element.addEventListener("keydown", listener);

    return () => element.removeEventListener("keydown", listener);
  }, [ref, enabled]);
}
