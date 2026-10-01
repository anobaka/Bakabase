"use client";

import type { SyntheticEvent } from "react";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineCopy } from "react-icons/ai";

import { Button, Tooltip, toast } from "@/components/bakaui";
import { copyTextToClipboard } from "@/core/clipboard";

const stopPropagation = (event: SyntheticEvent) => {
  if (!("continuePropagation" in event)) event.stopPropagation();
};

export default function TaskLinkCopyButton({ value }: { value?: string }) {
  const { t } = useTranslation();
  const [copiedValue, setCopiedValue] = useState<string>();
  const [pending, setPending] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout>>();
  const pendingRef = useRef(false);
  const mounted = useRef(true);
  const currentValue = useRef(value);

  currentValue.current = value;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      clearTimeout(timer.current);
    };
  }, []);

  const copied = value != null && copiedValue === value;
  const label = t<string>("downloader.action.copyDownloadLink");
  const copy = async () => {
    if (!value?.trim() || pendingRef.current) return;

    pendingRef.current = true;
    setPending(true);
    setCopiedValue(undefined);
    clearTimeout(timer.current);
    try {
      await copyTextToClipboard(value);
      if (!mounted.current || currentValue.current !== value) return;

      setCopiedValue(value);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopiedValue(undefined), 2000);
    } catch {
      if (mounted.current) toast.danger(t<string>("common.message.copyFailed"));
    } finally {
      pendingRef.current = false;
      if (mounted.current) setPending(false);
    }
  };

  return (
    <span
      data-task-action
      className="inline-flex shrink-0 items-center gap-1"
      role="presentation"
      onClick={stopPropagation}
      onContextMenu={stopPropagation}
      onKeyDown={stopPropagation}
    >
      <Tooltip content={copied ? t<string>("common.state.copied") : label}>
        <Button
          isIconOnly
          aria-label={label}
          className="h-5 min-h-0 w-5 min-w-0"
          isDisabled={!value?.trim() || pending}
          size="sm"
          variant="light"
          onPress={copy}
        >
          {copied ? (
            <AiOutlineCheck aria-hidden className="text-sm text-success" />
          ) : (
            <AiOutlineCopy aria-hidden className="text-sm" />
          )}
        </Button>
      </Tooltip>
      <span aria-live="polite" className="text-xs text-success">
        {copied ? t<string>("common.state.copied") : null}
      </span>
    </span>
  );
}
