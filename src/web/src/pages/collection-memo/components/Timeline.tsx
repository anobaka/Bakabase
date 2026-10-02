import type { PointerEvent as ReactPointerEvent, KeyboardEvent as ReactKeyboardEvent } from "react";
import type {
  CollectionMemoCoverageResize,
  CollectionMemoTarget,
  TimelineCoverage,
  TimelineDomain,
} from "../helpers";
import type { TimelineHoverSource } from "./TimelineHoverCard";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  clampCoverageBoundary,
  getCoverageResizeBounds,
  getTimelineCoverage,
  getTimelineRegions,
  getTimestampTicks,
} from "../helpers";

import { useTimelineHover } from "./TimelineHoverCard";

import { Button } from "@/components/bakaui";

interface Props {
  target: CollectionMemoTarget;
  domain: TimelineDomain;
  formatDate: (value: number | string) => string;
  reverse?: boolean;
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
  reverse: boolean;
  globalStartAt: string;
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

const resizeValue = (
  component: TimelineCoverage,
  edge: "start" | "end",
  at: string,
  globalStartAt: string,
): CollectionMemoCoverageResize => ({
  ranges: component.ranges,
  edge,
  at,
  ...(component.ranges.some((range) => range.startAt === null)
    ? { expectedGlobalStartAt: globalStartAt }
    : {}),
});

const Timeline = ({
  target,
  domain,
  formatDate,
  reverse = true,
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
  const globalStartAt = domain.startAt ?? new Date(domain.start).toISOString();
  const signature = JSON.stringify([signatureOf(target.ranges), globalStartAt, reverse]);
  const currentSignature = useRef(signature);

  currentSignature.current = signature;
  const disabled = pending || isSaving;
  const activeDomain = preview?.gesture.domain ?? domain;
  const activeReverse = preview?.gesture.reverse ?? reverse;
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
    : getTimelineCoverage(target.ranges, globalStartAt);
  const regions = getTimelineRegions(coverage, activeDomain, activeReverse);
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
    hover.close();
    const originalCoverage = getTimelineCoverage(target.ranges, globalStartAt);
    const component = originalCoverage[index];
    const originalAt = edge === "start" ? component.startAt : component.endAt;
    const gesture: Gesture = {
      pointerId: event.pointerId,
      originX: event.clientX,
      width: rect.width,
      domain: { ...domain },
      reverse,
      globalStartAt,
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
              (gesture.reverse ? -1 : 1) *
                (offset / gesture.width) *
                (gesture.domain.end - gesture.domain.start)) /
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
      value: resizeValue(
        gesture.coverage[gesture.index],
        gesture.edge,
        gesture.at,
        gesture.globalStartAt,
      ),
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
    const backward =
      event.key === (reverse ? "ArrowRight" : "ArrowLeft") || event.key === "ArrowDown";
    const forward = event.key === (reverse ? "ArrowLeft" : "ArrowRight") || event.key === "ArrowUp";

    if (!backward && !forward && event.key !== "Home" && event.key !== "End") return;
    event.preventDefault();
    const originalCoverage = getTimelineCoverage(target.ranges, globalStartAt);
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
    void runMutation({ kind: "resize", value: resizeValue(component, edge, at, globalStartAt) });
  };

  const retryAction = error?.action;
  const canRetry =
    retryAction?.kind === "resize"
      ? (retryAction.value.expectedGlobalStartAt === undefined ||
          retryAction.value.expectedGlobalStartAt === globalStartAt) &&
        getTimelineCoverage(target.ranges, globalStartAt).some(
          (component) => signatureOf(component.ranges) === signatureOf(retryAction.value.ranges),
        )
      : retryAction?.kind === "fill" &&
        getTimelineRegions(getTimelineCoverage(target.ranges, globalStartAt), domain).some(
          (region) =>
            !region.collected &&
            region.startAt === retryAction.value.startAt &&
            region.endAt === retryAction.value.endAt,
        );

  const componentId = (component: TimelineCoverage) =>
    component.ranges
      .map((range) => range.id)
      .sort((a, b) => a - b)
      .join(",");
  const regionLabel = (startAt: string, endAt: string, collected: boolean) =>
    t<string>(
      collected && getTimestampTicks(startAt) === getTimestampTicks(endAt)
        ? "collectionMemo.timeline.point"
        : collected
          ? "collectionMemo.timeline.collectedRange"
          : "collectionMemo.timeline.uncollectedRange",
      { start: formatDate(startAt), end: formatDate(endAt) },
    );
  const visibleCoverage = coverage.filter(
    (component) =>
      getTimestampTicks(component.endAt)! >=
        getTimestampTicks(activeDomain.startAt ?? new Date(activeDomain.start).toISOString())! &&
      getTimestampTicks(component.startAt)! <=
        getTimestampTicks(new Date(activeDomain.end).toISOString())!,
  );
  let regionCursor = 0;
  const regionSources = regions.map((region) => {
    const component = region.collected ? visibleCoverage[regionCursor++] : undefined;
    const previous = visibleCoverage[regionCursor - 1];
    const next = visibleCoverage[regionCursor];
    const id = component
      ? `collected:${componentId(component)}`
      : visibleCoverage.length === 0
        ? "gap:empty"
        : `gap:${previous ? componentId(previous) : "leading"}:${next ? componentId(next) : "trailing"}`;

    return { id, region };
  });
  const sources = new Map<string, TimelineHoverSource>();

  for (const { id, region } of regionSources) {
    sources.set(id, {
      id,
      label: regionLabel(region.startAt, region.endAt, region.collected),
      error:
        error?.action?.kind === "fill" &&
        error.action.value.startAt === region.startAt &&
        error.action.value.endAt === region.endAt
          ? errorText
          : undefined,
      onFill:
        !region.collected && onFillGap
          ? () =>
              runMutation({ kind: "fill", value: { startAt: region.startAt, endAt: region.endAt } })
          : undefined,
    });
  }
  const boundarySources = coverage.flatMap((component, index) =>
    (["start", "end"] as const).flatMap((edge) => {
      const at = edge === "start" ? component.startAt : component.endAt;
      const ticks = getTimestampTicks(at)!;
      const domainStartTicks = getTimestampTicks(
        activeDomain.startAt ?? new Date(activeDomain.start).toISOString(),
      )!;
      const domainEndTicks = getTimestampTicks(new Date(activeDomain.end).toISOString())!;

      // A clipped endpoint is not the stored endpoint and must not rewrite it through dragging.
      if (ticks < domainStartTicks || ticks > domainEndTicks) return [];

      const bounds = getCoverageResizeBounds(coverage, index, activeDomain, edge);
      const chronologicalLeft =
        activeDomain.end > activeDomain.start
          ? ((Date.parse(at) - activeDomain.start) / (activeDomain.end - activeDomain.start)) * 100
          : 100;
      const left = activeReverse ? 100 - chronologicalLeft : chronologicalLeft;
      const width =
        activeDomain.end > activeDomain.start
          ? ((Math.min(component.end, activeDomain.end) -
              Math.max(component.start, activeDomain.start)) /
              (activeDomain.end - activeDomain.start)) *
            100
          : 0;
      const id = `edge:${componentId(component)}:${edge}`;

      sources.set(id, {
        id,
        label: t<string>(
          edge === "start"
            ? "collectionMemo.timeline.resizeStart"
            : "collectionMemo.timeline.resizeEnd",
          { date: formatDate(at) },
        ),
        description: `${regionLabel(component.startAt, component.endAt, true)} · ${t<string>("collectionMemo.timeline.resizeHint")}`,
      });

      return [{ id, component, index, edge, at, bounds, left, width }];
    }),
  );
  const hover = useTimelineHover({ sources, track, disabled, suppressed: !!drag.current });

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
        className="group/timeline relative my-2 h-4"
        onLostPointerCapture={(event) => {
          if (drag.current?.pointerId === event.pointerId) cancel();
        }}
        onPointerCancel={(event) => {
          if (drag.current?.pointerId === event.pointerId) cancel();
        }}
        onPointerMove={moveDrag}
        onPointerUp={finishDrag}
      >
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 overflow-hidden rounded-full bg-default-200"
        >
          {regionSources
            .filter(({ region }) => region.collected)
            .map(({ id, region }) => (
              <span
                key={id}
                className="absolute top-0 h-full bg-success"
                style={{
                  left: `${region.left}%`,
                  width: region.point || region.width === 0 ? "4px" : `${region.width}%`,
                  transform: region.point ? "translateX(-50%)" : undefined,
                }}
              />
            ))}
        </div>
        {regionSources.map(({ id, region }) => (
          <button
            key={id}
            aria-describedby={hover.activeId === id ? hover.popupId : undefined}
            aria-expanded={region.collected ? undefined : hover.activeId === id}
            aria-haspopup={region.collected ? undefined : "dialog"}
            aria-label={sources.get(id)!.label}
            className="absolute top-0 h-full border-0 bg-transparent p-0 outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary"
            style={{
              left: `${region.left}%`,
              width: region.point || region.width === 0 ? "4px" : `${region.width}%`,
              transform: region.point ? "translateX(-50%)" : undefined,
              // Browser hit testing rounds subpixel boxes. Keep even a very short
              // gap above adjacent inward-facing resize targets.
              zIndex: region.collected ? 1 : 20,
            }}
            type="button"
            onBlur={(event) => hover.blur(event.relatedTarget)}
            onClick={(event) => hover.show(id, event.currentTarget)}
            onFocus={(event) => hover.show(id, event.currentTarget)}
            onKeyDown={(event) => {
              if (
                !region.collected &&
                (event.key === "Enter" || event.key === " " || event.key === "ArrowDown")
              ) {
                event.preventDefault();
                hover.show(id, event.currentTarget, true);
              }
            }}
            onPointerEnter={(event) => hover.show(id, event.currentTarget)}
            onPointerLeave={(event) => hover.leave(event.relatedTarget)}
          />
        ))}
        {onResizeCoverage &&
          boundarySources.map(({ id, index, edge, at, bounds, left, width }) => {
            const point = width <= 0;
            const facesRight = (point ? edge === "end" : edge === "start") !== activeReverse;
            const active =
              hover.activeId === id ||
              (preview?.gesture.index === index && preview.gesture.edge === edge);

            return (
              <div
                key={id}
                aria-describedby={`${hintId}${hover.activeId === id ? ` ${hover.popupId}` : ""}`}
                aria-disabled={disabled}
                aria-label={sources.get(id)!.label}
                aria-valuemax={Date.parse(bounds.max)}
                aria-valuemin={Date.parse(bounds.min)}
                aria-valuenow={Date.parse(at)}
                aria-valuetext={formatDate(at)}
                className={`group/edge absolute top-1/2 z-10 h-10 max-w-5 touch-none bg-transparent outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary ${disabled ? "cursor-wait" : "cursor-ew-resize"}`}
                role="slider"
                style={{
                  left: `${left}%`,
                  width: point ? "20px" : `${width / 2}%`,
                  zIndex: point ? 30 : undefined,
                  transform: facesRight ? "translate(0, -50%)" : "translate(-100%, -50%)",
                }}
                tabIndex={0}
                onBlur={(event) => hover.blur(event.relatedTarget)}
                onFocus={(event) => hover.show(id, event.currentTarget)}
                onKeyDown={(event) => resizeWithKeyboard(event, index, edge)}
                onPointerDown={(event) => startDrag(event, index, edge)}
                onPointerEnter={(event) => hover.show(id, event.currentTarget)}
                onPointerLeave={(event) => hover.leave(event.relatedTarget)}
              >
                <span
                  aria-hidden
                  className="pointer-events-none absolute top-1/2 h-5 w-px -translate-y-1/2 bg-foreground/70 opacity-0 transition-opacity group-hover/timeline:opacity-30 group-hover/edge:opacity-100 group-focus-visible/edge:opacity-100"
                  style={{
                    [facesRight ? "left" : "right"]: 0,
                    opacity: active ? 1 : undefined,
                    width: active ? 2 : undefined,
                  }}
                />
              </div>
            );
          })}
      </div>
      {hover.popup}
      <div
        className={`flex justify-between gap-2 text-xs text-default-500 ${activeReverse ? "flex-row-reverse" : ""}`}
      >
        <time
          className={activeReverse ? "text-right" : undefined}
          dateTime={activeDomain.startAt ?? new Date(activeDomain.start).toISOString()}
        >
          {formatDate(activeDomain.startAt ?? activeDomain.start)}
        </time>
        <time
          className={activeReverse ? undefined : "text-right"}
          dateTime={new Date(activeDomain.end).toISOString()}
        >
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
