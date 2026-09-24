"use client";

import type { KeyboardEvent } from "react";
import type { DeviceId } from "./devices";

import { useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { HOME_DEVICE, deviceNameKey, deviceStyle, devices, mdk } from "./devices";

/**
 * The picture the whole topic hangs on: several Bakabase servers on one network, each
 * on its own device with its own storage, each able to reach the others.
 *
 * Interactive on purpose. "Every device can use every other device" is easy to say and
 * hard to believe from a static mesh, so the reader picks a device and its connections
 * light up. A computer reaches out — it is where someone sits; the NAS has no screen, so
 * picking it shows the computers reaching in. Drawn in SVG with theme colour classes, so
 * it follows light and dark mode, and scales with the dialog.
 */

// Compact on purpose: the dialog is often narrow, and a wide viewBox would shrink the
// labels to nothing. The SVG is capped in width instead, so it does not balloon either.
const WIDTH = 600;
const HEIGHT = 340;
const CENTER_X = WIDTH / 2;
const CARD_W = 196;
const CARD_H = 60;
const CYLINDER_RX = 52;
const CYLINDER_RY = 9;
const CYLINDER_BODY = 34;
const LINK_GAP = 6;

const layout: Record<DeviceId, { cx: number; cy: number; storageCy: number }> = {
  desktop: { cx: 110, cy: 52, storageCy: 150 },
  laptop: { cx: 490, cy: 52, storageCy: 150 },
  nas: { cx: CENTER_X, cy: 222, storageCy: 306 },
};
const LAN_Y = 112;

const links: [DeviceId, DeviceId][] = [
  ["desktop", "laptop"],
  ["desktop", "nas"],
  ["laptop", "nas"],
];

/** Where the line from one card's centre towards another leaves the first card. */
const edgePoint = (from: DeviceId, to: DeviceId) => {
  const a = layout[from];
  const b = layout[to];
  const dx = b.cx - a.cx;
  const dy = b.cy - a.cy;
  const halfW = CARD_W / 2 + LINK_GAP;
  const halfH = CARD_H / 2 + LINK_GAP;
  const t = Math.min(
    dx === 0 ? Infinity : halfW / Math.abs(dx),
    dy === 0 ? Infinity : halfH / Math.abs(dy),
  );

  return { x: a.cx + dx * t, y: a.cy + dy * t };
};

/**
 * How one link reads while `selected` is picked. A computer's links point away from it;
 * the NAS's links point at it, because nobody sits at a NAS — the computers reach it.
 */
export const linkState = (selected: DeviceId, [a, b]: [DeviceId, DeviceId]) => {
  const active = a === selected || b === selected;

  if (!active) return { active, from: a, to: b };
  const other = a === selected ? b : a;

  return deviceStyle(selected).hasWindow
    ? { active, from: selected, to: other }
    : { active, from: other, to: selected };
};

const prefersReducedMotion = () =>
  typeof window !== "undefined" &&
  typeof window.matchMedia === "function" &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** A small line drawing of the device, 32×28, in the device's colour. */
const DeviceGlyph = ({ id, x, y }: { id: DeviceId; x: number; y: number }) => {
  const style = deviceStyle(id);
  const common = {
    className: `${style.stroke} fill-none`,
    strokeWidth: 2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };

  return (
    <g aria-hidden transform={`translate(${x} ${y})`}>
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

/** A storage cylinder with its label inside. */
const Storage = ({
  id,
  cx,
  cy,
  label,
}: {
  id: DeviceId;
  cx: number;
  cy: number;
  label: string;
}) => {
  const style = deviceStyle(id);
  const top = cy - CYLINDER_BODY / 2;
  const bottom = cy + CYLINDER_BODY / 2;

  return (
    <g aria-hidden>
      <path
        className={`${style.fill} ${style.stroke}`}
        d={`M${cx - CYLINDER_RX} ${top} V${bottom} A${CYLINDER_RX} ${CYLINDER_RY} 0 0 0 ${cx + CYLINDER_RX} ${bottom} V${top}`}
        strokeWidth={1.5}
      />
      <ellipse
        className={`fill-content1 ${style.stroke}`}
        cx={cx}
        cy={top}
        rx={CYLINDER_RX}
        ry={CYLINDER_RY}
        strokeWidth={1.5}
      />
      <text
        className="fill-default-600"
        dominantBaseline="middle"
        fontSize={12}
        textAnchor="middle"
        x={cx}
        y={cy + 5}
      >
        {label}
      </text>
    </g>
  );
};

const NetworkDiagram = () => {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<DeviceId>(HOME_DEVICE);
  const animate = useMemo(() => !prefersReducedMotion(), []);
  // Marker ids are document-global; two diagrams on one page must not share them.
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const markerId = (name: string) => `md-${uid}-${name}`;
  const titleId = `md-${uid}-title`;
  const captionId = `md-${uid}-caption`;

  const onKey = (id: DeviceId) => (event: KeyboardEvent<SVGGElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelected(id);
    }
  };

  const selectedStyle = deviceStyle(selected);

  return (
    <figure className="flex flex-col gap-2" data-testid="multi-device-network">
      <div className="rounded-xl border border-default-200 bg-default-50 p-2">
        <svg
          aria-describedby={captionId}
          aria-labelledby={titleId}
          className="mx-auto block h-auto w-full max-w-[720px] select-none"
          role="group"
          viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        >
          <title id={titleId}>{t(mdk("diagram.title"))}</title>
          <defs>
            {devices.map((device) => (
              <marker
                key={device.id}
                id={markerId(device.id)}
                markerHeight={8}
                markerWidth={8}
                orient="auto-start-reverse"
                refX={8}
                refY={4}
                viewBox="0 0 8 8"
              >
                <path className={device.solid} d="M0 0 L8 4 L0 8 z" />
              </marker>
            ))}
            <marker
              id={markerId("muted")}
              markerHeight={7}
              markerWidth={7}
              orient="auto-start-reverse"
              refX={8}
              refY={4}
              viewBox="0 0 8 8"
            >
              <path className="fill-default-300" d="M0 0 L8 4 L0 8 z" />
            </marker>
          </defs>

          {/* The network itself: every pair of devices can reach each other. */}
          {links.map((link) => {
            const { active, from, to } = linkState(selected, link);
            const start = edgePoint(from, to);
            const end = edgePoint(to, from);

            return (
              <line
                key={link.join("-")}
                className={active ? selectedStyle.stroke : "stroke-default-300"}
                data-active={active}
                data-from={from}
                data-link={link.join("-")}
                data-to={to}
                markerEnd={`url(#${markerId(active ? selected : "muted")})`}
                markerStart={active ? undefined : `url(#${markerId("muted")})`}
                strokeDasharray={active ? "7 6" : "4 6"}
                strokeLinecap="round"
                strokeWidth={active ? 2.5 : 1.5}
                x1={start.x}
                x2={end.x}
                y1={start.y}
                y2={end.y}
              >
                {active && animate && (
                  <animate
                    attributeName="stroke-dashoffset"
                    dur="1.2s"
                    from="26"
                    repeatCount="indefinite"
                    to="0"
                  />
                )}
              </line>
            );
          })}

          {/* The network, named once in the middle rather than on every line. */}
          <g aria-hidden>
            <rect
              className="fill-content1 stroke-default-300"
              height={26}
              rx={13}
              strokeWidth={1}
              width={120}
              x={CENTER_X - 60}
              y={LAN_Y - 13}
            />
            <text
              className="fill-default-500"
              dominantBaseline="middle"
              fontSize={12}
              textAnchor="middle"
              x={CENTER_X}
              y={LAN_Y + 1}
            >
              {t(mdk("diagram.lan"))}
            </text>
          </g>

          {devices.map((device) => {
            const { cx, cy, storageCy } = layout[device.id];
            const isSelected = device.id === selected;
            const x = cx - CARD_W / 2;
            const y = cy - CARD_H / 2;
            const name = t(deviceNameKey(device.id));

            return (
              <g key={device.id}>
                {/* Each server has its own storage, attached to it alone. */}
                <line
                  aria-hidden
                  className={device.stroke}
                  strokeWidth={1.5}
                  x1={cx}
                  x2={cx}
                  y1={cy + CARD_H / 2}
                  y2={storageCy - CYLINDER_BODY / 2 - CYLINDER_RY}
                />
                <Storage
                  cx={cx}
                  cy={storageCy}
                  id={device.id}
                  label={t(mdk(`diagram.storage.${device.id}`))}
                />

                <g
                  aria-label={t(mdk("diagram.select"), { name })}
                  aria-pressed={isSelected}
                  className="group cursor-pointer outline-none"
                  data-device={device.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => setSelected(device.id)}
                  onKeyDown={onKey(device.id)}
                >
                  {/* Keyboard focus ring: the card's own border is too faint to carry it. */}
                  <rect
                    className="fill-none stroke-focus opacity-0 group-focus-visible:opacity-100"
                    height={CARD_H + 8}
                    rx={15}
                    strokeWidth={2}
                    width={CARD_W + 8}
                    x={x - 4}
                    y={y - 4}
                  />
                  <rect
                    className={
                      isSelected
                        ? `${device.fill} ${device.stroke}`
                        : "fill-content1 stroke-default-300 group-hover:stroke-default-500"
                    }
                    height={CARD_H}
                    rx={12}
                    strokeWidth={isSelected ? 2 : 1}
                    width={CARD_W}
                    x={x}
                    y={y}
                  />
                  <DeviceGlyph id={device.id} x={x + 12} y={y + 16} />
                  <text
                    className="fill-foreground"
                    fontSize={15}
                    fontWeight={600}
                    x={x + 52}
                    y={y + 27}
                  >
                    {name}
                  </text>
                  <text className="fill-default-500" fontSize={10.5} x={x + 52} y={y + 45}>
                    {t(mdk(`diagram.role.${device.id}`))}
                  </text>
                </g>

                {isSelected && (
                  <g aria-hidden>
                    <rect
                      className={device.solid}
                      height={18}
                      rx={9}
                      width={92}
                      x={x + CARD_W - 100}
                      y={y - 11}
                    />
                    <text
                      className={device.solidText}
                      dominantBaseline="middle"
                      fontSize={10.5}
                      fontWeight={600}
                      textAnchor="middle"
                      x={x + CARD_W - 54}
                      y={y - 1.5}
                    >
                      {t(mdk(device.hasWindow ? "diagram.badge.here" : "diagram.badge.headless"))}
                    </text>
                  </g>
                )}
              </g>
            );
          })}
        </svg>
      </div>

      <figcaption className="flex flex-col gap-1.5">
        <p aria-live="polite" className="text-sm text-default-700" id={captionId}>
          <span className={`font-medium ${selectedStyle.text}`}>{t(deviceNameKey(selected))}</span>
          {" · "}
          {t(mdk(`diagram.caption.${selected}`))}
        </p>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-default-500">
          <span className="flex items-center gap-1.5">
            <span
              aria-hidden
              className="inline-block h-3 w-4 rounded-sm border border-default-400"
            />
            {t(mdk("diagram.legend.server"))}
          </span>
          <span className="flex items-center gap-1.5">
            <svg aria-hidden className="h-3.5 w-4" viewBox="0 0 16 14">
              <path
                className="fill-default-200 stroke-default-400"
                d="M1 3 V11 A7 2.5 0 0 0 15 11 V3"
                strokeWidth={1}
              />
              <ellipse
                className="fill-content1 stroke-default-400"
                cx={8}
                cy={3}
                rx={7}
                ry={2.5}
                strokeWidth={1}
              />
            </svg>
            {t(mdk("diagram.legend.storage"))}
          </span>
          <span className="flex items-center gap-1.5">
            <svg aria-hidden className="h-3 w-6" viewBox="0 0 24 12">
              <path
                className="stroke-default-500"
                d="M1 6 H18"
                strokeDasharray="4 3"
                strokeWidth={1.5}
              />
              <path className="fill-default-500" d="M16 2 L23 6 L16 10 z" />
            </svg>
            {t(mdk("diagram.legend.access"))}
          </span>
          <span className="text-default-400">{t(mdk("diagram.hint"))}</span>
        </div>
      </figcaption>
    </figure>
  );
};

NetworkDiagram.displayName = "NetworkDiagram";

export default NetworkDiagram;
