"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineCopy } from "react-icons/ai";

import { Button, Tooltip, toast } from "@/components/bakaui";
import { copyTextToClipboard } from "@/core/clipboard";

type Props = {
  message: string;
};

/**
 * How long a click on the text waits to see whether it is the start of a double or triple click.
 * Those select a word or a line, and copying everything on their first click would overwrite the
 * clipboard (and announce it) just as the user is selecting something smaller.
 */
export const CLICK_TO_COPY_DELAY_MS = 300;

/**
 * The full failure message of a download task, copyable in one click.
 *
 * These messages are mostly stack traces, and the only thing anyone does with one is paste it
 * somewhere — an issue, a chat, a search box. Selecting a few hundred lines of a scrolling block
 * by hand was the only way to do that. Clicking the text copies all of it; dragging, double- and
 * triple-clicking still select normally, so part of it can be copied the usual way.
 */
const TaskErrorMessage = ({ message }: Props) => {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const resetTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const pendingClickRef = useRef<ReturnType<typeof setTimeout>>();

  useEffect(
    () => () => {
      clearTimeout(resetTimerRef.current);
      clearTimeout(pendingClickRef.current);
    },
    [],
  );

  const copy = async () => {
    try {
      await copyTextToClipboard(message);
    } catch {
      toast.danger(t<string>("common.message.copyFailed"));

      return;
    }
    toast.success(t<string>("common.message.copiedToClipboard"));
    setCopied(true);
    clearTimeout(resetTimerRef.current);
    resetTimerRef.current = setTimeout(() => setCopied(false), 2000);
  };

  const copyLabel = t<string>(copied ? "common.state.copied" : "common.action.copy");

  return (
    <div className="relative">
      <Tooltip content={copyLabel}>
        <Button
          isIconOnly
          aria-label={t<string>("common.action.copy")}
          className="absolute right-2 top-2 z-10"
          size="sm"
          variant="flat"
          onPress={copy}
        >
          {copied ? (
            <AiOutlineCheck aria-hidden className="text-base text-success" />
          ) : (
            <AiOutlineCopy aria-hidden className="text-base" />
          )}
        </Button>
      </Tooltip>
      {/* Pointer shortcut only: the button above is the keyboard-reachable way to copy. */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-noninteractive-element-interactions */}
      <pre
        className="max-h-[60vh] cursor-copy overflow-auto whitespace-pre-wrap break-words rounded-lg bg-default-100 p-3 pr-12 font-mono text-xs leading-5"
        title={t<string>("downloader.tip.clickToCopyError")}
        onClick={(e) => {
          clearTimeout(pendingClickRef.current);
          // The second or third click of a word/line selection.
          if (e.detail > 1) {
            return;
          }
          pendingClickRef.current = setTimeout(() => {
            // A drag, or a double click that has since selected a word, selected text on purpose;
            // copying everything over it would throw that selection away.
            if (window.getSelection()?.toString()) {
              return;
            }
            void copy();
          }, CLICK_TO_COPY_DELAY_MS);
        }}
      >
        {message}
      </pre>
    </div>
  );
};

export default TaskErrorMessage;
