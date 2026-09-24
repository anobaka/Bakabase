"use client";

import type { StartupSurfaceStatus } from "@/components/Startup/startupQueue";

import { useEffect, useRef, useState } from "react";

import { ChangelogModal } from "@/components/Changelog";
import { useStartupSurface } from "@/components/Startup/startupQueue";
import BApi from "@/sdk/BApi";

const STORAGE_KEY = "bakabase.changelog.lastSeenVersion";

const readLastSeen = (): string | null => {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    // Private mode, or storage disabled — then this is simply never shown.
    return null;
  }
};

const writeLastSeen = (version: string) => {
  try {
    localStorage.setItem(STORAGE_KEY, version);
  } catch {
    // Nothing to do: at worst the notes are offered again next launch.
  }
};

interface Offer {
  version: string;
  /** This reader's true previous version, so every release they skipped is shown. */
  from: string;
}

/**
 * Finds out whether the running version has notes this browser has not been shown. A fresh
 * profile, a version already seen, or a version without notes records the version at once
 * and offers nothing; notes worth showing are recorded only once they are on screen.
 */
export const findWhatsNew = async (): Promise<Offer | null> => {
  const rsp = await BApi.app.getAppInfo();
  const version = rsp.data?.coreVersion;

  if (!version) return null;

  const lastSeen = readLastSeen();

  if (!lastSeen || lastSeen === version) {
    writeLastSeen(version);

    return null;
  }

  // Confirm there is something to read before opening a modal over the app: a dev
  // build, or a release older than the archive, has no notes.
  let hasNotes = false;

  try {
    hasNotes = !!(await BApi.changelog.getChangelog({ version })).data;
  } catch {
    // Unreachable archive: same as no notes.
  }

  if (!hasNotes) {
    // Recorded, so a version whose notes never arrive cannot re-ask on every launch.
    writeLastSeen(version);

    return null;
  }

  return { version, from: lastSeen };
};

/**
 * Shows the running version's release notes once, the first time the app runs
 * after an upgrade. A fresh install records the version silently instead — a
 * user who has never seen this app does not want a changelog as their first
 * screen. The marker is per-browser-profile rather than server state, so the
 * worst failure is showing (or skipping) the notes once.
 *
 * Waits its turn in the startup order (`startupQueue`): after the welcome and the notices.
 * The version is recorded when the notes are shown, not when they are found, so notes put
 * off until the next launch — the app closed first, or a notice's action took the user
 * somewhere — are still offered then.
 */
const WhatsNewGate = () => {
  const [offer, setOffer] = useState<Offer | null>();
  const handled = useRef(false);
  const status: StartupSurfaceStatus = offer === undefined ? "deciding" : offer ? "ready" : "idle";
  const { isTurn } = useStartupSurface("whatsNew", status);
  const shown = isTurn ? offer : null;

  useEffect(() => {
    if (handled.current) return;
    handled.current = true;

    findWhatsNew()
      .then(setOffer)
      .catch(() => setOffer(null));
  }, []);

  useEffect(() => {
    if (shown) writeLastSeen(shown.version);
  }, [shown]);

  return shown ? (
    <ChangelogModal
      visible
      from={shown.from}
      version={shown.version}
      onClose={() => setOffer(null)}
    />
  ) : null;
};

WhatsNewGate.displayName = "WhatsNewGate";

export default WhatsNewGate;
