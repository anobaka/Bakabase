import type { StartupSurfaceId } from "@/components/Startup/startupQueue";

import { useCallback, useState } from "react";

import { useStartupSurface } from "@/components/Startup/startupQueue";

const isCompleted = (storageKey: string) => {
  if (typeof localStorage === "undefined") return true;

  try {
    return !!localStorage.getItem(storageKey);
  } catch {
    // Storage unavailable — treat as completed to avoid nagging.
    return true;
  }
};

/**
 * Opens the help center automatically the first time a user visits a screen
 * that a topic covers. Completion is remembered per storage key.
 *
 * It takes its place in the startup order as `surface` (see `startupQueue`), so it never
 * opens over another dialog the app opened by itself — the dashboard's welcome goes first,
 * a page's own guide last.
 *
 * `deferRest` is for a host whose guide sends the reader to a page (`HelpCenterModal`'s
 * `onNavigate`): call it before completing, so nothing waiting after this guide opens over
 * the page the reader went to.
 */
export const useFirstRunHelp = (storageKey: string, surface: StartupSurfaceId = "pageGuide") => {
  const [pending, setPending] = useState(() => !isCompleted(storageKey));
  const { isTurn, deferRest } = useStartupSurface(surface, pending ? "ready" : "idle");

  const completeFirstRun = useCallback(() => {
    try {
      localStorage.setItem(storageKey, "true");
    } catch {
      // Ignore storage failures; the guide simply reappears next time.
    }
    setPending(false);
  }, [storageKey]);

  return {
    showFirstRun: pending && isTurn,
    completeFirstRun,
    deferRest,
  };
};

/** Storage key of the path mark first-run guide (kept from the legacy guide tour). */
export const PATH_MARK_FIRST_RUN_KEY = "bakabase-path-mark-guide-completed";

/**
 * Storage key of the welcome tour, deliberately the one the old onboarding carousel
 * used. Anyone who already finished that tour must not be shown the help center
 * again just because it replaced the carousel.
 */
export const GETTING_STARTED_FIRST_RUN_KEY = "bakabase-onboarding-completed";
