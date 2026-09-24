"use client";

import type { NoticeDefinition } from "./registry";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineLeft, AiOutlineNotification, AiOutlineRight } from "react-icons/ai";

import NoticeContent from "./NoticeContent";

import { Button, Modal } from "@/components/bakaui";

export interface NoticesDialogProps {
  /** The unread notices, in reading order. Shrinks as they are read, here or elsewhere. */
  notices: NoticeDefinition[];
  onRead: (id: string) => void;
  onReadAll: () => void;
  onAct: (notice: NoticeDefinition) => void;
  /** Closed without reading everything: the rest come back next launch. */
  onDismiss: () => void;
}

/**
 * The unread notices, one page each. "Got it" marks the one on screen read and brings the
 * next; "Mark all as read" ends it at once. Closing it (the close button or Escape) keeps
 * the rest unread for the next launch; a click beside it does nothing, so they are not lost
 * to a stray click.
 */
const NoticesDialog = ({ notices, onRead, onReadAll, onAct, onDismiss }: NoticesDialogProps) => {
  const { t } = useTranslation();
  const [page, setPage] = useState(0);
  const gotIt = useRef<HTMLButtonElement>(null);
  // A read notice leaves the list, so the one after it takes its place on the same page;
  // after the last one, the page before it.
  const index = Math.max(0, Math.min(page, notices.length - 1));
  const current = notices[index];
  const total = notices.length;

  if (!current) return null;

  const turnTo = (next: number) => {
    setPage(next);
    // The pager button that was pressed turns itself off at either end, and focus would
    // fall out of the dialog with it — taking Escape and the keyboard along.
    if (next == 0 || next == total - 1) gotIt.current?.focus();
  };

  return (
    <Modal
      visible
      footer={
        <div className="flex w-full flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-1">
            {total > 1 && (
              <>
                <Button
                  isIconOnly
                  aria-label={t("notices.dialog.previous")}
                  isDisabled={index == 0}
                  size="sm"
                  variant="light"
                  onPress={() => turnTo(index - 1)}
                >
                  <AiOutlineLeft />
                </Button>
                <span
                  aria-live="polite"
                  className="min-w-12 text-center text-xs tabular-nums text-default-500"
                >
                  {t("notices.dialog.position", { current: index + 1, total })}
                </span>
                <Button
                  isIconOnly
                  aria-label={t("notices.dialog.next")}
                  isDisabled={index == total - 1}
                  size="sm"
                  variant="light"
                  onPress={() => turnTo(index + 1)}
                >
                  <AiOutlineRight />
                </Button>
              </>
            )}
          </div>
          <div className="flex items-center gap-2">
            {total > 1 && (
              <Button variant="light" onPress={onReadAll}>
                {t("notices.action.markAllRead")}
              </Button>
            )}
            <Button ref={gotIt} color="primary" onPress={() => onRead(current.id)}>
              {t("notices.action.gotIt")}
            </Button>
          </div>
        </div>
      }
      isDismissable={false}
      size="md"
      title={
        <div className="flex items-center gap-2">
          <AiOutlineNotification className="text-lg" />
          <span>{t("notices.dialog.title")}</span>
        </div>
      }
      onClose={onDismiss}
    >
      {/* Pages of different length would move the pager under the pointer between presses. */}
      <div className={total > 1 ? "min-h-[22rem] py-1" : "py-1"}>
        <NoticeContent notice={current} onAct={() => onAct(current)} />
      </div>
    </Modal>
  );
};

NoticesDialog.displayName = "NoticesDialog";

export default NoticesDialog;
