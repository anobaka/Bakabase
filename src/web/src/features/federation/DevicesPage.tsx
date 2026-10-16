import type { DevicesPageContextValue } from "./devices/context";
import type { DevicesAnchor } from "./switching";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
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
import { FederationError } from "./transport";
import ManagementAccessSection from "./components/ManagementAccess";
import { DevicesPageContext } from "./devices/context";
import DevicesNav from "./devices/DevicesNav";
import { devicesAnchors, devicesTabs, resolveSection } from "./devices/sections";
import { useSectionFocusKeeper } from "./devices/useSectionFocusKeeper";

import { useCanAdministerShownServer } from "@/stores/remoteAccess";

/**
 * What a place brought into view keeps between itself and the sticky feedback above it
 * (`scrollOffsetClass`), in pixels.
 */
const SCROLL_GAP_PX = 16;

interface Confirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
}

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
 * manages, a browser without this server's federation capability — the part that still applies is whether other
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
    // Its forms are laid out against their container, as on the devices page.
    <div className="@container">
      <ManagementAccessSection
        highlighted={highlighted}
        reloadKey={managementLinkKey(section, locationKey)}
        sectionRef={ref}
        onSettled={() => setSettled(true)}
      />
    </div>
  );
}

/**
 * This device's own devices page, in sections: this device, management, data sync
 * and advanced (see `devices/sections.ts`). Everything is read once here and handed to the
 * tab shown, so switching tabs never waits and every tab's count stays live in the nav.
 */
function Devices() {
  const { t } = useTranslation();
  const data = useDevicesData();
  const { identityError: loadError, identityLoading: loading } = data;
  const [params] = useSearchParams();
  const { key: locationKey } = useLocation();
  const sectionParam = params.get("section");
  const { tab, anchor, explicit } = resolveSection(sectionParam);
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  // A notice says how something just went, where it went: a link or a tab followed
  // afterwards leaves it behind. Errors stay until they are dismissed or retried.
  const [noticeAt, setNoticeAt] = useState<{ text: string; locationKey: string }>();
  const locationKeyRef = useRef(locationKey);

  locationKeyRef.current = locationKey;
  const setNotice = useCallback(
    (text?: string) =>
      setNoticeAt(text === undefined ? undefined : { text, locationKey: locationKeyRef.current }),
    [],
  );
  const notice = noticeAt?.locationKey === locationKey ? noticeAt.text : undefined;
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  const [now, setNow] = useState(Date.now());
  const [revealed, setRevealed] = useState<DevicesAnchor | null>(null);
  const pageRef = useRef<HTMLDivElement>(null);
  const feedbackRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    mounted.current = true;
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => {
      mounted.current = false;
      clearInterval(timer);
    };
  }, []);

  useSectionFocusKeeper(panelRef, headingRef);

  // A corrupt identity state never produces a status, so its recovery cannot live behind one.
  const sharingStateUnavailable =
    loadError instanceof FederationError && loadError.code === "SharingStateUnavailable";
  const pageLoadError = sharingStateUnavailable ? undefined : loadError;
  const feedbackShown = !!(pageLoadError || error || notice);

  // The sticky feedback can stand where a link or the nav brings a place (an identity load
  // error survives tab changes): every such place keeps its height clear through
  // `--devices-scroll-offset` (`scrollOffsetClass`). Set before the effects below bring
  // anything into view, and kept as its text wraps.
  useLayoutEffect(() => {
    const page = pageRef.current;
    const feedback = feedbackRef.current;

    if (!page) return undefined;
    const update = () =>
      page.style.setProperty(
        "--devices-scroll-offset",
        `${Math.ceil(feedback?.getBoundingClientRect().height ?? 0) + SCROLL_GAP_PX}px`,
      );

    update();
    if (!feedback || typeof ResizeObserver !== "function") return undefined;
    const observer = new ResizeObserver(update);

    observer.observe(feedback);

    return () => observer.disconnect();
  }, [feedbackShown]);

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
      // Turning sharing on can turn remote access on as well: both are read again, and data
      // sync, which removing a device ends too.
      if (mounted.current) await data.reload(["identity", "access", "dataSync"]);

      return true;
    } catch (cause) {
      if (mounted.current) {
        onError(cause instanceof Error ? cause : new Error(String(cause)));
        if (cause instanceof FederationError && cause.code === "PathMappingsChanged")
          await data.reload(["identity"]);
      }

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  };

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
  const context: DevicesPageContextValue = {
    data,
    busy,
    run,
    confirm,
    setNotice,
    now,
    mounted,
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
    <div
      ref={pageRef}
      className="@container mx-auto flex max-w-[1200px] flex-col gap-5 p-4 sm:p-6"
      data-testid="devices-page"
    >
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
        </div>
      </header>
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
              onClick={() => void data.reload(["identity"])}
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
                    // Only the identity state was lost: the install keeps its identity.
                    await federationPeerApi.resetIdentity(true, false);
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
          <div className="flex min-w-0 flex-col gap-5">
            {feedbackShown && (
              // Actions are spread over the tabs; keep their outcome in view wherever the user
              // is. Stuck in the content's column, so it never covers the nav beside it.
              <div
                ref={feedbackRef}
                className="sticky top-0 z-10 -mx-1 space-y-2 bg-background/95 px-1 py-1 backdrop-blur"
                data-testid="federation-feedback"
              >
                <ErrorNotice error={pageLoadError} onRetry={() => void data.reload(["identity"])} />
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
