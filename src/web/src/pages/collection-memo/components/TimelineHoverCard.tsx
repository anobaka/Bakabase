import type { ReactNode, RefObject } from "react";

import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

export interface TimelineHoverSource {
  id: string;
  label: string;
  description?: string;
  details?: ReactNode;
  interactive?: boolean;
  onFill?: () => Promise<boolean>;
  error?: string;
}

interface Options {
  sources: ReadonlyMap<string, TimelineHoverSource>;
  track: RefObject<HTMLDivElement>;
  disabled: boolean;
  suppressed: boolean;
}

interface Position {
  left: number;
  top: number;
  width: number;
  maxHeight: number;
}

const VIEWPORT_MARGIN = 16;
const TRACK_GAP = 8;
const CLOSE_DELAY = 180;

/** One hover owner keeps the timeline and its portal in the same focus/pointer group. */
export const useTimelineHover = ({ sources, track, disabled, suppressed }: Options) => {
  const { t } = useTranslation();
  const popupId = `collection-memo-hover-${useId()}`;
  const [activeId, setActiveId] = useState<string>();
  const [position, setPosition] = useState<Position>({ left: 0, top: 0, width: 0, maxHeight: 0 });
  const [isFilling, setIsFilling] = useState(false);
  const [, setFocusRequest] = useState(0);
  const activeIdRef = useRef<string>();
  const sourcesRef = useRef(sources);
  const disabledRef = useRef(disabled);
  const suppressedRef = useRef(suppressed);
  const anchorRef = useRef<HTMLElement>();
  const cardRef = useRef<HTMLDivElement>(null);
  const closeTimer = useRef<number>();
  const sideRef = useRef<"above" | "below">();
  const restoringFocus = useRef(false);
  const pendingFocus = useRef(false);
  const pointerInCard = useRef(false);
  const fillingRef = useRef(false);

  sourcesRef.current = sources;
  disabledRef.current = disabled;
  suppressedRef.current = suppressed;

  const holdOpen = useCallback(() => {
    window.clearTimeout(closeTimer.current);
    closeTimer.current = undefined;
  }, []);
  const close = useCallback(() => {
    holdOpen();
    activeIdRef.current = undefined;
    pendingFocus.current = false;
    pointerInCard.current = false;
    sideRef.current = undefined;
    setActiveId(undefined);
  }, [holdOpen]);
  const contains = useCallback(
    (target: EventTarget | null | undefined) =>
      target instanceof Node &&
      !!(anchorRef.current?.contains(target) || cardRef.current?.contains(target)),
    [],
  );

  const reposition = useCallback(
    (reselectSide = false) => {
      const trackRect = track.current?.getBoundingClientRect();
      const anchorRect = anchorRef.current?.getBoundingClientRect();

      if (!trackRect || !anchorRect || activeIdRef.current === undefined) return;
      const width = Math.min(320, window.innerWidth - VIEWPORT_MARGIN * 2);
      const belowTop = Math.max(VIEWPORT_MARGIN, trackRect.bottom + TRACK_GAP);
      const aboveBottom = Math.min(window.innerHeight - VIEWPORT_MARGIN, trackRect.top - TRACK_GAP);
      const belowSpace = Math.max(0, window.innerHeight - VIEWPORT_MARGIN - belowTop);
      const aboveSpace = Math.max(0, aboveBottom - VIEWPORT_MARGIN);
      const card = cardRef.current;
      const measuredHeight = card?.getBoundingClientRect().height ?? 0;
      const naturalHeight = Math.max(measuredHeight, card ? card.scrollHeight + 2 : 0);

      // Measure once per open session. Content updates cannot repeatedly flip the card
      // through the pointer; a viewport resize may choose a more suitable side.
      if ((sideRef.current === undefined || reselectSide) && card) {
        sideRef.current =
          naturalHeight <= belowSpace || belowSpace >= aboveSpace ? "below" : "above";
      }
      const side = sideRef.current ?? (belowSpace >= aboveSpace ? "below" : "above");
      const maxHeight = side === "below" ? belowSpace : aboveSpace;

      if (width <= 0 || maxHeight < 4) {
        close();

        return;
      }
      const next = {
        left: Math.max(
          VIEWPORT_MARGIN,
          Math.min(
            window.innerWidth - width - VIEWPORT_MARGIN,
            anchorRect.left + anchorRect.width / 2 - width / 2,
          ),
        ),
        top: side === "below" ? belowTop : aboveBottom - Math.min(measuredHeight, maxHeight),
        width,
        maxHeight,
      };

      setPosition((current) =>
        current.left === next.left &&
        current.top === next.top &&
        current.width === next.width &&
        current.maxHeight === next.maxHeight
          ? current
          : next,
      );
    },
    [close, track],
  );
  const show = useCallback(
    (id: string, anchor: HTMLElement, focusAction = false) => {
      if (restoringFocus.current || suppressedRef.current || !sourcesRef.current.has(id)) return;
      holdOpen();
      if (
        activeIdRef.current !== id &&
        cardRef.current?.contains(document.activeElement) &&
        !focusAction
      )
        return;
      if (activeIdRef.current !== id) pendingFocus.current = false;
      if (activeIdRef.current === undefined) sideRef.current = undefined;
      anchorRef.current = anchor;
      activeIdRef.current = id;
      setActiveId(id);
      reposition();
      if (focusAction) {
        pendingFocus.current = true;
        setFocusRequest((current) => current + 1);
      }
    },
    [holdOpen, reposition],
  );
  const scheduleClose = useCallback(
    (focusDeparted = false) => {
      holdOpen();
      if (pointerInCard.current || (focusDeparted && anchorRef.current?.matches(":hover"))) return;
      if (!focusDeparted && contains(document.activeElement)) return;
      closeTimer.current = window.setTimeout(() => {
        if (focusDeparted || !contains(document.activeElement)) close();
      }, CLOSE_DELAY);
    },
    [close, contains, holdOpen],
  );
  const leave = useCallback(
    (next?: EventTarget | null) => {
      if (contains(next)) holdOpen();
      else scheduleClose();
    },
    [contains, holdOpen, scheduleClose],
  );
  const blur = useCallback(
    (next: EventTarget | null) => {
      if (contains(next)) holdOpen();
      else scheduleClose(true);
    },
    [contains, holdOpen, scheduleClose],
  );

  useEffect(() => holdOpen, [holdOpen]);
  useLayoutEffect(() => {
    if (activeId === undefined) return;
    if (suppressed || !sources.has(activeId) || !anchorRef.current?.isConnected) {
      close();

      return;
    }
    reposition();
    if (pendingFocus.current) {
      const action = cardRef.current?.querySelector<HTMLElement>(
        'button:not(:disabled), [role="link"], a[href]',
      );

      if (action && !disabled && !isFilling) {
        pendingFocus.current = false;
        action.focus({ preventScroll: true });
      }
    }
  });
  useEffect(() => {
    if (activeId === undefined) return;
    const keyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      const anchor = anchorRef.current;
      const restore = cardRef.current?.contains(document.activeElement);

      close();
      if (restore && anchor?.isConnected) {
        // Restoring keyboard focus fires the trigger's onFocus synchronously.
        restoringFocus.current = true;
        anchor.focus({ preventScroll: true });
        restoringFocus.current = false;
      }
    };
    const outside = (event: PointerEvent) => {
      if (!contains(event.target)) close();
    };
    const scroll = (event: Event) => {
      if (event.target instanceof Node && cardRef.current?.contains(event.target)) return;
      close();
    };
    const resize = () => reposition(true);
    const observer =
      typeof ResizeObserver === "undefined" ? undefined : new ResizeObserver(() => reposition());

    if (track.current) observer?.observe(track.current);
    if (cardRef.current) observer?.observe(cardRef.current);
    document.addEventListener("keydown", keyDown);
    document.addEventListener("pointerdown", outside);
    window.addEventListener("scroll", scroll, true);
    window.addEventListener("resize", resize);

    return () => {
      observer?.disconnect();
      document.removeEventListener("keydown", keyDown);
      document.removeEventListener("pointerdown", outside);
      window.removeEventListener("scroll", scroll, true);
      window.removeEventListener("resize", resize);
    };
  }, [activeId, close, contains, reposition, track]);

  const source = activeId === undefined ? undefined : sources.get(activeId);
  const popup: ReactNode =
    source && !suppressed
      ? createPortal(
          <div
            ref={cardRef}
            aria-label={source.label}
            className="fixed z-[100] overflow-y-auto rounded-large border border-default-200 bg-content1 text-sm shadow-lg"
            data-collection-memo-hover-card=""
            id={popupId}
            role={source.onFill || source.interactive ? "dialog" : "tooltip"}
            style={position}
            onBlurCapture={(event) => blur(event.relatedTarget)}
            onFocusCapture={holdOpen}
            onPointerEnter={() => {
              pointerInCard.current = true;
              holdOpen();
            }}
            onPointerLeave={(event) => {
              pointerInCard.current = false;
              leave(event.relatedTarget);
            }}
          >
            <div className="flex flex-col gap-2 p-3">
              <p>{source.label}</p>
              {source.description && <p className="text-default-500">{source.description}</p>}
              {source.details}
              {source.onFill && (
                <Button
                  color="success"
                  isDisabled={disabled || isFilling}
                  isLoading={isFilling}
                  size="sm"
                  type="button"
                  onPress={async () => {
                    const id = activeIdRef.current;
                    const onFill =
                      id === undefined ? undefined : sourcesRef.current.get(id)?.onFill;

                    if (!onFill || disabledRef.current || fillingRef.current) return;
                    fillingRef.current = true;
                    setIsFilling(true);
                    try {
                      if ((await onFill()) && activeIdRef.current === id) close();
                    } finally {
                      fillingRef.current = false;
                      setIsFilling(false);
                    }
                  }}
                >
                  {t<string>("collectionMemo.timeline.fillGap")}
                </Button>
              )}
              {source.error && (
                <p className="text-danger" role="alert">
                  {source.error}
                </p>
              )}
            </div>
          </div>,
          document.body,
        )
      : null;

  return { activeId, popupId, show, leave, blur, close, popup };
};
