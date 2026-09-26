import type { TFunction } from "i18next";
import type { ComponentProps, ReactNode, Ref } from "react";
import type { Tone } from "../viewModels";
import type { BTask } from "@/core/models/BTask";
import type { DataSyncProblemCode } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import { DataSyncProblemError, DataSyncRequestError } from "../api";

import ConfirmDialog from "@/features/federation/components/ConfirmDialog";
import { buttonClass, DismissButton, MessageError } from "@/features/federation/components/common";
import { DataSyncProblemCodeLabel } from "@/sdk/constants";

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

/**
 * Data sync's colour for words — labels on arrows, the mode on a line, kind chips, a device's
 * name: the device map's secondary one step deeper, which is darker in the light theme and
 * lighter in the dark one (HeroUI swaps its scale), so 10–11 px text keeps AA contrast where the
 * lines' own colour would not. Lines and arrowheads keep the map's `edgeStyles.sync`.
 */
export const syncText = "text-secondary-600";
export const syncFill = "fill-secondary-600";

/** A small button inside a sentence or a card. */
export const smallButtonClass =
  "inline-flex items-center justify-center gap-1.5 rounded-md border border-default-300 px-2 py-1 text-xs font-medium transition hover:bg-default-100 disabled:cursor-not-allowed disabled:opacity-50";
export const linkButtonClass = "text-xs text-primary-700 underline disabled:opacity-50";

/**
 * A status in words, at 12–14 px: every tone a step deep enough for AA contrast on the page's
 * panels and the status block's `default-50` in both themes (HeroUI swaps the -700 shades for
 * light ones in the dark theme).
 */
export const toneText: Record<Tone, string> = {
  success: "text-success-700 dark:text-success",
  primary: "text-primary-700",
  warning: "text-warning-700 dark:text-warning",
  danger: "text-danger-700",
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

/**
 * A problem's details that change what it means, by problem: each has words of its own
 * (`dataSync.problem.detail.{code}.{detail}`), said instead of the problem's. Every other detail
 * the server sends — an id, a token, a sentence of its own in English — is for whoever looks into
 * it, never copy: it is shown only under "Technical details".
 */
export const wordedProblemDetails: Partial<
  Record<keyof typeof DataSyncProblemCode, readonly string[]>
> = {
  DecisionsInvalid: ["tooEarly", "noRestore", "notInitialized", "notAwaitingAccess"],
  Busy: ["newDefinitionsStayLocal", "notRunning", "stopping"],
  UndoNotAvailable: ["undone", "undo", "expired", "notFound"],
  NothingToReview: ["applied"],
};

/** The words for a problem's detail, when it has some (see {@link wordedProblemDetails}). */
const problemDetailKey = (error: DataSyncProblemError) => {
  const detail = error.problem.detail;

  return detail &&
    wordedProblemDetails[error.label as keyof typeof DataSyncProblemCode]?.includes(detail)
    ? `dataSync.problem.detail.${error.label}.${detail}`
    : undefined;
};

/** What went wrong with a data sync call, in the page's words. */
export const errorText = (t: TFunction, error: Error): string => {
  if (error instanceof DataSyncProblemError)
    return t(problemDetailKey(error) ?? `dataSync.problem.${error.label}`);
  if (error instanceof DataSyncRequestError) {
    if (error.code === "HostOnly") return t("dataSync.notAvailable");
    if (error.code === "Network") return t("dataSync.error.network");

    return error.message || t("dataSync.error.server");
  }
  if (error instanceof MessageError && error.message) return error.message;

  return error.message || t("dataSync.error.network");
};

/**
 * A failure of data sync's own, in data sync's words, for what shows an error by its message —
 * the multi-device confirmation, the device map; anything else as it is.
 */
export const wordedError = <T,>(t: TFunction, cause: T): T | MessageError =>
  cause instanceof DataSyncProblemError || cause instanceof DataSyncRequestError
    ? new MessageError(errorText(t, cause))
    : cause;

const problemCodeNames = new Set<string>(Object.values(DataSyncProblemCodeLabel));

/**
 * Why a data sync task failed, in the page's words: the problem it ended with (its brief error
 * names one), else `fallback`. Never the task's full error, which is a stack trace.
 */
export const taskFailureText = (t: TFunction, task: BTask | undefined, fallback: string) =>
  task?.briefError && problemCodeNames.has(task.briefError)
    ? t(`dataSync.problem.${task.briefError}`)
    : fallback;

/** The multi-device confirmation, with a failure of data sync's own said in data sync's words. */
export function DataSyncConfirmDialog(props: ComponentProps<typeof ConfirmDialog>) {
  const { t } = useTranslation();

  return <ConfirmDialog {...props} error={props.error && wordedError(t, props.error)} />;
}

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
  // A detail with no words of its own is for whoever looks into it, not for the reader.
  const technical =
    error instanceof DataSyncProblemError && !problemDetailKey(error)
      ? error.problem.detail
      : undefined;

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
      {technical && (
        <details className="mt-1 text-xs text-default-500" data-testid="data-sync-error-technical">
          <summary className="cursor-pointer select-none">{t("dataSync.error.technical")}</summary>
          <p className="mt-0.5 break-all font-mono">{technical}</p>
        </details>
      )}
      {onRetry && (
        <button className={`${buttonClass} mt-2`} type="button" onClick={onRetry}>
          {t("dataSync.retry")}
        </button>
      )}
    </div>
  );
}

/**
 * A heading with the section's own help and actions on its right. With `headingRef`, the heading
 * can be given the keyboard: where a link elsewhere on the page leads to the section.
 */
export function SectionHeading({
  id,
  title,
  headingRef,
  children,
}: {
  id: string;
  title: string;
  headingRef?: Ref<HTMLHeadingElement>;
  children?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h2
        ref={headingRef}
        className="text-base font-semibold outline-none"
        id={id}
        tabIndex={headingRef ? -1 : undefined}
      >
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
