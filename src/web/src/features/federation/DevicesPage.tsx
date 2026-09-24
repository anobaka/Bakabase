import type { PairingRequest, PairingResult, PathMapping, Peer } from "./types";
import type { DevicesSection } from "./switching";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { AiOutlineLaptop, AiOutlinePlus, AiOutlineReload } from "react-icons/ai";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  FederationAccess,
  fieldClass,
  panelClass,
  primaryClass,
} from "./components/common";
import ConfirmDialog from "./components/ConfirmDialog";
import { useFederationStatus } from "./hooks/useFederationStatus";
import { useSectionReveal } from "./hooks/useSectionReveal";
import { federationPeerApi } from "./peerApi";
import { FederationError, isAbort } from "./transport";
import ImportConnectionHints from "./components/ImportConnectionHints";
import ManagedServersSection from "./components/ManagedServers";
import ManagementAccessSection from "./components/ManagementAccess";

import { useCanAdministerShownServer } from "@/stores/remoteAccess";

/** How often outgoing requests are claimed while the page is visible. */
const CLAIM_POLL_MS = 4000;
/** A request whose claim failed (e.g. its device is unreachable) is left alone this long. */
const CLAIM_BACKOFF_MS = 30_000;
/** How often status is re-read so new incoming requests appear without a manual refresh. */
const STATUS_POLL_MS = 15_000;

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
 * manages, the retired client, a browser — the one part that still applies is whether other
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

function Devices() {
  const { t } = useTranslation();
  const { status, error: loadError, loading, refresh } = useFederationStatus();
  const [params] = useSearchParams();
  const identitySection = useRef<HTMLDetailsElement>(null);
  const section = params.get("section") as DevicesSection | null;
  const identityRequested = section === "identity";
  const statusReady = !!status;
  const { key: locationKey } = useLocation();
  // Both management sections load on their own. The access section sits under the
  // servers section, so it is brought into view only once both have filled: arriving
  // there before the list above it has loaded would let that list push it away again.
  const [serversSettled, setServersSettled] = useState(false);
  const [accessSettled, setAccessSettled] = useState(false);
  const serversReveal = useSectionReveal<HTMLElement>(section === "servers", serversSettled);
  const accessReveal = useSectionReveal<HTMLElement>(
    section === "management",
    serversSettled && accessSettled,
  );
  // The access section reads its own settings; these are the moments they may have moved
  // under it. The sharing panel below can turn remote access on (the page's status then
  // reports the new mode), and the "wants to manage this device" notification leads here
  // again with a request the section has not seen yet.
  const accessReloadKey = [
    status?.remoteAccessMode ?? "",
    status?.requirePairing ?? "",
    managementLinkKey(section, locationKey),
  ].join("|");

  useEffect(() => {
    if (statusReady && identityRequested && identitySection.current) {
      identitySection.current.open = true;
      identitySection.current.scrollIntoView?.({ block: "start" });
    }
  }, [statusReady, identityRequested, locationKey]);
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [notice, setNotice] = useState<string>();
  const [invite, setInvite] = useState<{ code: string; expiresAt: string }>();
  // Unset until the user touches it: the default then follows the current remote-access mode.
  const [configureRemote, setConfigureRemote] = useState<boolean>();
  const [shareBack, setShareBack] = useState(true);
  const [editingName, setEditingName] = useState<string>();
  const [copied, setCopied] = useState<{ address: string; ok: boolean }>();
  const copyTimer = useRef<ReturnType<typeof setTimeout>>();
  const [discovered, setDiscovered] =
    useState<{ nodeId: string; name: string; address: string }[]>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  const [now, setNow] = useState(Date.now());
  const claimBackoff = useRef(new Map<string, number>());
  const latest = useRef({ status, refresh, t });

  latest.current = { status, refresh, t };

  useEffect(() => {
    mounted.current = true;
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => {
      mounted.current = false;
      clearInterval(timer);
      clearTimeout(copyTimer.current);
    };
  }, []);

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
      if (mounted.current) await refresh();

      return true;
    } catch (cause) {
      if (mounted.current) {
        onError(cause instanceof Error ? cause : new Error(String(cause)));
        if (cause instanceof FederationError && cause.code === "PathMappingsChanged")
          await refresh();
      }

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  const showPairingOutcome = (result: PairingResult) => {
    if (!mounted.current) return;
    setNotice(t(`federation.pair.${result.outcome}`));
    if (result.outcome === "granted") setCode("");
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

  // Background polling. It reads the latest render through a ref so a status change never
  // restarts it, and it never touches the busy flag, action errors or the user's inputs:
  // it only reports a request that has just been decided, then re-reads status quietly.
  useEffect(() => {
    const controller = new AbortController();
    let claiming = false;
    let refreshing = false;

    const quietRefresh = async () => {
      if (refreshing || controller.signal.aborted) return;
      refreshing = true;
      try {
        await latest.current.refresh({ quiet: true });
      } finally {
        refreshing = false;
      }
    };
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
        await quietRefresh();
      } finally {
        claiming = false;
      }
    };
    const claimTimer = setInterval(() => void claimPending(), CLAIM_POLL_MS);
    const statusTimer = setInterval(() => {
      if (!document.hidden) void quietRefresh();
    }, STATUS_POLL_MS);

    return () => {
      controller.abort();
      clearInterval(claimTimer);
      clearInterval(statusTimer);
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
  const inviteValid = invite && Date.parse(invite.expiresAt) > now;
  const remoteDisabled = status?.remoteAccessMode === 0;
  // Enabling sharing while remote access is off would leave the device unreachable, so the
  // one-click default opens it; a mode the operator already widened is left alone.
  const configureRemoteChecked = configureRemote ?? remoteDisabled;
  const reachableAddresses = status?.reachableAddresses ?? [];
  const candidates = discovered?.filter(
    (candidate) => candidate.nodeId !== status?.identity.nodeId,
  );
  // A corrupt sharing state never produces a status, so its recovery cannot live behind one.
  const sharingStateUnavailable =
    loadError instanceof FederationError && loadError.code === "SharingStateUnavailable";
  const pageLoadError = sharingStateUnavailable ? undefined : loadError;
  const saveName = (name: string) =>
    void run(async () => {
      await federationPeerApi.setName(name.trim() || null);
      if (mounted.current) setEditingName(undefined);
    });
  const copyAddress = async (value: string) => {
    let ok = true;

    try {
      await navigator.clipboard.writeText(value);
    } catch {
      ok = false;
    }
    if (!mounted.current) return;
    setCopied({ address: value, ok });
    clearTimeout(copyTimer.current);
    copyTimer.current = setTimeout(() => setCopied(undefined), 2000);
  };
  const resetAsNewNode = (description: string) =>
    confirm(t("federation.identity.reset"), description, async () => {
      await federationPeerApi.resetIdentity(true);
      if (mounted.current) setInvite(undefined);
    });

  return (
    <div className="mx-auto flex max-w-[1200px] flex-col gap-5 p-4 sm:p-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineLaptop aria-hidden />
            {t("federation.devices.title")}
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("federation.devices.intro")}</p>
        </div>
        <div className="flex gap-2">
          <button
            className={buttonClass}
            disabled={loading || busy}
            type="button"
            onClick={() => void refresh()}
          >
            <AiOutlineReload aria-hidden />
            {t("federation.refresh")}
          </button>
          <Link className={buttonClass} to="/federation">
            {t("federation.title")}
          </Link>
        </div>
      </header>
      {(pageLoadError || error || notice) && (
        // Actions are spread down a long page; keep their outcome in view wherever the user is.
        <div
          className="sticky top-0 z-10 -mx-1 space-y-2 bg-background/95 px-1 py-1 backdrop-blur"
          data-testid="federation-feedback"
        >
          <ErrorNotice error={pageLoadError} onRetry={() => void refresh()} />
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
              onClick={() => void refresh()}
            >
              {t("federation.retry")}
            </button>
            <button
              className={`${buttonClass} text-danger`}
              disabled={busy}
              type="button"
              onClick={() => resetAsNewNode(t("federation.recovery.confirm"))}
            >
              {t("federation.identity.reset")}
            </button>
          </div>
        </section>
      )}
      {/* Management first: it is where the window's server switcher leads, and it does not
          depend on the sharing state below — a device whose sharing state is unreadable
          can still manage other devices and be managed. */}
      <ManagedServersSection
        highlighted={serversReveal.highlighted}
        sectionRef={serversReveal.ref}
        onSettled={() => setServersSettled(true)}
      />
      <ManagementAccessSection
        highlighted={accessReveal.highlighted}
        reloadKey={accessReloadKey}
        sectionRef={accessReveal.ref}
        onChanged={() => void refresh({ quiet: true })}
        onSettled={() => setAccessSettled(true)}
      />
      {!status && loading && <p role="status">{t("federation.loading")}</p>}
      {status && (
        <>
          <section className={`${panelClass} space-y-3`}>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="font-semibold">{t("federation.browsing.title")}</h2>
              <span
                className={`rounded-md px-2 py-1 text-xs ${status.browsingEnabled === true ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`}
              >
                {t(
                  status.browsingEnabled === true
                    ? "federation.browsing.on"
                    : "federation.browsing.off",
                )}
              </span>
            </div>
            <p className="text-sm text-default-500">{t("federation.browsing.description")}</p>
            {status.browsingEnabled === true && (
              <p className="text-xs text-default-500">{t("federation.browsing.disableTip")}</p>
            )}
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() =>
                void run(() => federationPeerApi.browsing(status.browsingEnabled !== true))
              }
            >
              {t(
                status.browsingEnabled === true
                  ? "federation.browsing.disable"
                  : "federation.browsing.enable",
              )}
            </button>
          </section>
          <section className={`${panelClass} space-y-3`}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="text-xs text-default-500">{t("federation.thisDevice")}</p>
                {editingName === undefined ? (
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-lg font-semibold">{status.identity.name}</h2>
                    <button
                      className={`${buttonClass} !px-2 !py-1 text-xs`}
                      disabled={busy}
                      type="button"
                      onClick={() => setEditingName(status.identity.name)}
                    >
                      {t("federation.name.edit")}
                    </button>
                  </div>
                ) : (
                  <form
                    className="mt-1 space-y-2"
                    onSubmit={(event) => {
                      event.preventDefault();
                      saveName(editingName);
                    }}
                  >
                    <label className="block space-y-1 text-sm">
                      <span>{t("federation.name.label")}</span>
                      <input
                        // eslint-disable-next-line jsx-a11y/no-autofocus
                        autoFocus
                        className={fieldClass}
                        maxLength={64}
                        value={editingName}
                        onChange={(event) => setEditingName(event.target.value)}
                      />
                    </label>
                    <p className="text-xs text-default-500">{t("federation.name.tip")}</p>
                    <div className="flex flex-wrap gap-2">
                      <button className={primaryClass} disabled={busy} type="submit">
                        {t("federation.save")}
                      </button>
                      <button
                        className={buttonClass}
                        disabled={busy}
                        type="button"
                        onClick={() => saveName("")}
                      >
                        {t("federation.name.reset")}
                      </button>
                      <button
                        className={buttonClass}
                        disabled={busy}
                        type="button"
                        onClick={() => setEditingName(undefined)}
                      >
                        {t("federation.cancel")}
                      </button>
                    </div>
                  </form>
                )}
                <p className="mt-1 break-all font-mono text-xs text-default-400">
                  {status.identity.nodeId}
                </p>
              </div>
              <span
                className={`rounded-md px-2 py-1 text-xs ${status.sharingEnabled ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`}
              >
                {t(status.sharingEnabled ? "federation.sharing.on" : "federation.sharing.off")}
              </span>
            </div>
            <p className="text-sm">{t("federation.sharing.description")}</p>
            <p className="text-xs text-default-500">
              {t("federation.sharing.mode", {
                mode: t(`federation.remoteMode.${status.remoteAccessMode}`),
              })}{" "}
              ·{" "}
              {t(
                status.requirePairing ? "federation.sharing.paired" : "federation.sharing.unpaired",
              )}
            </p>
            {!status.sharingEnabled && (
              <label className="flex items-start gap-2 rounded-lg bg-default-50 p-3 text-sm">
                <input
                  checked={configureRemoteChecked}
                  className="mt-1"
                  type="checkbox"
                  onChange={(event) => setConfigureRemote(event.target.checked)}
                />
                <span>{t("federation.sharing.configureRemote")}</span>
              </label>
            )}
            <div className="flex flex-wrap gap-2">
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  status.sharingEnabled
                    ? confirm(
                        t("federation.sharing.stop"),
                        t("federation.sharing.stopConfirm"),
                        async () => {
                          await federationPeerApi.sharing(false, false);
                          if (mounted.current) setInvite(undefined);
                        },
                      )
                    : confirm(
                        t("federation.sharing.start"),
                        t(
                          configureRemoteChecked
                            ? "federation.sharing.confirmWithRemote"
                            : "federation.sharing.confirm",
                        ),
                        () => federationPeerApi.sharing(true, configureRemoteChecked),
                      )
                }
              >
                {t(status.sharingEnabled ? "federation.sharing.stop" : "federation.sharing.start")}
              </button>
              <button
                className={buttonClass}
                disabled={busy || !status.sharingEnabled}
                type="button"
                onClick={() =>
                  void run(async () => {
                    const issued = await federationPeerApi.invite();

                    if (mounted.current) setInvite(issued);
                  })
                }
              >
                {t("federation.sharing.issueCode")}
              </button>
            </div>
            {(status.sharingEnabled || invite) && (
              // What the other device types: an address, plus the code when one was issued.
              <div className="grid gap-3 md:grid-cols-2">
                {status.sharingEnabled && (
                  <div
                    aria-label={t("federation.sharing.addresses")}
                    className="space-y-2 rounded-lg border border-default-200 p-3"
                    role="group"
                  >
                    <p className="text-xs text-default-500">{t("federation.sharing.addresses")}</p>
                    {reachableAddresses.length > 0 ? (
                      <>
                        <ul className="space-y-1">
                          {reachableAddresses.map((reachable) => (
                            <li key={reachable} className="flex flex-wrap items-center gap-2">
                              <code className="break-all text-sm">{reachable}</code>
                              <button
                                aria-label={t("federation.copyAddress", { address: reachable })}
                                className={`${buttonClass} !px-2 !py-1 text-xs`}
                                type="button"
                                onClick={() => void copyAddress(reachable)}
                              >
                                {t(
                                  copied?.address !== reachable
                                    ? "federation.copy"
                                    : copied.ok
                                      ? "federation.copied"
                                      : "federation.copyFailed",
                                )}
                              </button>
                            </li>
                          ))}
                        </ul>
                        <p className="text-xs text-default-500">
                          {t("federation.sharing.addressesTip")}
                        </p>
                      </>
                    ) : remoteDisabled ? (
                      <>
                        <p className="text-sm text-warning">
                          {t("federation.sharing.remoteDisabled")}
                        </p>
                        <button
                          className={buttonClass}
                          disabled={busy}
                          type="button"
                          onClick={() =>
                            confirm(
                              t("federation.sharing.configureRemote"),
                              t("federation.sharing.confirmWithRemote"),
                              () => federationPeerApi.sharing(true, true),
                            )
                          }
                        >
                          {t("federation.sharing.configureRemote")}
                        </button>
                      </>
                    ) : (
                      <p className="text-sm text-warning">{t("federation.sharing.noAddress")}</p>
                    )}
                  </div>
                )}
                {invite && (
                  <div className="rounded-lg border border-default-200 p-3">
                    {inviteValid ? (
                      <>
                        <p className="text-xs text-default-500">
                          {t("federation.sharing.codeTip")}
                        </p>
                        <code className="my-2 block text-2xl tracking-[0.25em]">{invite.code}</code>
                        <p className="text-xs text-default-500">
                          {t("federation.expires", {
                            time: new Date(invite.expiresAt).toLocaleTimeString(),
                          })}
                        </p>
                      </>
                    ) : (
                      <p className="text-sm">{t("federation.sharing.codeExpired")}</p>
                    )}
                  </div>
                )}
              </div>
            )}
          </section>
          <section className={panelClass}>
            <h2 className="font-semibold">{t("federation.devices.add")}</h2>
            <p className="mt-1 text-sm text-default-500">{t("federation.pair.description")}</p>
            <form
              className="mt-4 grid items-end gap-3 md:grid-cols-[1fr_200px_auto_auto]"
              onSubmit={(event) => {
                event.preventDefault();
                if (address.trim())
                  void run(async () =>
                    showPairingOutcome(
                      await federationPeerApi.connect(
                        address.trim(),
                        code.trim() || undefined,
                        shareBack,
                      ),
                    ),
                  );
              }}
            >
              <label className="space-y-1 text-sm">
                <span>{t("federation.pair.address")}</span>
                <input
                  required
                  className={fieldClass}
                  placeholder="192.168.1.5:34567"
                  value={address}
                  onChange={(event) => setAddress(event.target.value)}
                />
              </label>
              <label className="space-y-1 text-sm">
                <span>{t("federation.pair.code")}</span>
                <input
                  autoComplete="off"
                  className={fieldClass}
                  value={code}
                  onChange={(event) => setCode(event.target.value)}
                />
              </label>
              <button className={primaryClass} disabled={busy || !address.trim()} type="submit">
                <AiOutlinePlus aria-hidden />
                {t(code.trim() ? "federation.pair.withCode" : "federation.pair.request")}
              </button>
              <label className="col-span-full flex items-start gap-2 text-sm">
                <input
                  checked={shareBack}
                  className="mt-1"
                  type="checkbox"
                  onChange={(event) => setShareBack(event.target.checked)}
                />
                <span>
                  {t("federation.pair.shareBack")}
                  <span className="mt-0.5 block text-xs text-default-500">
                    {t(
                      remoteDisabled
                        ? "federation.pair.shareBackTipRemote"
                        : "federation.pair.shareBackTip",
                    )}
                  </span>
                </span>
              </label>
            </form>
            {!shareBack && (
              <p className="mt-3 text-xs text-default-500">{t("federation.pair.directionTip")}</p>
            )}
            <button
              className={`${buttonClass} mt-3`}
              disabled={busy}
              type="button"
              onClick={() =>
                void run(async () => {
                  const found = await federationPeerApi.discover();

                  if (mounted.current) setDiscovered(found);
                })
              }
            >
              {t("federation.discovery.scan")}
            </button>
            {candidates && (
              <div className="mt-3 space-y-2">
                {!candidates.length && (
                  <p className="text-sm text-default-500">{t("federation.discovery.noneFound")}</p>
                )}
                {candidates.map((candidate) => (
                  <div
                    key={candidate.nodeId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-default-50 p-3 text-sm"
                  >
                    <span>
                      {candidate.name} <span className="text-default-500">{candidate.address}</span>
                    </span>
                    <button
                      className={buttonClass}
                      disabled={busy}
                      type="button"
                      onClick={() => {
                        setAddress(candidate.address);
                        setCode("");
                      }}
                    >
                      {t("federation.discovery.use")}
                    </button>
                  </div>
                ))}
              </div>
            )}
          </section>
          {status.requests.length > 0 && (
            <section className={panelClass}>
              <h2 className="font-semibold">{t("federation.requests.title")}</h2>
              <div className="mt-3 divide-y divide-default-200">
                {status.requests.map((request) => {
                  const expired = Date.parse(request.expiresAt) <= now;
                  const pending = request.status === "awaitingApproval";
                  const incoming = request.direction === "incoming";
                  // The name and node ID are the requester's own claims; the address is what we saw.
                  const replaces = incoming && pending && request.replacesExistingAccess;
                  const reciprocal = incoming && pending && request.offersReciprocalAccess;

                  return (
                    <div
                      key={request.requestId}
                      className="flex flex-wrap items-center justify-between gap-3 py-3"
                    >
                      <div className="min-w-0">
                        <p className="font-medium">{request.nodeName}</p>
                        <p className="mt-1 text-xs text-default-500">
                          {t(`federation.requests.${request.direction}`)} ·{" "}
                          {t(
                            expired && pending
                              ? "federation.requests.expired"
                              : pending && incoming
                                ? "federation.requests.awaitingYourApproval"
                                : `federation.pair.${request.status}`,
                          )}
                        </p>
                        {incoming && request.remoteAddress && (
                          <p className="mt-1 break-all text-xs text-default-500">
                            {t("federation.requests.from", { address: request.remoteAddress })}
                          </p>
                        )}
                        {reciprocal && !expired && (
                          <p className="mt-1 text-xs text-success">
                            {t("federation.requests.offersReciprocal", { name: request.nodeName })}
                          </p>
                        )}
                        {replaces && !expired && (
                          <p className="mt-2 max-w-2xl text-xs text-warning">
                            {t("federation.requests.replacesExisting")}
                          </p>
                        )}
                      </div>
                      {pending && !expired && (
                        <div className="flex flex-wrap gap-2">
                          {incoming ? (
                            <>
                              <button
                                className={primaryClass}
                                disabled={busy}
                                type="button"
                                onClick={() =>
                                  confirm(
                                    t("federation.requests.approve"),
                                    t(
                                      request.remoteAddress
                                        ? reciprocal
                                          ? "federation.requests.approveConfirmFromReciprocal"
                                          : "federation.requests.approveConfirmFrom"
                                        : reciprocal
                                          ? "federation.requests.approveConfirmReciprocal"
                                          : "federation.requests.approveConfirm",
                                      { name: request.nodeName, address: request.remoteAddress },
                                    ),
                                    () => federationPeerApi.decide(request.requestId, true),
                                    replaces
                                      ? t("federation.requests.replacesExisting")
                                      : undefined,
                                  )
                                }
                              >
                                {t("federation.requests.approve")}
                              </button>
                              <button
                                className={buttonClass}
                                disabled={busy}
                                type="button"
                                onClick={() =>
                                  void run(() => federationPeerApi.decide(request.requestId, false))
                                }
                              >
                                {t("federation.requests.reject")}
                              </button>
                            </>
                          ) : (
                            <>
                              <button
                                className={buttonClass}
                                disabled={busy}
                                type="button"
                                onClick={() =>
                                  void run(async () => {
                                    const result = await federationPeerApi.claim(request.requestId);

                                    claimBackoff.current.delete(request.requestId);
                                    showPairingOutcome(result);
                                  })
                                }
                              >
                                {t("federation.requests.check")}
                              </button>
                              <button
                                className={buttonClass}
                                disabled={busy}
                                type="button"
                                onClick={() =>
                                  void run(() => federationPeerApi.cancelRequest(request.requestId))
                                }
                              >
                                {t("federation.requests.cancel")}
                              </button>
                            </>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </section>
          )}
          <ImportConnectionHints
            busy={busy}
            onSelect={(address) => {
              setAddress(address);
              setCode("");
            }}
          />
          <section className="space-y-3">
            <div className="flex items-baseline justify-between">
              <h2 className="font-semibold">{t("federation.devices.known")}</h2>
              <span className="text-xs text-default-500">{status.peers.length}</span>
            </div>
            {!status.peers.length && (
              <div className={`${panelClass} py-8 text-center text-sm text-default-500`}>
                {t("federation.devices.empty")}
              </div>
            )}
            {status.peers.map((peer) => (
              <PeerCard
                key={peer.nodeId}
                busy={busy}
                peer={peer}
                onEnable={(enabled) =>
                  void run(() => federationPeerApi.enable(peer.nodeId, enabled))
                }
                onForget={() =>
                  confirm(
                    t("federation.devices.forget"),
                    t("federation.devices.forgetConfirm", { name: peer.label }),
                    () => federationPeerApi.forget(peer.nodeId),
                  )
                }
                onRemove={() =>
                  confirm(
                    t("federation.devices.remove"),
                    t("federation.devices.removeConfirm", { name: peer.label }),
                    () => federationPeerApi.remove(peer.nodeId),
                  )
                }
                onRevoke={() =>
                  peer.inboundGrant &&
                  confirm(
                    t("federation.devices.revoke"),
                    t("federation.devices.revokeConfirm", { name: peer.label }),
                    () => federationPeerApi.revoke(peer.inboundGrant!.grantId),
                  )
                }
                onSaveMappings={(mappings, expectedMappings) =>
                  run(async () => {
                    await federationPeerApi.mappings(peer.nodeId, mappings, expectedMappings);
                    if (mounted.current) setNotice(t("federation.mappings.saved"));
                  })
                }
              />
            ))}
          </section>
          <details ref={identitySection} className={panelClass} id="federation-identity">
            <summary className="cursor-pointer text-sm font-medium">
              {t("federation.identity.title")}
            </summary>
            <p className="mt-2 text-sm text-default-500">{t("federation.identity.tip")}</p>
            <button
              className={`${buttonClass} mt-3 mr-2`}
              disabled={busy}
              type="button"
              onClick={() =>
                confirm(
                  t("federation.identity.restore"),
                  t("federation.identity.restoreConfirm"),
                  async () => {
                    await federationPeerApi.resetIdentity(false);
                    if (mounted.current) setInvite(undefined);
                  },
                )
              }
            >
              {t("federation.identity.restore")}
            </button>
            <button
              className={`${buttonClass} mt-3 text-danger`}
              disabled={busy}
              type="button"
              onClick={() => resetAsNewNode(t("federation.identity.confirm"))}
            >
              {t("federation.identity.reset")}
            </button>
          </details>
        </>
      )}
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

function PeerCard({
  peer,
  busy,
  onEnable,
  onForget,
  onRevoke,
  onRemove,
  onSaveMappings,
}: {
  peer: Peer;
  busy: boolean;
  onEnable: (enabled: boolean) => void;
  onForget: () => void;
  onRevoke: () => void;
  onRemove: () => void;
  onSaveMappings: (mappings: PathMapping[], expectedMappings: PathMapping[]) => Promise<boolean>;
}) {
  const { t } = useTranslation();
  const [mappings, setMappings] = useState(peer.pathMappings);
  const [dirty, setDirty] = useState(false);
  const [mappingReview, setMappingReview] = useState<{
    proposed: PathMapping[];
    baseline: string;
  }>();
  const [roots, setRoots] = useState<{ sourceRootId: string; name: string }[]>();
  const [rootsError, setRootsError] = useState<Error>();
  const [loadingRoots, setLoadingRoots] = useState(false);
  const rootRequest = useRef<AbortController>();

  useEffect(() => () => rootRequest.current?.abort(), []);
  const loadRoots = async () => {
    rootRequest.current?.abort();
    const request = new AbortController();

    rootRequest.current = request;
    setLoadingRoots(true);
    setRootsError(undefined);
    try {
      const result = await federationPeerApi.mappingRoots(peer.nodeId, request.signal);

      if (!request.signal.aborted) setRoots(result);
    } catch (cause) {
      if (!request.signal.aborted)
        setRootsError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (!request.signal.aborted) setLoadingRoots(false);
    }
  };

  useEffect(() => {
    if (!dirty) setMappings(peer.pathMappings);
  }, [peer.pathMappings, dirty]);
  const update = (index: number, patch: Partial<PathMapping>) => {
    setMappingReview(undefined);
    setDirty(true);
    setMappings((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  };

  const mappingKey = (rows: PathMapping[]) =>
    JSON.stringify([...rows].sort((a, b) => a.sourceRootId.localeCompare(b.sourceRootId)));
  const conflicts = (proposed: PathMapping[]) =>
    peer.pathMappings.filter(
      (existing) =>
        proposed.find((row) => row.sourceRootId === existing.sourceRootId)?.localPath !==
        existing.localPath,
    );
  const persistMappings = async (proposed: PathMapping[]) => {
    if (await onSaveMappings(proposed, peer.pathMappings)) {
      setMappingReview(undefined);
      setMappings(proposed);
      setDirty(false);
    }
  };
  const reviewMappings = () => {
    const proposed = mappings.map((mapping) => ({
      sourceRootId: mapping.sourceRootId.trim(),
      localPath: mapping.localPath.trim(),
    }));

    if (conflicts(proposed).length)
      setMappingReview({ proposed, baseline: mappingKey(peer.pathMappings) });
    else void persistMappings(proposed);
  };
  const applyReview = (keepExisting: boolean) => {
    if (!mappingReview) return;
    // A refreshed status invalidates the previous decision; show the latest conflicts first.
    if (mappingReview.baseline !== mappingKey(peer.pathMappings)) {
      setMappingReview({ ...mappingReview, baseline: mappingKey(peer.pathMappings) });

      return;
    }
    const preserved = keepExisting ? conflicts(mappingReview.proposed) : [];
    const proposed = [
      ...mappingReview.proposed.filter(
        (row) => !preserved.some((existing) => existing.sourceRootId === row.sourceRootId),
      ),
      ...preserved,
    ];

    void persistMappings(proposed);
  };

  return (
    <article className={`${panelClass} space-y-3`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-semibold">{peer.label}</h3>
          <p className="mt-1 break-all text-xs text-default-500">{peer.address}</p>
        </div>
        <span className="rounded-md bg-default-100 px-2 py-1 text-xs">
          {t(`federation.connection.${peer.connectionState}`, {
            defaultValue: peer.connectionState,
          })}
        </span>
      </div>
      <div className="flex flex-wrap gap-2 text-xs">
        <span
          className={`rounded-md px-2 py-1 ${peer.outboundGrant ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`}
        >
          {t(peer.outboundGrant ? "federation.devices.outbound" : "federation.devices.noOutbound")}
        </span>
        <span
          className={`rounded-md px-2 py-1 ${peer.inboundGrant ? "bg-primary/10 text-primary" : "bg-default-100 text-default-500"}`}
        >
          {t(peer.inboundGrant ? "federation.devices.inbound" : "federation.devices.noInbound")}
        </span>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <label className="mr-auto flex items-center gap-2 text-sm">
          <input
            checked={peer.enabled}
            disabled={busy || !peer.outboundGrant}
            type="checkbox"
            onChange={(event) => onEnable(event.target.checked)}
          />
          {t("federation.devices.include")}
        </label>
        {peer.outboundGrant && (
          <button className={buttonClass} disabled={busy} type="button" onClick={onForget}>
            {t("federation.devices.forget")}
          </button>
        )}
        {peer.inboundGrant && (
          <button
            className={`${buttonClass} text-danger`}
            disabled={busy}
            type="button"
            onClick={onRevoke}
          >
            {t("federation.devices.revoke")}
          </button>
        )}
        <button
          className={`${buttonClass} text-danger`}
          disabled={busy}
          type="button"
          onClick={onRemove}
        >
          {t("federation.devices.remove")}
        </button>
      </div>
      {peer.outboundGrant && (
        <details
          className="border-t border-default-200 pt-3"
          onToggle={(event) => {
            if (event.currentTarget.open && !roots && !loadingRoots) void loadRoots();
          }}
        >
          <summary className="cursor-pointer text-sm font-medium">
            {t("federation.mappings.title")}
          </summary>
          <p className="mt-2 text-xs text-default-500">{t("federation.mappings.tip")}</p>
          <ErrorNotice error={rootsError} onRetry={() => void loadRoots()} />
          {loadingRoots && (
            <p className="mt-2 text-xs" role="status">
              {t("federation.loading")}
            </p>
          )}
          <div className="mt-3 space-y-2">
            {mappings.map((mapping, index) => (
              <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
                <label className="space-y-1 text-xs">
                  <span>{t("federation.mappings.root")}</span>
                  <select
                    className={fieldClass}
                    disabled={busy}
                    value={mapping.sourceRootId}
                    onChange={(event) => update(index, { sourceRootId: event.target.value })}
                  >
                    <option value="">{t("federation.mappings.selectRoot")}</option>
                    {mapping.sourceRootId &&
                      !roots?.some((root) => root.sourceRootId === mapping.sourceRootId) && (
                        <option value={mapping.sourceRootId}>
                          {t("federation.mappings.unavailableRoot")}
                        </option>
                      )}
                    {roots?.map((root) => (
                      <option key={root.sourceRootId} value={root.sourceRootId}>
                        {root.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="space-y-1 text-xs">
                  <span>{t("federation.mappings.local")}</span>
                  <input
                    className={fieldClass}
                    disabled={busy}
                    placeholder="/Volumes/Media"
                    value={mapping.localPath}
                    onChange={(event) => update(index, { localPath: event.target.value })}
                  />
                </label>
                <button
                  aria-label={t("federation.mappings.remove")}
                  className={`${buttonClass} self-end`}
                  disabled={busy}
                  type="button"
                  onClick={() => {
                    setMappingReview(undefined);
                    setDirty(true);
                    setMappings((rows) => rows.filter((_, i) => i !== index));
                  }}
                >
                  ×
                </button>
              </div>
            ))}
          </div>
          <div className="mt-3 flex gap-2">
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => {
                setMappingReview(undefined);
                setDirty(true);
                setMappings((rows) => [...rows, { sourceRootId: "", localPath: "" }]);
              }}
            >
              {t("federation.mappings.add")}
            </button>
            <button
              className={primaryClass}
              disabled={
                busy ||
                !dirty ||
                mappings.some(
                  (mapping) => !mapping.sourceRootId.trim() || !mapping.localPath.trim(),
                )
              }
              type="button"
              onClick={reviewMappings}
            >
              {t("federation.save")}
            </button>
          </div>
          {mappingReview && (
            <section
              aria-label={t("federation.mappings.conflictTitle")}
              className="mt-3 space-y-3 rounded-lg border border-warning/40 bg-warning/5 p-3"
              role="alertdialog"
            >
              <h4 className="font-medium">{t("federation.mappings.conflictTitle")}</h4>
              <p className="text-sm">{t("federation.mappings.conflictTip")}</p>
              {mappingReview.baseline !== mappingKey(peer.pathMappings) && (
                <p className="text-sm text-warning" role="status">
                  {t("federation.mappings.changedDuringReview")}
                </p>
              )}
              <ul className="space-y-2 text-xs">
                {conflicts(mappingReview.proposed).map((existing) => (
                  <li key={existing.sourceRootId} className="space-y-1 break-all">
                    <p className="font-medium">
                      {roots?.find((root) => root.sourceRootId === existing.sourceRootId)?.name ??
                        t("federation.mappings.unavailableRoot")}
                    </p>
                    <p>
                      {existing.localPath} →{" "}
                      {mappingReview.proposed.find(
                        (row) => row.sourceRootId === existing.sourceRootId,
                      )?.localPath ?? t("federation.mappings.removed")}
                    </p>
                  </li>
                ))}
              </ul>
              <div className="flex flex-wrap gap-2">
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => applyReview(true)}
                >
                  {t("federation.mappings.keep")}
                </button>
                <button
                  className={primaryClass}
                  disabled={busy}
                  type="button"
                  onClick={() => applyReview(false)}
                >
                  {t("federation.mappings.replace")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => setMappingReview(undefined)}
                >
                  {t("federation.cancel")}
                </button>
              </div>
            </section>
          )}
        </details>
      )}
    </article>
  );
}
