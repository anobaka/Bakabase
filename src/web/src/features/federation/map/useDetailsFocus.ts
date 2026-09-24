import type { RefObject } from "react";

import { useCallback, useEffect, useRef } from "react";

/** Inside the details, or in one of their confirmations, which are portalled over the page. */
export const inDetails = (element: Element | null | undefined, details: HTMLElement | null) =>
  !!element && (!!details?.contains(element) || !!element.closest?.('[role="alertdialog"]'));

/** Focus that is nowhere: on the page's body, or on an element no longer in the page. */
const nowhere = (element: Element | null) =>
  !element || element === document.body || !element.isConnected;

/** A control that can no longer hold focus: taken off the page, or disabled. */
const unusable = (element: Element) => !element.isConnected || !!element.matches?.(":disabled");

/** An action that began with the keyboard in the details. */
interface Action {
  element: Element | null;
  over: boolean;
}

/**
 * Keeps the keyboard in the details when the details themselves take away what had it: a
 * control disabled while its action runs (Chromium moves focus off it to the page's body at
 * once), a record an action or a refresh replaced, a form an answer hid, a message dismissed.
 * Focus then goes back to the action's own control when it is still there and usable, else to
 * the heading of what the details show now.
 *
 * Only focus the reader left in the details is kept: a pointer pressed anywhere, or focus
 * moved outside them, lets go — whatever happens to the details after that, focus is never
 * taken from where the reader put it.
 *
 * Checked on every change to the page's own DOM (so also when only a part of the details
 * re-renders) and when the page renders; `actionEnded` asks the page to render.
 */
export function useDetailsFocus({
  page,
  details,
  heading,
  actionEnded,
}: {
  /** What contains the details: its changes are watched. */
  page: RefObject<HTMLElement>;
  details: RefObject<HTMLElement>;
  heading: RefObject<HTMLElement>;
  /** Called when an action is over, for the page to render once it has taken its effects. */
  actionEnded: () => void;
}) {
  // What has the keyboard in the details, as the reader or the details put it there.
  const holder = useRef<Element | null>(null);
  const action = useRef<Action>();

  const check = useCallback(() => {
    const active = document.activeElement;
    const pending = action.current;

    if (pending) {
      // Its control is disabled while it runs: nothing to do before it is over.
      if (!pending.over) return;
      action.current = undefined;
      if (!nowhere(active)) return;
      const back = pending.element;
      const target =
        back && !unusable(back) && inDetails(back, details.current) ? back : heading.current;

      holder.current = null;
      (target as HTMLElement | null)?.focus?.();

      return;
    }
    const held = holder.current;

    if (held && unusable(held) && nowhere(active)) {
      holder.current = null;
      heading.current?.focus();
    }
  }, [details, heading]);

  useEffect(() => {
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target instanceof Element ? event.target : null;

      if (inDetails(target, details.current)) {
        holder.current = target;

        return;
      }
      // The reader went elsewhere.
      holder.current = null;
      action.current = undefined;
    };
    // The reader points somewhere: where focus goes next is theirs to decide.
    const onPointerDown = () => {
      holder.current = null;
      action.current = undefined;
    };

    document.addEventListener("focusin", onFocusIn, true);
    document.addEventListener("pointerdown", onPointerDown, true);
    const observer =
      typeof MutationObserver === "function" && page.current ? new MutationObserver(check) : null;

    observer?.observe(page.current!, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["disabled"],
    });

    return () => {
      document.removeEventListener("focusin", onFocusIn, true);
      document.removeEventListener("pointerdown", onPointerDown, true);
      observer?.disconnect();
    };
  }, [page, details, check]);

  // After every render of the page too: a change can land without touching the watched DOM.
  useEffect(() => {
    check();
  });

  return {
    /**
     * An action begins — before its control is disabled while it runs, which moves Chromium's
     * focus off it to the page's body at once. Never read while rendering, by when it has.
     */
    actionStarted: useCallback(() => {
      const active = document.activeElement;

      action.current = inDetails(active, details.current)
        ? { element: active, over: false }
        : undefined;
    }, [details]),
    /** It is over, listings re-read: focus goes back once the page has drawn the result. */
    actionOver: useCallback(() => {
      if (!action.current) return;
      action.current.over = true;
      actionEnded();
    }, [actionEnded]),
    /** The reader chose something else, or closed the details: nothing is kept any more. */
    release: useCallback(() => {
      action.current = undefined;
      holder.current = null;
    }, []),
  };
}
