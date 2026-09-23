import { useEffect, useId, useRef } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";

import { buttonClass, ErrorNotice, panelClass, primaryClass } from "./common";

const focusableSelector =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * A modal confirmation. It is portalled over the viewport so it is visible wherever the
 * triggering button sits on a long page; the page behind it cannot be reached until the
 * user confirms or cancels.
 */
export default function ConfirmDialog({
  title,
  description,
  warning,
  error,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  description: string;
  warning?: string;
  error?: Error;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const dialog = useRef<HTMLElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const latest = useRef({ busy, onCancel });

  latest.current = { busy, onCancel };

  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        if (!latest.current.busy) latest.current.onCancel();

        return;
      }
      if (event.key !== "Tab" || !dialog.current) return;
      const focusable = Array.from(dialog.current.querySelectorAll<HTMLElement>(focusableSelector));
      const active = document.activeElement;
      const inside = !!active && dialog.current.contains(active);

      if (!focusable.length) {
        event.preventDefault();

        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && (!inside || active === first)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (!inside || active === last)) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
      if (opener?.isConnected) opener.focus();
    };
  }, []);

  // Initial focus, and again after a failed attempt re-enables the (briefly disabled) button.
  useEffect(() => {
    if (!busy && !dialog.current?.contains(document.activeElement)) confirmButton.current?.focus();
  }, [busy]);

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <section
        ref={dialog}
        aria-describedby={descriptionId}
        aria-labelledby={titleId}
        aria-modal="true"
        className={`${panelClass} max-h-[calc(100vh-2rem)] w-full max-w-lg space-y-3 overflow-y-auto shadow-xl`}
        role="alertdialog"
      >
        <h2 className="font-semibold" id={titleId}>
          {title}
        </h2>
        <p className="text-sm" id={descriptionId}>
          {description}
        </p>
        {warning && (
          <p className="rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm">{warning}</p>
        )}
        <ErrorNotice error={error} />
        <div className="flex flex-wrap justify-end gap-2 pt-1">
          <button className={buttonClass} disabled={busy} type="button" onClick={onCancel}>
            {t("federation.cancel")}
          </button>
          <button
            ref={confirmButton}
            className={primaryClass}
            disabled={busy}
            type="button"
            onClick={onConfirm}
          >
            {t("federation.confirm")}
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
