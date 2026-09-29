import type { RefObject } from "react";

import { useEffect, useRef } from "react";

/** Focus that is nowhere: on the page's body, or on an element no longer in the page. */
const nowhere = (element: Element | null) =>
  !element || element === document.body || !element.isConnected;

/** A control that can no longer hold focus: taken off the page, or disabled. */
const unusable = (element: Element) => !element.isConnected || !!element.matches?.(":disabled");

/** A confirmation is portalled over the page, but belongs to the control that opened it. */
const inDialog = (element: Element | null) => !!element?.closest?.('[role="alertdialog"]');

/** The heading of a part of the tab (`data-focus-section`), which takes focus when asked. */
const headingOf = (section: Element | null) =>
  section?.isConnected
    ? (section.querySelector<HTMLElement>("h2[tabindex], h3[tabindex], h4[tabindex]") ?? null)
    : null;

/**
 * Keeps the keyboard in a devices tab when an action takes away what had it — a row an
 * action removed, a form an answer hid, a control disabled while its action ran (Chromium
 * moves focus to the page's body at once). Focus goes back to that control when it is
 * usable again, else to the heading of the part of the tab it was in (a `data-focus-section`),
 * else to the tab's own heading.
 *
 * Taken from the device map's details (`map/useDetailsFocus.ts`), and just as careful: only
 * focus the keyboard has in the tab is kept. A pointer pressed anywhere — the click that
 * focuses the very button it presses included — or focus moved outside the tab lets go, and
 * focus is never pulled back from where the reader put it: after a click, a removed row
 * leaves focus where the browser leaves it, and the page does not scroll. A key pressed
 * afterwards hands what has focus in the tab back to the keeper.
 */
export function useSectionFocusKeeper(
  panel: RefObject<HTMLElement>,
  heading: RefObject<HTMLElement>,
) {
  // What had the keyboard in the tab, and the part of the tab it was in.
  const holder = useRef<Element | null>(null);
  const section = useRef<Element | null>(null);
  /** The holder was disabled while its action ran, which is what dropped focus. */
  const disabledWhileHeld = useRef(false);
  /**
   * The reader's last input was a pointer: focus it gave is theirs. A mouse click on a
   * button fires `pointerdown` and then focuses that button, so letting go on the press is
   * not enough — the focus that follows it must not be taken either.
   */
  const pointing = useRef(false);

  useEffect(() => {
    const release = () => {
      holder.current = null;
      section.current = null;
      disabledWhileHeld.current = false;
    };
    const hold = (target: Element) => {
      holder.current = target;
      section.current = target.closest("[data-focus-section]");
      disabledWhileHeld.current = false;
    };
    const check = () => {
      const held = holder.current;

      if (!held) return;
      if (held.isConnected && unusable(held)) {
        // Disabled while its action runs: wait for it to be usable again.
        disabledWhileHeld.current = true;

        return;
      }
      if (!nowhere(document.activeElement)) return;
      if (held.isConnected) {
        // Focus left a control that is still there only because it was disabled.
        if (disabledWhileHeld.current) (held as HTMLElement).focus?.();
        disabledWhileHeld.current = false;

        return;
      }
      const target = headingOf(section.current) ?? heading.current;

      release();
      target?.focus();
    };
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target instanceof Element ? event.target : null;

      if (target && panel.current?.contains(target)) {
        // Where a pointer put focus is the reader's, not the keeper's.
        if (pointing.current) release();
        else hold(target);

        return;
      }
      if (inDialog(target)) return;
      // The reader went elsewhere.
      release();
    };
    // The reader points somewhere: where focus goes next is theirs to decide.
    const onPointerDown = () => {
      pointing.current = true;
      release();
    };
    // The keyboard again: what has focus in the tab is kept from now on.
    const onKeyDown = () => {
      pointing.current = false;
      const active = document.activeElement;

      if (active && active !== holder.current && panel.current?.contains(active)) hold(active);
    };
    // A confirmation closing gives focus back to what opened it, or to nothing when that
    // control went with the action: then the part of the tab it was in takes it.
    const onFocusOut = () => setTimeout(check, 0);

    document.addEventListener("focusin", onFocusIn, true);
    document.addEventListener("focusout", onFocusOut, true);
    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    const observer =
      typeof MutationObserver === "function" && panel.current ? new MutationObserver(check) : null;

    observer?.observe(panel.current!, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["disabled"],
    });

    return () => {
      document.removeEventListener("focusin", onFocusIn, true);
      document.removeEventListener("focusout", onFocusOut, true);
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("keydown", onKeyDown, true);
      observer?.disconnect();
    };
  }, [panel, heading]);
}
