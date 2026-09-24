import type { FreshInstallFacts, NoticeState } from "./eligibility";
import type { FreshInstallFact, NoticeAudience } from "./registry";

import { useMemo } from "react";
import { create } from "zustand";

import {
  freshInstallFactsAsked,
  noticeViewerOf,
  toNoticeState,
  upgradeOnlyNoticeIds,
} from "./eligibility";
import { notices } from "./registry";

import { managedServerApi } from "@/features/federation/serverApi";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

type LoadStatus = "idle" | "loading" | "loaded" | "failed";

interface NoticeStore {
  status: LoadStatus;
  /** The server's last answer. */
  state?: NoticeState;
  /**
   * Marked read in this window. They count as read here at once, and stay read here even if
   * saving failed — the request's own error says so, and the notice returns next launch
   * rather than straight away.
   */
  readHere: string[];
  /**
   * Whether the startup dialog (`NoticesGate`) has had its turn in this page load: it showed
   * what there was, found nothing, was put off, or could not find out. It never takes another
   * — neither the help center's list retrying a load that failed at startup nor learning only
   * later who is looking may open a startup dialog in the middle of a session. Kept here
   * rather than in the gate, so a gate mounted again (a page outside the app's layout and
   * back) does not start over either; a reload does.
   */
  startupDone: boolean;
  finishStartup: () => void;
  /**
   * Reads what this install has recorded. The first UI a fresh install shows also records
   * its baseline here: every upgrade-only notice it ships with, as read — less those the
   * install turns out to be for after all (`showOnFreshInstallWhen`), asked as `viewer`.
   */
  load: (viewer: NoticeAudience) => Promise<void>;
  markRead: (ids: string[]) => Promise<void>;
}

const succeeded = <T>(rsp: { code?: number; data?: T }): rsp is { code?: number; data: T } =>
  !rsp.code && rsp.data != undefined;

/**
 * Asks this install about each fact in `asked`, where `viewer` can ask. Never throws: a fact
 * that cannot be learned is left out, which reads as not holding — the notice that asked
 * stays upgrade-only, the rule for every fresh install.
 */
export const learnFreshInstallFacts = async (
  asked: FreshInstallFact[],
  viewer: NoticeAudience,
): Promise<FreshInstallFacts> => {
  const facts: FreshInstallFacts = {};

  // The pairings live with the desktop app's relay manager, which only this install's own
  // window may ask about (`/federation/local`). A headless server answers that it manages
  // nothing. The import runs as the host starts, long before the window opens.
  if (asked.includes("thinClientPairingsImported") && viewer === "local") {
    try {
      const listing = await managedServerApi.list();

      facts.thinClientPairingsImported = (listing.servers ?? []).some(
        (server) => server.importedFromLegacyClient,
      );
    } catch {
      // Unknown: stays upgrade-only.
    }
  }

  return facts;
};

/**
 * Notice state shared by the startup dialog and the help center's list, so reading a notice
 * in one shows in the other. Only ever loaded where {@link useNoticeViewer} is not null: a
 * page showing another server, or one that could not record anything, never asks.
 */
export const useNoticeStore = create<NoticeStore>((set, get) => ({
  status: "idle",
  readHere: [],
  startupDone: false,
  finishStartup: () => {
    if (!get().startupDone) set({ startupDone: true });
  },
  load: async (viewer) => {
    const { status } = get();

    if (status === "loading" || status === "loaded") return;
    set({ status: "loading" });

    try {
      // Quietly: failing to learn about notices must not greet anyone with an error.
      const rsp = await BApi.options.getUiOptions({ showErrorToast: false });

      if (rsp.code) throw new Error(rsp.message);
      let state = toNoticeState(rsp.data?.notices);

      if (state.baselinePending) {
        try {
          const facts = await learnFreshInstallFacts(freshInstallFactsAsked(notices), viewer);
          const captured = await BApi.options.captureNoticeBaseline(
            upgradeOnlyNoticeIds(notices, facts),
            { showErrorToast: false },
          );

          if (succeeded(captured)) state = toNoticeState(captured.data);
        } catch {
          // Still pending: upgrade-only notices stay hidden, and the next load tries again.
        }
      }

      set({ status: "loaded", state });
    } catch {
      set({ status: "failed" });
    }
  },
  markRead: async (ids) => {
    const fresh = ids.filter((id) => !get().readHere.includes(id));

    if (fresh.length == 0) return;
    set((store) => ({ readHere: [...store.readHere, ...fresh] }));

    try {
      const rsp = await BApi.options.markNoticesRead(fresh);

      if (succeeded(rsp)) set({ state: toNoticeState(rsp.data) });
    } catch {
      // Reported by the request itself.
    }
  },
}));

/** Who is looking, for notices: see `noticeViewerOf`. */
export const useNoticeViewer = () => useRemoteAccessStore(noticeViewerOf);

/**
 * Every notice read on this install as far as this window knows: the server's answer, what
 * any window marked since (the server pushes the UI options to every window when they
 * change), and what was marked here. Read ids only ever grow, so the union is always right.
 */
export const useReadNoticeIds = () => {
  const answered = useNoticeStore((store) => store.state?.readIds);
  const readHere = useNoticeStore((store) => store.readHere);
  const pushed = useUiOptionsStore((store) => store.data?.notices?.readIds);

  return useMemo(
    () => [...new Set([...(answered ?? []), ...(pushed ?? []), ...readHere])],
    [answered, pushed, readHere],
  );
};
