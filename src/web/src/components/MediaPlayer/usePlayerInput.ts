import { useEffect, useRef, type RefObject } from "react";

import { WindowManager } from "@/components/Window/WindowManager";
import { MediaType } from "@/sdk/constants";

const CONTROL_SELECTOR =
  'input, textarea, select, video, audio, [contenteditable]:not([contenteditable="false"]), [role="slider"], [role="spinbutton"]';
const BUTTON_SELECTOR = 'button, a[href], [role="button"]';
const LIST_SELECTOR =
  '.media-player-file-list, [role="listbox"], [role="menu"], [role="menuitem"], [role="tab"], [aria-haspopup]:not([aria-haspopup="false"])';
const WHEEL_THRESHOLD = 50;
const WHEEL_COOLDOWN = 250;
const WHEEL_GESTURE_GAP = 200;

const elementTarget = (target: EventTarget | null) => (target instanceof Element ? target : null);

/** Keep native controls and scrollable content in charge of their own input. */
const hasOwnInput = (target: Element, scope: HTMLElement, protectButtons = true) => {
  if (target.closest(CONTROL_SELECTOR) || target.closest(LIST_SELECTOR)) return true;
  if (protectButtons && target.closest(BUTTON_SELECTOR)) return true;
  for (
    let current: Element | null = target;
    current && current !== scope;
    current = current.parentElement
  ) {
    if (!(current instanceof HTMLElement)) continue;
    const overflow = getComputedStyle(current).overflowY;

    if (/auto|scroll|overlay/.test(overflow) && current.scrollHeight > current.clientHeight)
      return true;
  }

  return false;
};

interface PlayerInputOptions {
  mediaType?: MediaType;
  canGoPrevious: boolean;
  canGoNext: boolean;
  previous: () => void;
  next: () => void;
  togglePlaying: () => void;
}

/** A floating player's title, content and footer share one input scope. Embedded
 * players keep their own region as the scope instead of listening globally. */
export const usePlayerInput = (root: RefObject<HTMLDivElement>, options: PlayerInputOptions) => {
  const current = useRef(options);

  current.current = options;

  useEffect(() => {
    const player = root.current;

    if (!player) return;
    const scope = player.closest<HTMLElement>("[data-bakabase-window-id]") ?? player;
    const windowId = scope.dataset.bakabaseWindowId;
    const manager = WindowManager.getInstance();
    const isActive = () =>
      !document.querySelector('[role="dialog"][aria-modal="true"]') &&
      (!windowId || (manager.isTopmost(windowId) && !manager.getWindow(windowId)?.isMinimized));
    let accumulated = 0;
    let lastWheel = -Infinity;
    let lastNavigation = -Infinity;
    let restoreAfterGesture = false;

    const focusPlayer = (event: MouseEvent | PointerEvent) => {
      const target = elementTarget(event.target);

      if (event.defaultPrevented || event.button !== 0 || !target || hasOwnInput(target, scope))
        return;
      // Dragging a non-focusable title must not leave focus on the page or on a
      // different player. Controls still retain their usual native focus.
      restoreAfterGesture = true;
      player.focus({ preventScroll: true });
    };
    const finishGesture = () => {
      if (!restoreAfterGesture) return;
      restoreAfterGesture = false;
      // The browser's default mousedown action can undo the initial focus.
      // Restore after release without cancelling native dragging or touch.
      if (player.isConnected && isActive()) player.focus({ preventScroll: true });
    };
    const cancelGesture = () => {
      restoreAfterGesture = false;
    };
    const onKeyDown = (event: KeyboardEvent) => {
      const target = elementTarget(event.target);

      if (
        event.defaultPrevented ||
        !isActive() ||
        !target ||
        hasOwnInput(target, scope, event.key !== "ArrowLeft" && event.key !== "ArrowRight") ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        event.isComposing
      )
        return;
      const state = current.current;

      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        event.preventDefault();
        if (event.key === "ArrowLeft" && state.canGoPrevious) state.previous();
        if (event.key === "ArrowRight" && state.canGoNext) state.next();
      } else if (
        event.key === " " &&
        [MediaType.Video, MediaType.Audio].includes(state.mediaType as MediaType)
      ) {
        event.preventDefault();
        state.togglePlaying();
      }
    };
    const onWheel = (event: WheelEvent) => {
      const target = elementTarget(event.target);
      const state = current.current;

      if (
        event.defaultPrevented ||
        !isActive() ||
        state.mediaType !== MediaType.Image ||
        !target ||
        hasOwnInput(target, scope, false) ||
        event.ctrlKey ||
        event.metaKey ||
        event.altKey ||
        event.shiftKey ||
        Math.abs(event.deltaX) > Math.abs(event.deltaY) ||
        !event.deltaY
      ) {
        accumulated = 0;

        return;
      }
      const forward = event.deltaY > 0;

      if (!(forward ? state.canGoNext : state.canGoPrevious)) {
        accumulated = 0;

        return;
      }
      event.preventDefault();
      const now = performance.now();

      if (now - lastNavigation < WHEEL_COOLDOWN) {
        accumulated = 0;
        lastWheel = now;

        return;
      }
      const delta =
        event.deltaY *
        (event.deltaMode === WheelEvent.DOM_DELTA_LINE
          ? 20
          : event.deltaMode === WheelEvent.DOM_DELTA_PAGE
            ? Math.max(scope.clientHeight, 100)
            : 1);

      if (now - lastWheel > WHEEL_GESTURE_GAP || Math.sign(accumulated) !== Math.sign(delta))
        accumulated = 0;
      accumulated += delta;
      lastWheel = now;
      if (Math.abs(accumulated) < WHEEL_THRESHOLD) return;
      accumulated = 0;
      lastNavigation = now;
      if (forward) state.next();
      else state.previous();
    };

    scope.addEventListener("pointerdown", focusPlayer, true);
    scope.addEventListener("mousedown", focusPlayer, true);
    scope.addEventListener("keydown", onKeyDown);
    document.addEventListener("pointerup", finishGesture, true);
    document.addEventListener("mouseup", finishGesture, true);
    document.addEventListener("pointercancel", cancelGesture, true);
    // A non-passive native listener is needed to keep image gestures from
    // scrolling the page; React's delegated wheel listener may be passive.
    scope.addEventListener("wheel", onWheel, { passive: false });

    return () => {
      scope.removeEventListener("pointerdown", focusPlayer, true);
      scope.removeEventListener("mousedown", focusPlayer, true);
      scope.removeEventListener("keydown", onKeyDown);
      scope.removeEventListener("wheel", onWheel);
      document.removeEventListener("pointerup", finishGesture, true);
      document.removeEventListener("mouseup", finishGesture, true);
      document.removeEventListener("pointercancel", cancelGesture, true);
    };
  }, [root]);
};
