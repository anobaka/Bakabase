import type { ReactNode } from "react";

import { useEffect, useId, useRef } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import { panelClass } from "./common";
import DataSyncHelp from "./DataSyncHelp";

const focusableSelector =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Focus that is nowhere — on the page's body, or on an element gone from the page — goes here. */
const keepFocusIn = (heading: HTMLElement | null) => {
  const active = document.activeElement;

  if (active && active !== document.body && active.isConnected) return;
  heading?.focus();
};

/**
 * A modal of data sync's own, for what takes more than a yes or a no: the "sync with another
 * device" wizard, a one-time code. Portalled over the page like the multi-device pages'
 * confirmations; Tab stays inside, focus it takes away goes to its heading, Escape closes it
 * (unless it is busy), and focus goes back to what opened it — or, when what opened it went
 * away meanwhile (the review's link, gone once the first sync is done), to `returnFocus`.
 */
export default function DataSyncDialog({
  title,
  children,
  footer,
  busy = false,
  onClose,
  returnFocus,
  testId,
  wide = false,
}: {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  busy?: boolean;
  onClose: () => void;
  /** Where the keyboard goes on closing when what opened the dialog is no longer there. */
  returnFocus?: () => HTMLElement | null | undefined;
  testId?: string;
  /** For what needs room: the first sync review, the undo preview. */
  wide?: boolean;
}) {
  const { t } = useTranslation();
  const dialog = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const titleId = useId();
  const latest = useRef({ busy, onClose, returnFocus });

  latest.current = { busy, onClose, returnFocus };

  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const onKeyDown = (event: KeyboardEvent) => {
      const active = document.activeElement;

      // A dialog opened over this one (the help center) has the keyboard: it is its to handle.
      if (
        active instanceof Element &&
        !dialog.current?.contains(active) &&
        active.closest('[role="dialog"], [aria-modal="true"]')
      )
        return;
      if (event.key === "Escape") {
        if (event.defaultPrevented) return;
        event.preventDefault();
        if (!latest.current.busy) latest.current.onClose();

        return;
      }
      if (event.key !== "Tab" || !dialog.current) return;
      const focusable = Array.from(dialog.current.querySelectorAll<HTMLElement>(focusableSelector));
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
    heading.current?.focus();

    return () => {
      document.removeEventListener("keydown", onKeyDown);
      if (opener?.isConnected && !opener.matches(":disabled")) opener.focus();
      else latest.current.returnFocus?.()?.focus();
    };
  }, []);

  // Focus the dialog itself takes away — a pressed control disabled while its action runs
  // (Chromium moves focus off it to the page's body at once), or replaced by what the action
  // answered — goes to its heading: the keyboard stays in the dialog, and a screen reader reads
  // on from there. Checked on every change inside the dialog, and whenever it renders.
  useEffect(() => {
    const element = dialog.current;

    if (!element || typeof MutationObserver !== "function") return;
    const observer = new MutationObserver(() => keepFocusIn(heading.current));

    observer.observe(element, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["disabled"],
    });

    return () => observer.disconnect();
  }, []);
  useEffect(() => keepFocusIn(heading.current));

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <section
        ref={dialog}
        aria-labelledby={titleId}
        aria-modal="true"
        className={`${panelClass} flex max-h-[calc(100vh-2rem)] w-full ${
          wide ? "max-w-4xl" : "max-w-xl"
        } flex-col gap-3 shadow-xl`}
        data-testid={testId}
        role="dialog"
      >
        <header className="flex items-start justify-between gap-3">
          <h2 ref={heading} className="font-semibold outline-none" id={titleId} tabIndex={-1}>
            {title}
          </h2>
          <div className="-m-1 flex shrink-0 items-center gap-1">
            <DataSyncHelp />
            <button
              aria-label={t("dataSync.close")}
              className="rounded p-1 text-default-500 hover:bg-default-100 disabled:opacity-50"
              disabled={busy}
              type="button"
              onClick={onClose}
            >
              <AiOutlineClose aria-hidden />
            </button>
          </div>
        </header>
        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto">{children}</div>
        {footer && <div className="flex flex-wrap justify-end gap-2 pt-1">{footer}</div>}
      </section>
    </div>,
    document.body,
  );
}
