import type { TFunction } from "i18next";
import type { ReactNode } from "react";
import type { Tone } from "../viewModels";

import { useTranslation } from "react-i18next";

import { DataSyncProblemError, DataSyncRequestError } from "../api";

import { buttonClass, DismissButton, MessageError } from "@/features/federation/components/common";

/*
 * Pieces every data sync surface shares. The classes are the multi-device pages' own, so data
 * sync reads as part of the same mode.
 */

export {
  buttonClass,
  fieldClass,
  panelClass,
  primaryClass,
} from "@/features/federation/components/common";

/** A small button inside a sentence or a card. */
export const smallButtonClass =
  "inline-flex items-center justify-center gap-1.5 rounded-md border border-default-300 px-2 py-1 text-xs font-medium transition hover:bg-default-100 disabled:cursor-not-allowed disabled:opacity-50";
export const linkButtonClass = "text-xs text-primary underline disabled:opacity-50";

export const toneText: Record<Tone, string> = {
  success: "text-success-700 dark:text-success",
  primary: "text-primary",
  warning: "text-warning-700 dark:text-warning",
  danger: "text-danger",
  default: "text-default-500",
};

export const toneDot: Record<Tone, string> = {
  success: "bg-success",
  primary: "bg-primary",
  warning: "bg-warning",
  danger: "bg-danger",
  default: "bg-default-400",
};

export const toneFill: Record<Tone, string> = {
  success: "fill-success stroke-success",
  primary: "fill-primary stroke-primary",
  warning: "fill-warning stroke-warning",
  danger: "fill-danger stroke-danger",
  default: "fill-default-400 stroke-default-400",
};

/** A coloured dot in front of a status: not colour alone — the text beside it says the same. */
export function StatusDot({ tone }: { tone: Tone }) {
  return (
    <span
      aria-hidden
      className={`mt-1.5 inline-block h-2 w-2 shrink-0 rounded-full ${toneDot[tone]}`}
    />
  );
}

/** What went wrong with a data sync call, in the page's words. */
export const errorText = (t: TFunction, error: Error): string => {
  if (error instanceof DataSyncProblemError) return t(`dataSync.problem.${error.label}`);
  if (error instanceof DataSyncRequestError) {
    if (error.code === "HostOnly") return t("dataSync.notAvailable");
    if (error.code === "Network") return t("dataSync.error.network");

    return error.message || t("dataSync.error.server");
  }
  if (error instanceof MessageError && error.message) return error.message;

  return error.message || t("dataSync.error.network");
};

export function DataSyncErrorNotice({
  error,
  onRetry,
  onDismiss,
}: {
  error?: Error;
  onRetry?: () => void;
  onDismiss?: () => void;
}) {
  const { t } = useTranslation();

  if (!error) return null;

  return (
    <div
      className="rounded-lg border border-danger/30 bg-danger/5 p-3 text-sm"
      data-testid="data-sync-error"
      role="alert"
    >
      <div className="flex items-start justify-between gap-3">
        <p>{errorText(t, error)}</p>
        {onDismiss && <DismissButton onClick={onDismiss} />}
      </div>
      {error instanceof DataSyncProblemError && error.problem.detail && (
        <p className="mt-1 text-xs text-default-500">{error.problem.detail}</p>
      )}
      {onRetry && (
        <button className={`${buttonClass} mt-2`} type="button" onClick={onRetry}>
          {t("dataSync.retry")}
        </button>
      )}
    </div>
  );
}

/** A heading with the section's own help and actions on its right. */
export function SectionHeading({
  id,
  title,
  children,
}: {
  id: string;
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h2 className="text-base font-semibold" id={id}>
        {title}
      </h2>
      {children && <div className="flex flex-wrap items-center gap-2">{children}</div>}
    </div>
  );
}

/** A count on a card: solid for what waits here, hollow for what waits elsewhere. */
export function CountBubble({
  count,
  hollow = false,
  label,
}: {
  count: number;
  hollow?: boolean;
  label: string;
}) {
  if (count <= 0) return null;

  return (
    <span
      aria-label={label}
      className={`inline-flex min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-semibold leading-5 ${
        hollow
          ? "border border-warning text-warning-700 dark:text-warning"
          : "bg-warning text-warning-foreground"
      }`}
      role="img"
      title={label}
    >
      {count > 99 ? "99+" : count}
    </span>
  );
}
