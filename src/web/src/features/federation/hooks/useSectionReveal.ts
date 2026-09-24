import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";

/** How long a revealed section stays marked, long enough to find it after the scroll. */
export const REVEAL_HIGHLIGHT_MS = 2500;

/**
 * Brings a section a link asked for (`?section=…`) into view, moves focus to it and marks
 * it for a moment.
 *
 * Waits for `ready`: a section scrolled to while the content above it is still loading
 * would be pushed back out of view the moment that content arrives. Runs again for every
 * navigation to the same URL — a notification clicked while its page is already open has
 * to land just as well as the first time — which is what the location key is for.
 *
 * Focus is part of arriving, not decoration: without it a keyboard or screen-reader user
 * is left at the top of a page that has scrolled somewhere else. The element needs
 * `tabIndex={-1}` for that; the scroll is left to `scrollIntoView` so the focus call
 * itself does not jump a second time.
 */
export function useSectionReveal<T extends HTMLElement>(requested: boolean, ready: boolean) {
  const ref = useRef<T>(null);
  const [highlighted, setHighlighted] = useState(false);
  const { key } = useLocation();

  useEffect(() => {
    const element = ref.current;

    if (!requested || !ready || !element) return;
    element.scrollIntoView?.({ block: "start" });
    element.focus?.({ preventScroll: true });
    setHighlighted(true);
    const timer = setTimeout(() => setHighlighted(false), REVEAL_HIGHLIGHT_MS);

    return () => clearTimeout(timer);
  }, [requested, ready, key]);

  return { ref, highlighted };
}

/** The mark a revealed section wears; the focus ring is replaced by it, not doubled. */
export const revealClass = (highlighted: boolean) =>
  `scroll-mt-2 outline-none transition-shadow duration-500 ${
    highlighted ? "ring-2 ring-primary ring-offset-2 ring-offset-background" : ""
  }`;
