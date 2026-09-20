import type { ReactNode } from "react";

import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { FederationError } from "../transport";

import { useRemoteAccessStore, useIsPureClient } from "@/stores/remoteAccess";

export const fieldClass =
  "w-full rounded-lg border border-default-300 bg-content1 px-3 py-2 text-sm outline-none focus:border-primary focus:ring-2 focus:ring-primary/15 disabled:opacity-50";
export const buttonClass =
  "inline-flex items-center justify-center gap-2 rounded-lg border border-default-300 px-3 py-2 text-sm font-medium transition hover:bg-default-100 disabled:cursor-not-allowed disabled:opacity-50";
export const primaryClass = `${buttonClass} !border-primary bg-primary text-primary-foreground hover:!bg-primary/90`;
export const panelClass = "rounded-xl border border-default-200 bg-content1 p-4";

export function FederationAccess({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const local = useRemoteAccessStore((state) => state.isLocal);
  const pureClient = useIsPureClient();

  if (!initialized)
    return (
      <div className="p-6" role="status">
        {t("federation.loading")}
      </div>
    );
  if (pureClient || !local) {
    return (
      <div className="mx-auto flex max-w-2xl flex-col gap-4 p-6">
        <h1 className="text-xl font-semibold">{t("federation.title")}</h1>
        <p>{t(pureClient ? "federation.migration.intro" : "federation.localOnly")}</p>
        {pureClient && (
          <p className="text-sm text-default-500">{t("federation.migration.security")}</p>
        )}
        <Link className={buttonClass} to="/other-devices">
          {t("federation.migration.download")}
        </Link>
      </div>
    );
  }

  return <>{children}</>;
}

export function ErrorNotice({ error, onRetry }: { error?: Error; onRetry?: () => void }) {
  const { t, i18n } = useTranslation();

  if (!error) return null;
  const key =
    error instanceof FederationError
      ? `federation.error.${error.code}`
      : "federation.error.network";
  const known = typeof i18n.exists === "function" && i18n.exists(key);

  return (
    <div className="rounded-lg border border-danger/30 bg-danger/5 p-3 text-sm" role="alert">
      <p>{known ? t(key) : error.message || t("federation.error.network")}</p>
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
