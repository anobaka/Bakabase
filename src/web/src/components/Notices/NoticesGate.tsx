"use client";

import type { HelpTarget } from "@/components/HelpCenter/types";
import type { StartupSurfaceStatus } from "@/components/Startup/startupQueue";
import type { NoticeDefinition } from "./registry";

import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";

import { pendingNotices } from "./eligibility";
import NoticesDialog from "./NoticesDialog";
import { useNoticeStore, useNoticeViewer, useReadNoticeIds } from "./noticeStore";
import { notices } from "./registry";

import HelpCenterModal from "@/components/HelpCenter/HelpCenterModal";
import { useStartupSurface } from "@/components/Startup/startupQueue";

/**
 * Set when the dialog is closed with notices still unread. Session storage, so a reload
 * (switching the theme reloads the page) or a switch back from a managed server does not
 * bring it straight back — only the next launch does.
 */
export const NOTICES_DISMISSED_SESSION_KEY = "bakabase.notices.dismissedForSession";

const dismissedThisSession = () => {
  try {
    return sessionStorage.getItem(NOTICES_DISMISSED_SESSION_KEY) === "1";
  } catch {
    return false;
  }
};

const rememberDismissed = () => {
  try {
    sessionStorage.setItem(NOTICES_DISMISSED_SESSION_KEY, "1");
  } catch {
    // At worst they are offered again on the next page load.
  }
};

/**
 * Shows the notices this install has not read, at startup, in their turn (`startupQueue`):
 * after the welcome, before the release notes. Once per page load: when it is done — or
 * found nothing, or could not find out — it stays done until the next launch or reload
 * (`startupDone`), whatever the help center's list or a late answer about the viewer learns.
 *
 * Only where the viewer is one the notices are for (`noticeViewerOf`): never in a window
 * showing another server through the console relay — its acknowledgements would land on that
 * server — and never where nothing could be recorded.
 *
 * A notice's action counts as reading it. One that opens a help topic keeps the turn while
 * the guide is open and comes back to the rest afterwards. Going to a page from anywhere in
 * this flow — a notice's own action, or any link in the guide it opened, whichever topic the
 * reader moved on to — ends the startup dialogs for this launch: the remaining notices and
 * the release notes wait for the next one rather than open over the page the user asked for.
 */
const NoticesGate = () => {
  const viewer = useNoticeViewer();
  const loadStatus = useNoticeStore((store) => store.status);
  const state = useNoticeStore((store) => store.state);
  const load = useNoticeStore((store) => store.load);
  const markRead = useNoticeStore((store) => store.markRead);
  const startupDone = useNoticeStore((store) => store.startupDone);
  const finishStartup = useNoticeStore((store) => store.finishStartup);
  const readIds = useReadNoticeIds();
  const navigate = useNavigate();
  const [dismissed, setDismissed] = useState(dismissedThisSession);
  const [guide, setGuide] = useState<HelpTarget | null>(null);
  const engaged = !!viewer && !dismissed && !startupDone;

  useEffect(() => {
    if (engaged && viewer) void load(viewer);
  }, [engaged, viewer, load]);

  const pending = useMemo(
    () =>
      engaged && viewer && state ? pendingNotices(notices, { ...state, readIds }, viewer) : [],
    [engaged, viewer, state, readIds],
  );

  const status: StartupSurfaceStatus = startupDone
    ? "idle"
    : viewer === undefined
      ? "deciding"
      : !engaged
        ? "idle"
        : guide
          ? "ready"
          : loadStatus === "failed"
            ? "idle"
            : !state
              ? "deciding"
              : pending.length > 0
                ? "ready"
                : "idle";
  const { isTurn, deferRest } = useStartupSurface("notices", status);

  useEffect(() => {
    if (status === "idle") finishStartup();
  }, [status, finishStartup]);

  const dismiss = () => {
    rememberDismissed();
    setDismissed(true);
  };

  /** The reader is going to a page: nothing waiting after the notices may open over it. */
  const leaveFor = (path: string) => {
    deferRest();
    dismiss();
    setGuide(null);
    navigate(path);
  };

  const act = (notice: NoticeDefinition) => {
    void markRead([notice.id]);
    const action = notice.action;

    if (!action) return;
    if (action.kind === "help") {
      setGuide({ topic: action.topic, section: action.section });

      return;
    }
    leaveFor(action.route);
  };

  if (!isTurn || startupDone) return null;

  if (guide) {
    return (
      <HelpCenterModal
        visible
        section={guide.section}
        topic={guide.topic}
        onClose={() => setGuide(null)}
        onNavigate={leaveFor}
      />
    );
  }

  return pending.length > 0 ? (
    <NoticesDialog
      notices={pending}
      onAct={act}
      onDismiss={dismiss}
      onRead={(id) => void markRead([id])}
      onReadAll={() => void markRead(pending.map((notice) => notice.id))}
    />
  ) : null;
};

NoticesGate.displayName = "NoticesGate";

export default NoticesGate;
