import type { ReactNode } from "react";

import { Modal, ModalContent, ModalFooter, ModalHeader } from "@heroui/react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import DataSyncHelp from "./DataSyncHelp";

/** Focus that is nowhere — on the page's body, or on an element gone from the page — goes here. */
const keepFocusIn = (heading: HTMLElement | null) => {
  const active = document.activeElement;

  if (active && active !== document.body && active.isConnected) return;
  heading?.focus();
};

/**
 * A modal of data sync's own, for what takes more than a yes or a no: the "sync with another
 * device" wizard, a one-time code, the first sync review, the undo preview. A HeroUI modal, so
 * Tab stays inside and Escape closes it (unless it is busy); it takes the keyboard on its
 * heading, focus it takes away goes back there, and on closing focus goes back to what opened
 * it — or, when what opened it went away meanwhile (the review's link, gone once the first sync
 * is done), to `returnFocus`.
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
  // What had the keyboard before the dialog took it, read before the modal moves focus.
  const [opener] = useState(() =>
    document.activeElement instanceof HTMLElement ? document.activeElement : null,
  );
  const latestReturn = useRef(returnFocus);

  latestReturn.current = returnFocus;

  useEffect(() => {
    heading.current?.focus();

    return () => {
      if (opener?.isConnected && !opener.matches(":disabled")) opener.focus();
      else latestReturn.current?.()?.focus();
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

  return (
    <Modal
      ref={dialog}
      hideCloseButton
      isOpen
      data-testid={testId}
      isDismissable={false}
      isKeyboardDismissDisabled={busy}
      scrollBehavior="inside"
      size={wide ? "4xl" : "xl"}
      onClose={onClose}
    >
      <ModalContent>
        <div className="flex items-start justify-between gap-3 px-6 pt-4">
          <ModalHeader
            ref={heading}
            as="h2"
            className="p-0 text-base font-semibold outline-none"
            tabIndex={-1}
          >
            {title}
          </ModalHeader>
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
        </div>
        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto px-6 py-2">{children}</div>
        {footer && <ModalFooter className="flex-wrap">{footer}</ModalFooter>}
      </ModalContent>
    </Modal>
  );
}
