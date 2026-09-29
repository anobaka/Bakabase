import type { PairingRequest, SharingCandidate } from "./types";
import type { DevicesPageContextValue } from "./devices/context";
import type { DevicesAnchor } from "./switching";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { AiOutlineLaptop, AiOutlineReload } from "react-icons/ai";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  FederationAccess,
  panelClass,
} from "./components/common";
import ConfirmDialog from "./components/ConfirmDialog";
import { REVEAL_HIGHLIGHT_MS, useSectionReveal } from "./hooks/useSectionReveal";
import { useDevicesData } from "./hooks/useDevicesData";
import { federationPeerApi } from "./peerApi";
import { FederationError, isAbort } from "./transport";
import ManagementAccessSection from "./components/ManagementAccess";
import { DevicesPageContext } from "./devices/context";
import DevicesNav from "./devices/DevicesNav";
import { devicesAnchors, devicesTabs, resolveSection } from "./devices/sections";
import { useSectionFocusKeeper } from "./devices/useSectionFocusKeeper";

import { useCanAdministerShownServer } from "@/stores/remoteAccess";

/** How often outgoing requests are claimed while the page is visible. */
const CLAIM_POLL_MS = 4000;
/** A request whose claim failed (e.g. its device is unreachable) is left alone this long. */
const CLAIM_BACKOFF_MS = 30_000;

interface Confirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
}

const isAwaitingOutgoing = (request: PairingRequest, at: number) =>
  request.direction === "outgoing" &&
  request.status === "awaitingApproval" &&
  Date.parse(request.expiresAt) > at;

/**
 * Changes with every navigation that lands on the management section — the notification
 * followed again while the page is open is the same URL, but a new location key.
 */
const managementLinkKey = (section: string | null, locationKey: string) =>
  section === "management" ? locationKey : "";

export default function DevicesPage() {
  return (
    <FederationAccess elsewhere={<ShownServerManagement />}>
      <Devices />
    </FederationAccess>
  );
}

/**
 * Where the rest of this page is not available — the desktop app showing a server it
 * manages, a browser — the one part that still applies is whether other
 * devices may manage the server the window shows. A headless server's management requests
 * are answered exactly there, and its notification links here.
 *
 * Only where the window may decide that: a paired window or an Unrestricted server. An
 * ordinary browser on a paired-only server would only be told it is not allowed to look.
 */
function ShownServerManagement() {
  const [params] = useSearchParams();
  const { key: locationKey } = useLocation();
  const allowed = useCanAdministerShownServer();
  const [settled, setSettled] = useState(false);
  const section = params.get("section");
  const { ref, highlighted } = useSectionReveal<HTMLElement>(section === "management", settled);

  if (!allowed) return null;

  return (
    <ManagementAccessSection
      highlighted={highlighted}
      reloadKey={managementLinkKey(section, locationKey)}
      sectionRef={ref}
      onSettled={() => setSettled(true)}
    />
  );
}

/**
 * This device's own devices page, in sections: this device, management, library sharing
 * and advanced (see `devices/sections.ts`). Everything is read once here and handed to the
 * tab shown, so switching tabs never waits and every tab's count stays live in the nav.
 */
function Devices() {
  const { t } = useTranslation();
  const data = useDevicesData();
  const { status, sharingError: loadError, sharingLoading: loading, refreshSharing } = data;
  const [params] = useSearchParams();
  const { key: locationKey } = useLocation();
  const sectionParam = params.get("section");
  const { tab, anchor, explicit } = resolveSection(sectionParam);
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [notice, setNotice] = useState<string>();
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [shareBack, setShareBack] = useState(true);
  const [configureRemote, setConfigureRemote] = useState<boolean>();
  const [invite, setInvite] = useState<{ code: string; expiresAt: string }>();
  const [discovered, setDiscovered] = useState<SharingCandidate[]>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  const [now, setNow] = useState(Date.now());
  const [revealed, setRevealed] = useState<DevicesAnchor | null>(null);
  const claimBackoff = useRef(new Map<string, number>());
  const panelRef = useRef<HTMLDivElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const latest = useRef({ status, refreshSharing, t });

  latest.current = { status, refreshSharing, t };

  useEffect(() => {
    mounted.current = true;
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => {
      mounted.current = false;
      clearInterval(timer);
    };
  }, []);

  useSectionFocusKeeper(panelRef, headingRef);

  // A link arriving: a place inside a tab is brought into view, focused and marked for a
  // moment once what it waits for has loaded; a tab alone gets its heading focused. Again
  // for every navigation, the same link followed again included.
  const ready = anchor ? devicesAnchors[anchor].ready(data) : true;

  useEffect(() => {
    if (!explicit || !ready) return;
    const element = anchor ? document.getElementById(devicesAnchors[anchor].elementId) : undefined;

    if (element) {
      element.scrollIntoView?.({ block: "start" });
      element.focus?.({ preventScroll: true });
      setRevealed(anchor);
      const timer = setTimeout(() => setRevealed(null), REVEAL_HIGHLIGHT_MS);

      // Leaving for another place ends the mark too, so it is never left on.
      return () => {
        clearTimeout(timer);
        setRevealed(null);
      };
    }
    // A tab, or a place that is not there (a request already decided): the tab's heading.
    const heading = headingRef.current;

    heading?.scrollIntoView?.({ block: "nearest" });
    heading?.focus({ preventScroll: true });

    return undefined;
    // `data` changes on every read; `ready` is what this waits for.
  }, [explicit, ready, anchor, tab, locationKey]);

  // The "wants to manage this device" notification followed again while the page is open:
  // the request it announces may not have been read yet.
  const firstLocationKey = useRef(locationKey);

  useEffect(() => {
    if (anchor === "management" && locationKey !== firstLocationKey.current)
      void data.loadAccess({ quiet: true });
  }, [locationKey]);

  /** User-initiated actions: one at a time, with their failure reported to `onError`. */
  const run = async (
    operation: () => Promise<unknown>,
    onError: (cause: Error) => void = setError,
  ) => {
    if (busyRef.current) return false;
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    setConfirmationError(undefined);
    try {
      await operation();
      // Turning sharing on can turn remote access on as well: both are read again.
      if (mounted.current) await data.reload(["sharing", "access"]);

      return true;
    } catch (cause) {
      if (mounted.current) {
        onError(cause instanceof Error ? cause : new Error(String(cause)));
        if (cause instanceof FederationError && cause.code === "PathMappingsChanged")
          await data.reload(["sharing"]);
      }

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  // The server also claims approved requests in the background, so an outgoing request can
  // be decided between two refreshes without this page's own claim ever seeing it.
  const outgoingSeen = useRef(new Map<string, string>());

  useEffect(() => {
    const seen = outgoingSeen.current;

    for (const request of status?.requests ?? []) {
      if (request.direction !== "outgoing") continue;
      if (
        seen.get(request.requestId) === "awaitingApproval" &&
        (request.status === "granted" || request.status === "rejected")
      ) {
        setNotice(t(`federation.pair.${request.status}`));
        if (request.status === "granted") setCode("");
      }
      seen.set(request.requestId, request.status);
    }
  }, [status?.requests]);

  // Claims outgoing requests in the background. It reads the latest render through a ref so
  // a status change never restarts it, and it never touches the busy flag, action errors or
  // the user's inputs: it only reports a request that has just been decided, then re-reads
  // status quietly. Status itself is re-read on its own by the page's data.
  useEffect(() => {
    const controller = new AbortController();
    let claiming = false;

    const claimPending = async () => {
      if (claiming || document.hidden || busyRef.current) return;
      const at = Date.now();
      const due = (latest.current.status?.requests ?? []).filter(
        (request) =>
          isAwaitingOutgoing(request, at) &&
          (claimBackoff.current.get(request.requestId) ?? 0) <= at,
      );

      if (!due.length) return;
      claiming = true;
      try {
        for (const request of due) {
          try {
            const result = await federationPeerApi.claim(request.requestId, controller.signal);

            claimBackoff.current.delete(request.requestId);
            if (controller.signal.aborted) return;
            if (result.outcome === "granted" || result.outcome === "rejected")
              setNotice(latest.current.t(`federation.pair.${result.outcome}`));
          } catch (cause) {
            if (controller.signal.aborted || isAbort(cause)) return;
            // An unreachable device would otherwise be hit (and time out) on every tick.
            claimBackoff.current.set(request.requestId, Date.now() + CLAIM_BACKOFF_MS);
          }
        }
        if (!controller.signal.aborted) await latest.current.refreshSharing({ quiet: true });
      } finally {
        claiming = false;
      }
    };
    const claimTimer = setInterval(() => void claimPending(), CLAIM_POLL_MS);

    return () => {
      controller.abort();
      clearInterval(claimTimer);
    };
  }, []);

  const confirm = (
    title: string,
    description: string,
    action: () => Promise<unknown>,
    warning?: string,
  ) => {
    setConfirmationError(undefined);
    setConfirmation({ title, description, warning, action });
  };
  const closeConfirmation = () => {
    setConfirmation(undefined);
    setConfirmationError(undefined);
  };
  // A corrupt sharing state never produces a status, so its recovery cannot live behind one.
  const sharingStateUnavailable =
    loadError instanceof FederationError && loadError.code === "SharingStateUnavailable";
  const pageLoadError = sharingStateUnavailable ? undefined : loadError;

  const context: DevicesPageContextValue = {
    data,
    busy,
    run,
    confirm,
    setNotice,
    now,
    mounted,
    claimBackoff,
    sharingForm: {
      address,
      setAddress,
      code,
      setCode,
      shareBack,
      setShareBack,
      configureRemote,
      setConfigureRemote,
      invite,
      setInvite,
      discovered,
      setDiscovered,
    },
    tab,
    anchor,
    locationKey,
    revealed,
    tabs: devicesTabs,
    headingRef,
  };
  const Panel = useMemo(
    () => devicesTabs.find((entry) => entry.id === tab)?.Panel ?? devicesTabs[0].Panel,
    [tab],
  );

  return (
    <div className="@container mx-auto flex max-w-[1200px] flex-col gap-5 p-4 sm:p-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineLaptop aria-hidden />
            {t("federation.devices.title")}
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("federation.devices.intro")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            className={buttonClass}
            disabled={loading || busy}
            type="button"
            onClick={() => void data.refreshAll()}
          >
            <AiOutlineReload aria-hidden />
            {t("federation.refresh")}
          </button>
          <Link className={buttonClass} to="/federation/map">
            {t("federation.map.title")}
          </Link>
          <Link className={buttonClass} to="/federation">
            {t("federation.title")}
          </Link>
        </div>
      </header>
      {(pageLoadError || error || notice) && (
        // Actions are spread over the tabs; keep their outcome in view wherever the user is.
        <div
          className="sticky top-0 z-10 -mx-1 space-y-2 bg-background/95 px-1 py-1 backdrop-blur"
          data-testid="federation-feedback"
        >
          <ErrorNotice error={pageLoadError} onRetry={() => void data.reload(["sharing"])} />
          <ErrorNotice error={error} onDismiss={() => setError(undefined)} />
          {notice && (
            <div
              className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
              role="status"
            >
              <p>{notice}</p>
              <DismissButton onClick={() => setNotice(undefined)} />
            </div>
          )}
        </div>
      )}
      {sharingStateUnavailable && (
        <section
          aria-labelledby="federation-recovery-title"
          className={`${panelClass} space-y-3 !border-danger/40`}
        >
          <h2 className="font-semibold" id="federation-recovery-title">
            {t("federation.recovery.title")}
          </h2>
          <p className="text-sm">{t("federation.recovery.description")}</p>
          <p className="text-xs text-default-500">{loadError.code}</p>
          <div className="flex flex-wrap gap-2">
            <button
              className={buttonClass}
              disabled={loading || busy}
              type="button"
              onClick={() => void data.reload(["sharing"])}
            >
              {t("federation.retry")}
            </button>
            <button
              className={`${buttonClass} text-danger`}
              disabled={busy}
              type="button"
              onClick={() =>
                confirm(
                  t("federation.recovery.reset"),
                  t("federation.recovery.confirm"),
                  async () => {
                    // Only the sharing state was lost: the install keeps its identity.
                    await federationPeerApi.resetIdentity(true, false);
                    if (mounted.current) setInvite(undefined);
                  },
                )
              }
            >
              {t("federation.recovery.reset")}
            </button>
          </div>
        </section>
      )}
      <DevicesPageContext.Provider value={context}>
        <div className="grid gap-5 @3xl:grid-cols-[12rem_1fr]">
          <DevicesNav active={tab} data={data} tabs={devicesTabs} />
          <div
            ref={panelRef}
            aria-labelledby="devices-panel-title"
            className="flex min-w-0 flex-col gap-5"
            data-section={tab}
            data-testid="devices-panel"
            role="region"
          >
            <Panel />
          </div>
        </div>
      </DevicesPageContext.Provider>
      {confirmation && (
        <ConfirmDialog
          busy={busy}
          description={confirmation.description}
          error={confirmationError}
          title={confirmation.title}
          warning={confirmation.warning}
          onCancel={closeConfirmation}
          onConfirm={() => {
            const { action } = confirmation;

            // A failure stays in the dialog, next to the decision the user just made.
            void run(async () => {
              await action();
              if (mounted.current) setConfirmation(undefined);
            }, setConfirmationError);
          }}
        />
      )}
    </div>
  );
}
