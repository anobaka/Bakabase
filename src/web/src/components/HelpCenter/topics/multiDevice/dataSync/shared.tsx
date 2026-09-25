"use client";

import type { ReactNode } from "react";
import type { DeviceId } from "../devices";

import { useTranslation } from "react-i18next";

import { HOME_DEVICE, deviceNameKey, deviceStyle } from "../devices";

/**
 * The pieces every data sync picture is drawn from. An arrow points to the device that
 * **receives** the definitions, as on the device map, so a reader who knows one reads the
 * other. Its colour is the pictures' own rule, not the map's (which colours a line by its
 * kind): the colour of the device the definitions come from (`devices.ts`), or grey when the
 * picture is about something else. Everything is drawn with theme colour classes, so it
 * follows light and dark mode.
 */

export const dsk = (key: string) => `helpCenter.dataSync.${key}`;

/** The device the reader sits at, and the other device it syncs with, in every picture. */
export const HERE: DeviceId = HOME_DEVICE;
export const THERE: DeviceId = "laptop";

/** A device drawn in its colour, 32×28 — the same drawings as the topic's network picture. */
export const DeviceShape = ({ id }: { id: DeviceId }) => {
  const style = deviceStyle(id);
  const common = {
    className: `${style.stroke} fill-none`,
    strokeWidth: 2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };

  return (
    <g aria-hidden>
      {id === "desktop" && (
        <>
          <rect {...common} height={19} rx={2} width={30} x={1} y={1} />
          <path {...common} d="M16 20 V25 M9 26 H23" />
        </>
      )}
      {id === "laptop" && (
        <>
          <rect {...common} height={16} rx={2} width={22} x={5} y={3} />
          <path {...common} d="M1 23 H31" />
        </>
      )}
      {id === "nas" && (
        <>
          <rect {...common} height={26} rx={3} width={22} x={5} y={1} />
          <path {...common} d="M9 9 H23 M9 15 H23 M9 21 H23" />
          <circle className={style.solid} cx={23} cy={5} r={1.3} />
        </>
      )}
    </g>
  );
};

export const DeviceMark = ({ id, className = "h-6 w-7" }: { id: DeviceId; className?: string }) => (
  <svg aria-hidden className={`shrink-0 ${className}`} viewBox="0 0 32 28">
    <DeviceShape id={id} />
  </svg>
);

/**
 * How one direction of a link looks: `active` definitions flow, `once` they are copied a
 * single time and the lane then stays off (copy once), `pending` waits (for an approval),
 * `idle` is set up but not what the picture is about, `none` is not drawn.
 */
export type LaneState = "active" | "once" | "pending" | "idle" | "none";

/**
 * One arrow from `from` to `to`, drawn inside an SVG. The head sits at (x2, y2), the end
 * the definitions arrive at; a `once` arrow also has a bar across its tail, where the lane
 * stops after the copy. The data attributes say who sends and who receives; the help test
 * checks them against the colour and the geometry actually drawn.
 */
export const SyncArrow = ({
  from,
  to,
  x1,
  y1,
  x2,
  y2,
  state,
}: {
  from: DeviceId;
  to: DeviceId;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  state: Exclude<LaneState, "none">;
}) => {
  const style = deviceStyle(from);
  const stroke = state === "idle" ? "stroke-default-300" : style.stroke;
  const fill = state === "idle" ? "fill-default-300" : style.solid;
  const angle = Math.atan2(y2 - y1, x2 - x1);
  const head = 6;
  const baseX = x2 - Math.cos(angle) * head;
  const baseY = y2 - Math.sin(angle) * head;
  const spreadX = Math.sin(angle) * head * 0.6;
  const spreadY = -Math.cos(angle) * head * 0.6;
  // Half the tail bar of a `once` arrow, across the line.
  const barX = Math.sin(angle) * 5;
  const barY = -Math.cos(angle) * 5;

  return (
    <g data-arrow data-from={from} data-state={state} data-to={to}>
      <path
        className={stroke}
        d={`M${x1} ${y1} L${baseX} ${baseY}`}
        fill="none"
        strokeDasharray={state === "pending" ? "4 3" : undefined}
        strokeLinecap="round"
        strokeWidth={2}
      />
      <path
        className={fill}
        d={`M${x2} ${y2} L${baseX + spreadX} ${baseY + spreadY} L${baseX - spreadX} ${
          baseY - spreadY
        } Z`}
      />
      {state === "once" && (
        <path
          data-bar
          className={stroke}
          d={`M${x1 + barX} ${y1 + barY} L${x1 - barX} ${y1 - barY}`}
          fill="none"
          strokeLinecap="round"
          strokeWidth={2}
        />
      )}
    </g>
  );
};

/**
 * The two directions between this device (left) and the other one (right): the upper lane
 * brings the other device's definitions here, the lower one takes this device's there.
 */
export const SyncLanes = ({
  toHere,
  toThere,
  mark,
}: {
  toHere: LaneState;
  toThere: LaneState;
  /**
   * A short sign, such as "1×" for a copy made once, shown where the lower lane would be:
   * only for a pair that draws no lower lane.
   */
  mark?: string;
}) => (
  // Takes its share of the pair's width: in a very narrow dialog the drawing scales down.
  // Its upper lane lines up with the middle of the devices' drawings above their names.
  <div className="relative mt-0.5 min-w-0 flex-1">
    <svg aria-hidden className="block h-8 w-full" viewBox="0 0 64 32">
      {toHere !== "none" && (
        <SyncArrow from={THERE} state={toHere} to={HERE} x1={60} x2={4} y1={10} y2={10} />
      )}
      {toThere !== "none" && (
        <SyncArrow from={HERE} state={toThere} to={THERE} x1={4} x2={60} y1={21} y2={21} />
      )}
    </svg>
    {/* Text, not part of the drawing, so it keeps a readable size when the drawing scales. */}
    {mark && (
      <span
        data-mark
        className="absolute inset-x-0 bottom-0 text-center text-xs font-semibold leading-none text-default-500"
      >
        {mark}
      </span>
    )}
  </div>
);

/**
 * A device with its name under it and, optionally, a small sign at its corner. In a very
 * narrow dialog the name wraps onto a second line rather than being cut.
 */
export const DeviceBlock = ({ id, badge }: { id: DeviceId; badge?: ReactNode }) => {
  const { t } = useTranslation();

  return (
    <div className="flex min-w-0 flex-1 flex-col items-center gap-0.5" data-device={id}>
      <span className="relative">
        <DeviceMark id={id} />
        {badge && <span className="absolute -right-2.5 -top-1.5">{badge}</span>}
      </span>
      <span className="line-clamp-2 w-full break-words text-center text-[10px] leading-tight text-default-500">
        {t(deviceNameKey(id))}
      </span>
    </div>
  );
};

/**
 * This device and the other one, with the lanes between them: at most 13rem wide, and
 * narrower when its container is, so it never makes a narrow help dialog scroll sideways.
 * Aligned at the top, so the two drawings stay level when only one name wraps.
 */
export const DevicePair = ({
  toHere,
  toThere,
  mark,
  badgeHere,
  badgeThere,
}: {
  toHere: LaneState;
  toThere: LaneState;
  mark?: string;
  badgeHere?: ReactNode;
  badgeThere?: ReactNode;
}) => (
  <div aria-hidden className="flex w-full max-w-52 shrink-0 items-start gap-1 self-center">
    <DeviceBlock badge={badgeHere} id={HERE} />
    <SyncLanes mark={mark} toHere={toHere} toThere={toThere} />
    <DeviceBlock badge={badgeThere} id={THERE} />
  </div>
);

/** A round sign at a device's corner. */
export const CornerBadge = ({
  tone,
  children,
}: {
  tone: "neutral" | "attention";
  children: ReactNode;
}) => (
  <span
    className={`flex h-4 w-4 items-center justify-center rounded-full border text-[10px] font-semibold ${
      tone === "attention"
        ? "border-warning bg-warning text-warning-foreground"
        : "border-default-300 bg-content1 text-default-700"
    }`}
    data-badge={tone}
  >
    {children}
  </span>
);

/** A single arrow for HTML layouts, pointing the way the definitions go. */
export const LaneArrow = ({
  from,
  to,
  direction,
  className = "",
}: {
  from: DeviceId;
  to: DeviceId;
  direction: "left" | "right" | "up" | "down";
  className?: string;
}) => {
  const horizontal = direction === "left" || direction === "right";
  const [x1, y1, x2, y2] = {
    left: [30, 6, 2, 6],
    right: [2, 6, 30, 6],
    up: [6, 30, 6, 2],
    down: [6, 2, 6, 30],
  }[direction];

  return (
    <svg
      aria-hidden
      className={`shrink-0 ${horizontal ? "h-3 w-8" : "h-8 w-3"} ${className}`}
      viewBox={horizontal ? "0 0 32 12" : "0 0 12 32"}
    >
      <SyncArrow from={from} state="active" to={to} x1={x1} x2={x2} y1={y1} y2={y2} />
    </svg>
  );
};
