import type { DataSyncConfirmation } from "../hooks/useDataSyncActions";

import { useEffect, useId, useRef } from "react";
import { useTranslation } from "react-i18next";

import { buttonClass, DataSyncErrorNotice, primaryClass } from "./common";

/**
 * A question asked inside a data sync dialog (the first sync, the wizard) rather than in a dialog
 * over it: a second one on top would fight it for the keyboard, and react-aria would hide it from
 * screen readers. Takes the keyboard when it appears; Cancel gives it back where the caller says.
 */
export default function InlineConfirmation({
  confirmation,
  busy,
  error,
  onConfirm,
  onCancel,
}: {
  confirmation: DataSyncConfirmation;
  busy: boolean;
  error?: Error;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const root = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const titleId = useId();
  const latest = useRef({ busy, onCancel });

  latest.current = { busy, onCancel };

  useEffect(() => heading.current?.focus(), []);

  // Escape answers the question, and only the question: it never reaches the preview's dialog.
  useEffect(() => {
    const element = root.current;

    if (!element) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      event.stopPropagation();
      if (!latest.current.busy) latest.current.onCancel();
    };

    element.addEventListener("keydown", onKeyDown);

    return () => element.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <section
      ref={root}
      aria-labelledby={titleId}
      className="space-y-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm"
      data-testid="data-sync-review-confirm"
      role="group"
    >
      <h3 ref={heading} className="font-medium outline-none" id={titleId} tabIndex={-1}>
        {confirmation.title}
      </h3>
      <p className="text-xs">{confirmation.description}</p>
      {confirmation.warning && (
        <p className="text-xs font-medium" data-testid="data-sync-review-confirm-warning">
          {confirmation.warning}
        </p>
      )}
      <DataSyncErrorNotice error={error} />
      <div className="flex flex-wrap justify-end gap-2">
        <button className={buttonClass} disabled={busy} type="button" onClick={onCancel}>
          {t("dataSync.cancel")}
        </button>
        <button
          className={primaryClass}
          data-testid="data-sync-review-confirm-yes"
          disabled={busy}
          type="button"
          onClick={onConfirm}
        >
          {t("federation.confirm")}
        </button>
      </div>
    </section>
  );
}
