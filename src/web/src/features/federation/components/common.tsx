import type { ReactNode } from "react";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation } from "react-router-dom";

import { FederationError } from "../transport";
import { openLocalView } from "../switching";

import { useRemoteAccessStore, useIsPureClient } from "@/stores/remoteAccess";

export const fieldClass =
  "w-full rounded-lg border border-default-300 bg-content1 px-3 py-2 text-sm outline-none focus:border-primary focus:ring-2 focus:ring-primary/15 disabled:opacity-50";
export const buttonClass =
  "inline-flex items-center justify-center gap-2 rounded-lg border border-default-300 px-3 py-2 text-sm font-medium transition hover:bg-default-100 disabled:cursor-not-allowed disabled:opacity-50";
export const primaryClass = `${buttonClass} !border-primary bg-primary text-primary-foreground hover:!bg-primary/90`;
export const panelClass = "rounded-xl border border-default-200 bg-content1 p-4";

/**
 * An error whose message is already written for the person reading it — a refusal the
 * server phrased, or a sentence this UI chose. {@link ErrorNotice} shows it as is, where
 * any other non-federation error is summarised as a network failure.
 */
export class MessageError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "MessageError";
  }
}

/**
 * Multi-device pages are this device's own: they run on its local API, which only its own
 * window can reach. Anywhere else the page explains where to go instead.
 *
 * `elsewhere` is what a page can still offer there — content about the server the window
 * shows rather than about this device. It is rendered under the explanation, wherever the
 * page itself is not.
 */
export function FederationAccess({
  children,
  elsewhere,
}: {
  children: ReactNode;
  elsewhere?: ReactNode;
}) {
  const { t } = useTranslation();
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const local = useRemoteAccessStore((state) => state.isLocal);
  // Read through the store rather than a dedicated hook, so this stays a plain selector.
  const inConsole = useRemoteAccessStore((state) => state.clientHost === "console");
  const pureClient = useIsPureClient();

  if (!initialized)
    return (
      <div className="p-6" role="status">
        {t("federation.loading")}
      </div>
    );
  if (pureClient && inConsole) return <ConsoleLocalOnly elsewhere={elsewhere} />;
  if (pureClient || !local) {
    return (
      <div className="mx-auto flex max-w-2xl flex-col gap-4 p-6">
        <h1 className="text-xl font-semibold">{t("federation.title")}</h1>
        <p>{t(pureClient ? "federation.migration.intro" : "federation.localOnly")}</p>
        {pureClient && (
          <p className="text-sm text-default-500">{t("federation.migration.automatic")}</p>
        )}
        <Link className={`${buttonClass} self-start`} to="/other-devices">
          {t("federation.migration.download")}
        </Link>
        {elsewhere}
      </div>
    );
  }

  return <>{children}</>;
}

/**
 * A multi-device page reached while the desktop app shows a managed server. These pages
 * belong to the device the window is on, so the answer is to go back to it — on the same
 * page — rather than to explain the managed server's own copy of them.
 */
function ConsoleLocalOnly({ elsewhere }: { elsewhere?: ReactNode }) {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const [error, setError] = useState<Error>();

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">{t("federation.title")}</h1>
      <p>{t("federation.console.localOnly")}</p>
      <button
        className={`${buttonClass} self-start`}
        type="button"
        onClick={() => {
          setError(undefined);
          openLocalView(pathname).catch(() =>
            setError(new MessageError(t("federation.switcher.openFailed"))),
          );
        }}
      >
        {t("federation.console.switchToThisDevice")}
      </button>
      <ErrorNotice error={error} />
      {elsewhere}
    </div>
  );
}

export function ErrorNotice({
  error,
  onRetry,
  onDismiss,
}: {
  error?: Error;
  onRetry?: () => void;
  onDismiss?: () => void;
}) {
  const { t, i18n } = useTranslation();

  if (!error) return null;
  const key =
    error instanceof FederationError
      ? `federation.error.${error.code}`
      : "federation.error.network";
  const known = typeof i18n.exists === "function" && i18n.exists(key);
  const text =
    error instanceof MessageError && error.message
      ? error.message
      : known
        ? t(key)
        : error.message || t("federation.error.network");

  return (
    <div className="rounded-lg border border-danger/30 bg-danger/5 p-3 text-sm" role="alert">
      <div className="flex items-start justify-between gap-3">
        <p>{text}</p>
        {onDismiss && <DismissButton onClick={onDismiss} />}
      </div>
      {error instanceof FederationError && (
        <p className="mt-1 text-xs text-default-500">{error.code}</p>
      )}
      {onRetry && (
        <button className={`${buttonClass} mt-2`} type="button" onClick={onRetry}>
          {t("federation.retry")}
        </button>
      )}
    </div>
  );
}

export function DismissButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation();

  return (
    <button
      aria-label={t("federation.dismiss")}
      className="-m-1 shrink-0 rounded p-1 leading-none text-default-500 hover:bg-default-100"
      type="button"
      onClick={onClick}
    >
      ×
    </button>
  );
}

export function SourceBadge({ label, local }: { label: string; local?: boolean }) {
  const { t } = useTranslation();

  return (
    <span className="inline-flex max-w-full items-center gap-1.5 rounded-md bg-primary/10 px-2 py-1 text-xs text-primary">
      <span aria-hidden>{local ? "◉" : "◎"}</span>
      <span className="truncate">{label}</span>
      {local && <span>· {t("federation.thisDevice")}</span>}
    </span>
  );
}
