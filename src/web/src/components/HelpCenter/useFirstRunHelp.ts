import { useCallback, useEffect, useState } from "react";

/**
 * Opens the help center automatically the first time a user visits a screen
 * that a topic covers. Completion is remembered per storage key.
 */
export const useFirstRunHelp = (storageKey: string) => {
  const [showFirstRun, setShowFirstRun] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined" || typeof localStorage === "undefined") {
      return;
    }

    let completed: string | null = null;

    try {
      completed = localStorage.getItem(storageKey);
    } catch {
      // Storage unavailable — treat as completed to avoid nagging.
      completed = "true";
    }

    if (!completed) {
      setShowFirstRun(true);
    }
  }, [storageKey]);

  const completeFirstRun = useCallback(() => {
    try {
      localStorage.setItem(storageKey, "true");
    } catch {
      // Ignore storage failures; the guide simply reappears next time.
    }
    setShowFirstRun(false);
  }, [storageKey]);

  return {
    showFirstRun,
    completeFirstRun,
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
