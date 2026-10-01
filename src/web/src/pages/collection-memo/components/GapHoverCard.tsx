import type { RefObject } from "react";
import type { TimelineRegion } from "../helpers";

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

interface Props {
  region: TimelineRegion;
  label: string;
  track: RefObject<HTMLDivElement>;
  disabled: boolean;
  suppressed: boolean;
  error?: string;
  onFill?: () => Promise<boolean>;
}

/** Keep the card open while the pointer crosses from the timeline to its action. */
const GapHoverCard = ({ region, label, track, disabled, suppressed, error, onFill }: Props) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState({ left: 0, top: 0 });
  const trigger = useRef<HTMLButtonElement>(null);
  const card = useRef<HTMLDivElement>(null);
  const closeTimer = useRef<number>();
  const restoreFocus = useRef(false);
  const focusAction = useRef(false);
  const holdOpen = useCallback(() => {
    window.clearTimeout(closeTimer.current);
  }, []);
  const show = () => {
    if (suppressed) return;
    holdOpen();
    const trackRect = track.current?.getBoundingClientRect();
    const rect = trigger.current?.getBoundingClientRect();

    if (rect && trackRect) {
      const width = Math.min(320, window.innerWidth - 32);

      setPosition({
        left: Math.max(
          16,
          Math.min(window.innerWidth - width - 16, rect.left + rect.width / 2 - width / 2),
        ),
        top: trackRect.bottom + 8,
      });
    }
    setOpen(true);
  };
  const scheduleClose = (focusDeparted = false) => {
    holdOpen();
    if (
      !focusDeparted &&
      (trigger.current?.contains(document.activeElement) ||
        card.current?.contains(document.activeElement))
    )
      return;
    if (!disabled) closeTimer.current = window.setTimeout(() => setOpen(false), 180);
  };
  const onBlur = (next: EventTarget | null) => {
    if (next instanceof Node && (trigger.current?.contains(next) || card.current?.contains(next)))
      return;
    scheduleClose(true);
  };
  const focusFillAction = () => {
    if (suppressed || disabled) return;
    focusAction.current = true;
    show();
    const action = card.current?.querySelector<HTMLButtonElement>("button:not(:disabled)");

    if (open) {
      focusAction.current = false;
      action?.focus();
    }
  };

  useEffect(() => () => window.clearTimeout(closeTimer.current), []);
  useLayoutEffect(() => {
    if (open && focusAction.current) {
      focusAction.current = false;
      card.current?.querySelector<HTMLButtonElement>("button:not(:disabled)")?.focus();
    }
  }, [open]);
  useLayoutEffect(() => {
    if (!open) return;
    const trackRect = track.current?.getBoundingClientRect();
    const cardRect = card.current?.getBoundingClientRect();

    if (!trackRect || !cardRect) return;
    const below = trackRect.bottom + 8;
    const top =
      below + cardRect.height <= window.innerHeight - 16
        ? below
        : Math.max(16, trackRect.top - cardRect.height - 8);

    setPosition((current) => (current.top === top ? current : { ...current, top }));
  }, [open, disabled, error, track]);
  useEffect(() => {
    if (suppressed) setOpen(false);
    if (disabled) holdOpen();
  }, [disabled, suppressed, holdOpen]);
  useEffect(() => {
    if (!open) return;
    const keyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        if (card.current?.contains(document.activeElement)) {
          restoreFocus.current = true;
          trigger.current?.focus();
        }
      }
    };
    const outside = (event: PointerEvent) => {
      if (
        event.target instanceof Node &&
        !trigger.current?.contains(event.target) &&
        !card.current?.contains(event.target)
      )
        setOpen(false);
    };
    const scroll = () => setOpen(false);

    document.addEventListener("keydown", keyDown);
    document.addEventListener("pointerdown", outside);
    window.addEventListener("scroll", scroll, true);

    return () => {
      document.removeEventListener("keydown", keyDown);
      document.removeEventListener("pointerdown", outside);
      window.removeEventListener("scroll", scroll, true);
    };
  }, [open]);

  return (
    <>
      <button
        ref={trigger}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={label}
        className="absolute top-0 h-full rounded-sm border-0 bg-default-200 p-0 outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary"
        style={{
          left: `${region.left}%`,
          width: `${region.width}%`,
          minWidth: region.width === 0 ? 4 : undefined,
        }}
        type="button"
        onBlur={(event) => onBlur(event.relatedTarget)}
        onClick={show}
        onFocus={() => {
          if (restoreFocus.current) {
            restoreFocus.current = false;

            return;
          }
          show();
        }}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " " || event.key === "ArrowDown") {
            event.preventDefault();
            focusFillAction();
          }
        }}
        onPointerEnter={show}
        onPointerLeave={() => scheduleClose()}
      />
      {open &&
        !suppressed &&
        createPortal(
          <div
            ref={card}
            aria-label={label}
            className="fixed z-[100] flex max-h-[calc(100vh-2rem)] w-80 max-w-[calc(100vw-2rem)] flex-col gap-2 overflow-y-auto rounded-large border border-default-200 bg-content1 p-3 text-sm shadow-lg"
            role="dialog"
            style={position}
            onBlurCapture={(event) => onBlur(event.relatedTarget)}
            onFocusCapture={holdOpen}
            onPointerEnter={holdOpen}
            onPointerLeave={() => scheduleClose()}
          >
            <p>{label}</p>
            {onFill && (
              <Button
                color="success"
                isDisabled={disabled}
                isLoading={disabled}
                size="sm"
                onPress={async () => {
                  if (await onFill()) setOpen(false);
                }}
              >
                {t<string>("collectionMemo.timeline.fillGap")}
              </Button>
            )}
            {error && (
              <p className="text-danger" role="alert">
                {error}
              </p>
            )}
          </div>,
          document.body,
        )}
    </>
  );
};

export default GapHoverCard;
