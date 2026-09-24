import type { Ref } from "react";
import type {
  BakabaseServiceModelsViewRemoteAccessDeviceViewModel as PairedDevice,
  BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel as PendingRequest,
  BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings,
} from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineSafety } from "react-icons/ai";

import { revealClass } from "../hooks/useSectionReveal";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  MessageError,
  panelClass,
  primaryClass,
} from "./common";
import ConfirmDialog from "./ConfirmDialog";

import BApi from "@/sdk/BApi";
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { millisecondsUntil, minutesUntil, parseServerTime } from "@/core/serverTime";
import { remoteDevicePlatformLabelKey } from "@/core/remoteDevicePlatform";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/** While somebody may be waiting on this device — a request, a live code — re-read often. */
const LIVE_POLL_MS = 5000;
/**
 * Otherwise still re-read, often enough that a new request, or a mode changed from another
 * window, shows up on its own. Whatever the mode: "off" is exactly what something else on
 * the page (the sharing panel) or elsewhere (the settings page) may have just changed.
 */
const IDLE_POLL_MS = 15_000;

/**
 * Failures are shown in this section, next to what failed; a toast from the shared client
 * on top of that would say the same thing twice, in a corner, without the context.
 */
const inline = { showErrorToast: false } as const;

/**
 * A failure as the person should read it: the server's own words when it gave any.
 * `BApi` rejects with the whole response on an HTTP failure, not with an `Error`.
 */
const toError = (cause: unknown, fallback: string): Error => {
  if (cause instanceof Error) return cause;
  const body = (cause as { error?: { message?: string | null } } | undefined)?.error;

  return new MessageError(body?.message || fallback);
};

interface Confirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
}

export interface ManagementAccessProps {
  /** Called after anything that changes what the rest of the page shows (the mode). */
  onChanged?: () => void;
  /** Called once, when the first read of the settings has finished — either way. */
  onSettled?: () => void;
  /** The section element, for a page that brings it into view. */
  sectionRef?: Ref<HTMLElement>;
  /** Marks the section for a moment after a link led here. */
  highlighted?: boolean;
  /**
   * Changes whenever the settings may have changed outside this section — the page's own
   * view of the remote-access mode, a link that led here again. A new value re-reads them
   * at once, instead of on the next poll.
   */
  reloadKey?: string;
}

/**
 * The other side of management: whether other devices may manage the server this window
 * shows — this device in its own window, or the server a paired window is showing.
 *
 * Built on the existing remote-access endpoints rather than new ones — being managed is
 * exactly legacy paired-device access, the same pairing the settings page configures.
 * This is the short path through it: on, pair, approve, revoke. The full settings (live
 * transcoding, renaming devices, turning it back off) stay on the settings page, linked.
 *
 * Nothing changes on its own. Turning management on sets the mode to Enabled *and*
 * requires pairing, in one confirmed click; an Unrestricted mode is explained with an
 * explicit button to require pairing, never corrected behind the user's back.
 *
 * Also rendered where the rest of the devices page is not — the desktop app showing a
 * managed server, a browser on an Unrestricted server — because that
 * is where a headless server's management requests are answered: nobody can walk over to
 * a container and click a button. The wording then names the server instead of saying
 * "this device", which there would mean the wrong one.
 */
export default function ManagementAccessSection({
  onChanged,
  onSettled,
  sectionRef,
  highlighted = false,
  reloadKey,
}: ManagementAccessProps) {
  const { t } = useTranslation();
  const [settings, setSettings] = useState<RemoteAccessSettings>();
  const [loadError, setLoadError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const generation = useRef(0);
  const inFlight = useRef(0);
  const settled = useRef(false);
  /** Whether a quiet read that fails has anything better to leave on screen. */
  const hasSettings = useRef(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState<string>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  /** The digits, held only in this tab — the server keeps a digest and never returns them. */
  const [issuedCode, setIssuedCode] = useState<{ code: string; expiresAt: string }>();
  const [now, setNow] = useState(() => Date.now());
  // Optional: a test double of the store may not carry it, and a stale mode elsewhere
  // corrects itself on the next context read anyway.
  const reloadContext = useRemoteAccessStore((state) => state.load) as
    | (() => Promise<void>)
    | undefined;
  const isLocal = useRemoteAccessStore((state) => state.isLocal);
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const ownDeviceId = useRemoteAccessStore((state) => state.ownDeviceId);
  const latest = useRef({ onChanged, onSettled, reloadContext, failedText: "" });

  /** This device's own window, as opposed to a window showing the server from elsewhere. */
  const ownWindow = isLocal !== false && clientMode !== ClientMode.PureClient;
  /** A browser: nothing signs its requests, so requiring pairing locks it out as well. */
  const unpairedViewer = !isLocal && clientMode !== ClientMode.PureClient;
  const target = ownWindow
    ? t<string>("federation.management.self")
    : serverName || t<string>("federation.management.theServer");

  latest.current = {
    onChanged,
    onSettled,
    reloadContext,
    // Read inside `load` without making it depend on the language.
    failedText: t("federation.management.loadFailed", { target }),
  };

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      generation.current += 1;
    };
  }, []);

  /** `BApi` answers a refusal in the body; it is a failure here like any other. */
  const ensureOk = <T extends { code?: number; message?: string | null }>(rsp: T) => {
    if (rsp?.code) throw new MessageError(rsp.message || t("federation.management.failed"));

    return rsp;
  };

  /**
   * `quiet` reads happen on their own rather than because somebody asked: a failure keeps
   * the last good state instead of replacing it with an error (with nothing good to keep,
   * the error is still shown). `ifIdle` is for the polls, which yield to a read already in
   * flight; any other read supersedes it, so an answer from before a change never wins.
   */
  const load = useCallback(async (options: { quiet?: boolean; ifIdle?: boolean } = {}) => {
    if (options.ifIdle && inFlight.current > 0) return;
    const run = ++generation.current;

    inFlight.current += 1;
    try {
      const rsp = await BApi.remoteAccess.getRemoteAccessSettings(inline);

      if (run !== generation.current) return;
      if (rsp?.code) throw new MessageError(rsp.message || latest.current.failedText);
      if (rsp?.data) {
        hasSettings.current = true;
        setSettings(rsp.data);
        setLoadError(undefined);
      }
    } catch (cause) {
      if (run === generation.current && (!options.quiet || !hasSettings.current))
        setLoadError(toError(cause, latest.current.failedText));
    } finally {
      inFlight.current -= 1;
      if (!settled.current && mounted.current) {
        settled.current = true;
        latest.current.onSettled?.();
      }
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Something outside this section says the settings may have moved — e.g. the sharing
  // panel above turned remote access on, or a notification led here again. The first
  // value is the mount, which the read above already covers.
  const lastReloadKey = useRef(reloadKey);

  useEffect(() => {
    if (lastReloadKey.current === reloadKey) return;
    lastReloadKey.current = reloadKey;
    void load({ quiet: true });
  }, [reloadKey, load]);

  const mode = settings?.mode ?? RemoteAccessMode.Disabled;
  const pending = settings?.pendingRequests ?? [];
  const devices = settings?.devices ?? [];
  const live = pending.length > 0 || !!settings?.pairingCode || !!issuedCode;

  useEffect(() => {
    const timer = setInterval(
      () => {
        setNow(Date.now());
        if (!document.hidden) void load({ quiet: true, ifIdle: true });
      },
      live ? LIVE_POLL_MS : IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

  // Drop the digits the moment they stop working, so nobody types a dead code.
  useEffect(() => {
    if (issuedCode && millisecondsUntil(issuedCode.expiresAt, now) <= 0) setIssuedCode(undefined);
  }, [issuedCode, now]);

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
      if (mounted.current) await load();

      return true;
    } catch (cause) {
      if (mounted.current) onError(toError(cause, t("federation.management.failed")));

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
    options: { warning?: string; contextChanged?: boolean } = {},
  ) => {
    setConfirmationError(undefined);
    setConfirmation({
      title,
      description,
      warning: options.warning,
      action: options.contextChanged
        ? async () => {
            await action();
            // The mode shows elsewhere too — the sharing panel, the play buttons.
            await latest.current.reloadContext?.();
            latest.current.onChanged?.();
          }
        : action,
    });
  };

  /** Pairing first, then the mode: at no point is the server open without it. */
  const requirePairedAccess = async () => {
    ensureOk(await BApi.remoteAccess.setRemoteAccessRequirePairing({ require: true }, inline));
    if (mode !== RemoteAccessMode.Enabled)
      ensureOk(
        await BApi.remoteAccess.setRemoteAccessMode({ mode: RemoteAccessMode.Enabled }, inline),
      );
  };

  const requirePairing = () =>
    confirm(
      t("federation.management.requirePairing"),
      t(
        mode === RemoteAccessMode.Unrestricted
          ? "federation.management.requirePairingConfirmUnrestricted"
          : "federation.management.requirePairingConfirm",
        { target },
      ),
      requirePairedAccess,
      {
        contextChanged: true,
        // A browser is never paired: this would shut the door it is standing in.
        warning: unpairedViewer ? t("federation.management.lockOutWarning") : undefined,
      },
    );

  const approve = (request: PendingRequest) =>
    confirm(
      t("federation.management.requests.approve"),
      t(
        request.remoteAddress
          ? "federation.management.requests.approveConfirmFrom"
          : "federation.management.requests.approveConfirm",
        { name: request.deviceName, address: request.remoteAddress, target },
      ),
      async () => {
        ensureOk(await BApi.remoteAccess.approveRemoteDevicePairingRequest(request.id, inline));
        if (mounted.current)
          setNotice(
            t("federation.management.requests.approved", { name: request.deviceName, target }),
          );
      },
    );

  const revoke = (device: PairedDevice) =>
    confirm(
      t("federation.management.devices.revoke"),
      t("federation.management.devices.revokeConfirm", { name: device.name, target }),
      async () => {
        ensureOk(await BApi.remoteAccess.revokeRemoteAccessDevice(device.id, inline));
      },
      {
        warning:
          device.id === ownDeviceId
            ? t("federation.management.devices.revokeSelfWarning", { target })
            : undefined,
      },
    );

  const status = !settings
    ? undefined
    : mode === RemoteAccessMode.Disabled
      ? { key: "off", className: "bg-default-100 text-default-500" }
      : mode === RemoteAccessMode.Unrestricted
        ? { key: "unrestricted", className: "bg-warning/10 text-warning" }
        : settings.requirePairing
          ? { key: "paired", className: "bg-success/10 text-success" }
          : { key: "open", className: "bg-primary/10 text-primary" };

  return (
    <section
      ref={sectionRef}
      aria-busy={(!settings && !loadError) || undefined}
      aria-labelledby="management-access-title"
      className={`${panelClass} space-y-3 ${revealClass(highlighted)}`}
      data-highlighted={highlighted || undefined}
      data-testid="management-access"
      id="management-access"
      tabIndex={-1}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="flex items-center gap-2 font-semibold" id="management-access-title">
            <AiOutlineSafety aria-hidden />
            {t("federation.management.title", { target })}
          </h2>
          <p className="mt-1 max-w-3xl text-sm text-default-500">
            {t("federation.management.description", { target })}
          </p>
        </div>
        {status && (
          <span className={`rounded-md px-2 py-1 text-xs ${status.className}`}>
            {t(`federation.management.status.${status.key}`)}
          </span>
        )}
      </div>
      {(error || notice) && (
        <div className="space-y-2">
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
      <ErrorNotice error={loadError} onRetry={() => void load()} />
      {!settings && !loadError && <p className="text-sm">{t("federation.loading")}</p>}
      {settings && mode === RemoteAccessMode.Disabled && (
        <div className="space-y-2">
          <p className="text-sm">{t("federation.management.offTip", { target })}</p>
          <button
            className={primaryClass}
            disabled={busy}
            type="button"
            onClick={() =>
              confirm(
                t("federation.management.enable", { target }),
                t("federation.management.enableConfirm", { target }),
                requirePairedAccess,
                { contextChanged: true },
              )
            }
          >
            {t("federation.management.enable", { target })}
          </button>
        </div>
      )}
      {settings && mode === RemoteAccessMode.Unrestricted && (
        <div
          className="space-y-2 rounded-lg border border-warning/40 bg-warning/10 p-3"
          data-testid="management-unrestricted"
        >
          <p className="text-sm">{t("federation.management.unrestricted", { target })}</p>
          <button className={buttonClass} disabled={busy} type="button" onClick={requirePairing}>
            {t("federation.management.requirePairing")}
          </button>
        </div>
      )}
      {settings && mode === RemoteAccessMode.Enabled && !settings.requirePairing && (
        <div className="space-y-2 rounded-lg bg-default-50 p-3">
          <p className="text-sm">{t("federation.management.unpairedBrowse", { target })}</p>
          <button className={buttonClass} disabled={busy} type="button" onClick={requirePairing}>
            {t("federation.management.requirePairing")}
          </button>
        </div>
      )}
      {settings && mode !== RemoteAccessMode.Disabled && (
        <div className="grid gap-3 md:grid-cols-2">
          <div className="space-y-2 rounded-lg border border-default-200 p-3">
            <p className="text-xs text-default-500">
              {t("federation.management.addresses", { target })}
            </p>
            {settings.addresses?.length ? (
              <ul className="space-y-1">
                {settings.addresses.map((address) => (
                  <li key={address.url} className="break-all text-sm">
                    <code>{address.url}</code>
                    <span className="ml-2 text-xs text-default-400">{address.interfaceName}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-warning">
                {t("federation.management.noAddress", { target })}
              </p>
            )}
          </div>
          <div className="space-y-2 rounded-lg border border-default-200 p-3">
            <p className="text-xs text-default-500">
              {t("federation.management.code.tip", { target })}
            </p>
            {issuedCode ? (
              <>
                <code className="block text-2xl tracking-[0.25em]">{issuedCode.code}</code>
                <p className="text-xs text-default-500">
                  {t("configuration.remoteAccess.pairingCode.shownOnce", {
                    minutes: minutesUntil(issuedCode.expiresAt, now),
                  })}
                </p>
              </>
            ) : (
              settings.pairingCode && (
                <p className="text-xs text-default-500">
                  {t("configuration.remoteAccess.pairingCode.outstanding", {
                    minutes: minutesUntil(settings.pairingCode.expiresAt, now),
                    attempts: settings.pairingCode.remainingAttempts,
                  })}
                </p>
              )
            )}
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() =>
                void run(async () => {
                  const rsp = ensureOk(
                    await BApi.remoteAccess.issueRemoteAccessPairingCode(inline),
                  );

                  if (mounted.current && rsp.data) {
                    setIssuedCode({ code: rsp.data.code, expiresAt: rsp.data.expiresAt });
                    setNow(Date.now());
                  }
                })
              }
            >
              {t(
                issuedCode || settings.pairingCode
                  ? "federation.management.code.reissue"
                  : "federation.management.code.issue",
              )}
            </button>
          </div>
        </div>
      )}
      {pending.length > 0 && (
        <div className="space-y-2" data-testid="management-requests">
          <h3 className="text-sm font-medium">{t("federation.management.requests.title")}</h3>
          {pending.map((request) => (
            <div
              key={request.id}
              className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-default-50 p-3 text-sm"
            >
              <div className="min-w-0">
                <p className="font-medium">{request.deviceName}</p>
                <p className="mt-1 text-xs text-default-500">
                  {t(remoteDevicePlatformLabelKey(request.platform))}
                  {request.remoteAddress ? ` · ${request.remoteAddress}` : ""} ·{" "}
                  {t("configuration.remoteAccess.pending.expiresIn", {
                    minutes: minutesUntil(request.expiresAt, now),
                  })}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <button
                  className={primaryClass}
                  disabled={busy}
                  type="button"
                  onClick={() => approve(request)}
                >
                  {t("federation.management.requests.approve")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(async () => {
                      ensureOk(
                        await BApi.remoteAccess.rejectRemoteDevicePairingRequest(
                          request.id,
                          inline,
                        ),
                      );
                    })
                  }
                >
                  {t("federation.management.requests.reject")}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
      {devices.length > 0 && (
        <div className="space-y-2" data-testid="management-devices">
          <h3 className="text-sm font-medium">
            {t("federation.management.devices.title", { target })}
          </h3>
          {devices.map((device) => (
            <div
              key={device.id}
              className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-default-200 p-3 text-sm"
            >
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 font-medium">
                  {device.name}
                  {device.id === ownDeviceId && (
                    <span className="rounded-md bg-primary/10 px-2 py-0.5 text-xs font-normal text-primary">
                      {t("federation.management.devices.you")}
                    </span>
                  )}
                </p>
                <p className="mt-1 text-xs text-default-500">
                  {t(remoteDevicePlatformLabelKey(device.platform))} ·{" "}
                  {device.lastSeenAt
                    ? t("configuration.remoteAccess.devices.lastSeen", {
                        time: parseServerTime(device.lastSeenAt)?.toLocaleString() ?? "",
                      })
                    : t("configuration.remoteAccess.devices.neverSeen")}
                </p>
              </div>
              <button
                className={`${buttonClass} text-danger`}
                disabled={busy}
                type="button"
                onClick={() => revoke(device)}
              >
                {t("federation.management.devices.revoke")}
              </button>
            </div>
          ))}
        </div>
      )}
      <Link className="inline-block text-xs text-primary underline" to="/configuration">
        {t("federation.management.allSettings")}
      </Link>
      {confirmation && (
        <ConfirmDialog
          busy={busy}
          description={confirmation.description}
          error={confirmationError}
          title={confirmation.title}
          warning={confirmation.warning}
          onCancel={() => {
            setConfirmation(undefined);
            setConfirmationError(undefined);
          }}
          onConfirm={() => {
            const { action } = confirmation;

            void run(async () => {
              await action();
              if (mounted.current) setConfirmation(undefined);
            }, setConfirmationError);
          }}
        />
      )}
    </section>
  );
}
