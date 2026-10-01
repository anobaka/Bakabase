import type { PointerEvent as ReactPointerEvent, KeyboardEvent as ReactKeyboardEvent } from "react";
import type {
  CollectionMemoCoverageResize,
  CollectionMemoTarget,
  TimelineCoverage,
  TimelineDomain,
} from "../helpers";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  clampCoverageBoundary,
  getCoverageResizeBounds,
  getTimelineCoverage,
  getTimelineRegions,
  getTimestampTicks,
} from "../helpers";

import GapHoverCard from "./GapHoverCard";

import { Button, Tooltip } from "@/components/bakaui";

interface Props {
  target: CollectionMemoTarget;
  domain: TimelineDomain;
  formatDate: (value: number | string) => string;
  isSaving?: boolean;
  onFillGap?: (gap: { startAt: string; endAt: string }) => Promise<void>;
  onResizeCoverage?: (value: CollectionMemoCoverageResize) => Promise<void>;
}

type Mutation =
  | { kind: "fill"; value: { startAt: string; endAt: string } }
  | { kind: "resize"; value: CollectionMemoCoverageResize };
interface Gesture {
  pointerId: number;
  originX: number;
  width: number;
  domain: TimelineDomain;
  coverage: TimelineCoverage[];
  index: number;
  edge: "start" | "end";
  originalAt: string;
  at: string;
  bounds: { min: string; max: string };
  signature: string;
}

const signatureOf = (ranges: CollectionMemoTarget["ranges"]) =>
  JSON.stringify([...ranges].sort((a, b) => a.id - b.id));

const Timeline = ({
  target,
  domain,
  formatDate,
  isSaving = false,
  onFillGap,
  onResizeCoverage,
}: Props) => {
  const { t } = useTranslation();
  const hintId = useId();
  const track = useRef<HTMLDivElement>(null);
  const drag = useRef<Gesture>();
  const busy = useRef(false);
  const [preview, setPreview] = useState<{ gesture: Gesture; at: string }>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<{ changed?: boolean; action?: Mutation }>();
  const signature = signatureOf(target.ranges);
  const currentSignature = useRef(signature);

  currentSignature.current = signature;
  const disabled = pending || isSaving;
  const activeDomain = preview?.gesture.domain ?? domain;
  const coverage = preview
    ? preview.gesture.coverage.map((component, index) =>
        index === preview.gesture.index
          ? {
              ...component,
              ...(preview.gesture.edge === "start"
                ? { start: Date.parse(preview.at), startAt: preview.at }
                : { end: Date.parse(preview.at), endAt: preview.at }),
            }
          : component,
      )
    : getTimelineCoverage(target.ranges);
  const regions = getTimelineRegions(coverage, activeDomain);
  const errorText = error
    ? t<string>(
        error.changed ? "collectionMemo.timeline.changed" : "collectionMemo.timeline.saveFailed",
      )
    : undefined;

  const releasePointer = (gesture: Gesture) => {
    if (track.current?.hasPointerCapture?.(gesture.pointerId))
      track.current.releasePointerCapture(gesture.pointerId);
  };
  const cancel = (changed = false) => {
    const gesture = drag.current;

    drag.current = undefined;
    setPreview(undefined);
    if (gesture) releasePointer(gesture);
    if (changed) setError({ changed: true });
  };

  useEffect(() => {
    if (drag.current && drag.current.signature !== signature) cancel(true);
    else if (drag.current && isSaving) cancel();
  }, [signature, isSaving]);
  useEffect(() => {
    const keyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && drag.current) {
        event.preventDefault();
        cancel();
      }
    };

    document.addEventListener("keydown", keyDown);

    return () => {
      document.removeEventListener("keydown", keyDown);
      drag.current = undefined;
    };
  }, []);

  const runMutation = async (action: Mutation): Promise<boolean> => {
    if (busy.current || isSaving) {
      if (!busy.current) setPreview(undefined);

      return false;
    }
    busy.current = true;
    setPending(true);
    setError(undefined);
    try {
      if (action.kind === "fill") await onFillGap?.(action.value);
      else await onResizeCoverage?.(action.value);
      setPreview(undefined);

      return true;
    } catch (cause) {
      setPreview(undefined);
      const changed =
        typeof cause === "object" && cause !== null && "code" in cause && cause.code === 409;

      setError(changed ? { changed: true } : { action });

      return false;
    } finally {
      busy.current = false;
      setPending(false);
    }
  };

  const startDrag = (
    event: ReactPointerEvent<HTMLDivElement>,
    index: number,
    edge: "start" | "end",
  ) => {
    if (disabled || busy.current || !onResizeCoverage || event.button !== 0 || drag.current) return;
    const rect = track.current?.getBoundingClientRect();

    if (!rect || rect.width <= 0 || domain.end <= domain.start) return;
    event.preventDefault();
    event.stopPropagation();
    event.currentTarget.focus();
    const originalCoverage = getTimelineCoverage(target.ranges);
    const component = originalCoverage[index];
    const originalAt = edge === "start" ? component.startAt : component.endAt;
    const gesture: Gesture = {
      pointerId: event.pointerId,
      originX: event.clientX,
      width: rect.width,
      domain: { ...domain },
      coverage: originalCoverage,
      index,
      edge,
      originalAt,
      at: originalAt,
      bounds: getCoverageResizeBounds(originalCoverage, index, domain, edge),
      signature,
    };

    setError(undefined);
    drag.current = gesture;
    setPreview({ gesture, at: originalAt });
    track.current?.setPointerCapture(event.pointerId);
  };

  const pointerBoundary = (gesture: Gesture, clientX: number) => {
    const offset = clientX - gesture.originX;

    return Math.abs(offset) < 3
      ? gesture.originalAt
      : clampCoverageBoundary(
          Math.round(
            (Date.parse(gesture.originalAt) +
              (offset / gesture.width) * (gesture.domain.end - gesture.domain.start)) /
              1000,
          ) * 1000,
          gesture.bounds,
        );
  };

  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const gesture = drag.current;

    if (!gesture || gesture.pointerId !== event.pointerId) return;
    if (gesture.signature !== currentSignature.current) {
      cancel(true);

      return;
    }
    const at = pointerBoundary(gesture, event.clientX);

    gesture.at = at;
    setPreview({ gesture, at });
  };

  const finishDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const gesture = drag.current;

    if (!gesture || gesture.pointerId !== event.pointerId) return;
    gesture.at = pointerBoundary(gesture, event.clientX);
    drag.current = undefined;
    releasePointer(gesture);
    if (gesture.signature !== currentSignature.current) {
      setPreview(undefined);
      setError({ changed: true });

      return;
    }
    if (getTimestampTicks(gesture.at) === getTimestampTicks(gesture.originalAt)) {
      setPreview(undefined);

      return;
    }

    void runMutation({
      kind: "resize",
      value: { ranges: gesture.coverage[gesture.index].ranges, edge: gesture.edge, at: gesture.at },
    });
  };

  const resizeWithKeyboard = (
    event: ReactKeyboardEvent<HTMLDivElement>,
    index: number,
    edge: "start" | "end",
  ) => {
    if (event.key === "Escape" && drag.current) {
      event.preventDefault();
      cancel();

      return;
    }
    if (disabled || busy.current || drag.current || !onResizeCoverage) return;
    const backward = event.key === "ArrowLeft" || event.key === "ArrowDown";
    const forward = event.key === "ArrowRight" || event.key === "ArrowUp";

    if (!backward && !forward && event.key !== "Home" && event.key !== "End") return;
    event.preventDefault();
    const originalCoverage = getTimelineCoverage(target.ranges);
    const component = originalCoverage[index];
    const originalAt = edge === "start" ? component.startAt : component.endAt;
    const bounds = getCoverageResizeBounds(originalCoverage, index, domain, edge);
    const at =
      event.key === "Home"
        ? bounds.min
        : event.key === "End"
          ? bounds.max
          : clampCoverageBoundary(
              Date.parse(originalAt) + (backward ? -1 : 1) * (event.shiftKey ? 3_600_000 : 60_000),
              bounds,
            );

    if (getTimestampTicks(at) === getTimestampTicks(originalAt)) return;
    void runMutation({ kind: "resize", value: { ranges: component.ranges, edge, at } });
  };

  const retryAction = error?.action;
  const canRetry =
    retryAction?.kind === "resize"
      ? getTimelineCoverage(target.ranges).some(
          (component) => signatureOf(component.ranges) === signatureOf(retryAction.value.ranges),
        )
      : retryAction?.kind === "fill" &&
        getTimelineRegions(getTimelineCoverage(target.ranges), domain).some(
          (region) =>
            !region.collected &&
            region.startAt === retryAction.value.startAt &&
            region.endAt === retryAction.value.endAt,
        );

  return (
    <div
      aria-label={t<string>("collectionMemo.timeline.label", { name: target.name })}
      className="flex flex-col gap-1"
      role="group"
    >
      <div
        ref={track}
        data-collection-memo-track
        aria-busy={disabled}
        className="relative my-2 h-4 rounded-full bg-default-200"
        onLostPointerCapture={(event) => {
          if (drag.current?.pointerId === event.pointerId) cancel();
        }}
        onPointerCancel={(event) => {
          if (drag.current?.pointerId === event.pointerId) cancel();
        }}
        onPointerMove={moveDrag}
        onPointerUp={finishDrag}
      >
        {regions.map((region) => {
          const label = t<string>(
            region.point
              ? "collectionMemo.timeline.point"
              : region.collected
                ? "collectionMemo.timeline.collectedRange"
                : "collectionMemo.timeline.uncollectedRange",
            { start: formatDate(region.startAt), end: formatDate(region.endAt) },
          );
          const key = `${region.collected}:${region.startAt}:${region.endAt}`;

          if (!region.collected)
            return (
              <GapHoverCard
                key={key}
                disabled={disabled}
                error={
                  error?.action?.kind === "fill" &&
                  error.action.value.startAt === region.startAt &&
                  error.action.value.endAt === region.endAt
                    ? errorText
                    : undefined
                }
                label={label}
                region={region}
                suppressed={!!drag.current}
                track={track}
                onFill={
                  onFillGap
                    ? () =>
                        runMutation({
                          kind: "fill",
                          value: { startAt: region.startAt, endAt: region.endAt },
                        })
                    : undefined
                }
              />
            );

          return (
            <Tooltip key={key} content={label}>
              <button
                aria-label={label}
                className="absolute top-0 h-full rounded-sm border-0 bg-success p-0 outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary"
                style={{
                  left: `${region.left}%`,
                  width: region.point || region.width === 0 ? "4px" : `${region.width}%`,
                  transform: region.point ? "translateX(-50%)" : undefined,
                  zIndex: 1,
                }}
                type="button"
              />
            </Tooltip>
          );
        })}
        {onResizeCoverage &&
          coverage.map((component, index) =>
            (["start", "end"] as const).map((edge) => {
              const at = edge === "start" ? component.startAt : component.endAt;
              const bounds = getCoverageResizeBounds(coverage, index, activeDomain, edge);
              const left =
                activeDomain.end > activeDomain.start
                  ? ((Date.parse(at) - activeDomain.start) /
                      (activeDomain.end - activeDomain.start)) *
                    100
                  : 100;
              const label = t<string>(
                edge === "start"
                  ? "collectionMemo.timeline.resizeStart"
                  : "collectionMemo.timeline.resizeEnd",
                { date: formatDate(at) },
              );

              return (
                <Tooltip
                  key={`${component.ranges
                    .map((range) => range.id)
                    .sort((a, b) => a - b)
                    .join(",")}:${edge}`}
                  content={`${label} · ${t<string>("collectionMemo.timeline.resizeHint")}`}
                >
                  <div
                    aria-describedby={hintId}
                    aria-disabled={disabled}
                    aria-label={label}
                    aria-valuemax={Date.parse(bounds.max)}
                    aria-valuemin={Date.parse(bounds.min)}
                    aria-valuenow={Date.parse(at)}
                    aria-valuetext={formatDate(at)}
                    className={`absolute top-1/2 z-10 h-6 w-3 touch-none rounded border border-default-400 bg-content1 shadow-sm outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary ${disabled ? "cursor-wait" : "cursor-ew-resize"}`}
                    role="slider"
                    style={{
                      left: `${left}%`,
                      transform: edge === "start" ? "translate(-100%, -50%)" : "translate(0, -50%)",
                    }}
                    tabIndex={0}
                    onKeyDown={(event) => resizeWithKeyboard(event, index, edge)}
                    onPointerDown={(event) => startDrag(event, index, edge)}
                  />
                </Tooltip>
              );
            }),
          )}
      </div>
      <div className="flex justify-between gap-2 text-xs text-default-500">
        <time dateTime={activeDomain.startAt ?? new Date(activeDomain.start).toISOString()}>
          {formatDate(activeDomain.startAt ?? activeDomain.start)}
        </time>
        <time className="text-right" dateTime={new Date(activeDomain.end).toISOString()}>
          {t<string>("collectionMemo.timeline.now", { date: formatDate(activeDomain.end) })}
        </time>
      </div>
      {onResizeCoverage && (
        <p className="text-xs text-default-500" id={hintId}>
          {t<string>("collectionMemo.timeline.resizeHint")}
        </p>
      )}
      {error && (
        <div className="flex items-center gap-2 text-sm text-danger" role="alert">
          <span>{errorText}</span>
          {canRetry && retryAction && (
            <Button
              isDisabled={disabled}
              size="sm"
              variant="light"
              onPress={() => {
                void runMutation(retryAction);
              }}
            >
              {t<string>("collectionMemo.timeline.retry")}
            </Button>
          )}
        </div>
      )}
    </div>
  );
};

export default Timeline;
