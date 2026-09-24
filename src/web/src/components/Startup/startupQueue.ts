import { useCallback, useEffect } from "react";
import { create } from "zustand";

/**
 * Every dialog that may open by itself when the app starts, in the order they take the
 * screen. **The one place that order is decided.** Only one is on screen at a time, and a
 * surface waits while any earlier one is still finding out whether it has something to show.
 *
 * - `gettingStarted` — the help center's welcome, the first time this browser opens the
 *   dashboard (`pages/dashboard`). First, because nothing after it should assume the reader
 *   knows the app.
 * - `notices` — notices shipped with the app that this install has not read yet
 *   (`components/Notices`). Short, and some ask for something to be done.
 * - `whatsNew` — the release notes of the update just installed
 *   (`components/Changelog/WhatsNewGate`). Long and only informative, so last of the three.
 * - `pageGuide` — a page's own first-visit guide (the path mark pages). Not a startup
 *   surface in itself, but a page can be where the window starts, and its guide must not
 *   open over any of the above. The page is still there once they are done.
 *
 * A surface that is not mounted takes no part: the welcome belongs to the dashboard, so a
 * window starting on another page goes straight to the notices.
 */
export const STARTUP_SURFACES = ["gettingStarted", "notices", "whatsNew", "pageGuide"] as const;

export type StartupSurfaceId = (typeof STARTUP_SURFACES)[number];

/**
 * What a surface says about itself:
 * - `deciding` — still finding out whether it has something to show; every later surface waits.
 * - `ready` — has something to show. Once given the turn it keeps it until it reports `idle`.
 * - `idle` — nothing to show, or done showing it.
 */
export type StartupSurfaceStatus = "deciding" | "ready" | "idle";

export interface StartupSlot {
  status: StartupSurfaceStatus;
  /** Put off until the next launch: it takes no turn, and nothing waits for it. */
  deferred?: boolean;
}

export type StartupSlots = Partial<Record<StartupSurfaceId, StartupSlot>>;

const takesPart = (slot?: StartupSlot): slot is StartupSlot => !!slot && !slot.deferred;

/**
 * Whose turn it is. The surface on screen keeps the screen until it is done — a surface
 * that arrives later but comes earlier in the order waits instead of opening over it.
 * Otherwise the first surface in order that is ready, unless one before it is still deciding.
 */
export const nextStartupSurface = (
  slots: StartupSlots,
  current: StartupSurfaceId | null,
): StartupSurfaceId | null => {
  if (current) {
    const slot = slots[current];

    if (takesPart(slot) && slot.status !== "idle") return current;
  }

  for (const id of STARTUP_SURFACES) {
    const slot = slots[id];

    if (!takesPart(slot)) continue;
    if (slot.status === "deciding") return null;
    if (slot.status === "ready") return id;
  }

  return null;
};

interface StartupQueueState {
  slots: StartupSlots;
  /** The surface whose turn it is, if any. */
  current: StartupSurfaceId | null;
  report: (id: StartupSurfaceId, status: StartupSurfaceStatus) => void;
  leave: (id: StartupSurfaceId) => void;
  /** Puts off every surface after `id` that is waiting now, until the next launch. */
  deferAfter: (id: StartupSurfaceId) => void;
}

let settling = false;

/**
 * Hands out the turn once the reports of one render are all in, not after each of them.
 * Components mounted together report in effect order, which is tree order rather than
 * startup order: settling after each report would give the turn to whichever `ready` came
 * first — and the turn, once given, is kept.
 */
const settleSoon = () => {
  if (settling) return;
  settling = true;
  queueMicrotask(() => {
    settling = false;
    useStartupQueue.setState((state) => {
      const current = nextStartupSurface(state.slots, state.current);

      return current === state.current ? state : { current };
    });
  });
};

export const useStartupQueue = create<StartupQueueState>((set) => ({
  slots: {},
  current: null,
  report: (id, status) => {
    set((state) =>
      state.slots[id]?.status === status
        ? state
        : { slots: { ...state.slots, [id]: { ...state.slots[id], status } } },
    );
    settleSoon();
  },
  leave: (id) => {
    set((state) => {
      if (!state.slots[id]) return state;
      const slots: StartupSlots = { ...state.slots };

      delete slots[id];

      return { slots };
    });
    settleSoon();
  },
  deferAfter: (id) => {
    set((state) => {
      const slots: StartupSlots = { ...state.slots };

      for (const later of STARTUP_SURFACES.slice(STARTUP_SURFACES.indexOf(id) + 1)) {
        const slot = slots[later];

        if (slot && slot.status !== "idle") slots[later] = { ...slot, deferred: true };
      }

      return { slots };
    });
    settleSoon();
  },
}));

/**
 * Takes part in the startup order as `id` for as long as the calling component is mounted.
 *
 * `status` is re-reported whenever it changes, so a surface drives its whole life through it:
 * `deciding` while it loads, `ready` when it has something, `idle` when it is done — which
 * hands the turn on. `isTurn` says when to show.
 */
export const useStartupSurface = (id: StartupSurfaceId, status: StartupSurfaceStatus) => {
  const isTurn = useStartupQueue((state) => state.current === id);

  useEffect(() => {
    useStartupQueue.getState().report(id, status);
  }, [id, status]);

  useEffect(() => () => useStartupQueue.getState().leave(id), [id]);

  /** After this surface sent the user somewhere: whatever waits after it can wait for the next launch. */
  const deferRest = useCallback(() => useStartupQueue.getState().deferAfter(id), [id]);

  return { isTurn, deferRest };
};
