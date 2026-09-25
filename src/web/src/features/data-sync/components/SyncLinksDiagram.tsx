import type { TFunction } from "i18next";
import type { KeyboardEvent } from "react";
import type { DataSyncKindCount } from "../api";
import type { Box, Point } from "./diagramLayout";
import type { LaneStatus, SyncPeer } from "../viewModels";

import { useEffect, useId, useLayoutEffect, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import { useElementWidth } from "../hooks/useElementWidth";
import {
  isOffline,
  lineMode,
  linkStatus,
  orderKinds,
  peerCardLine,
  readLane,
  receiveLane,
  syncIssueOf,
} from "../viewModels";

import { CountBubble, StatusDot, toneFill, toneText } from "./common";
import { layoutSyncDiagram, PEER_W, spokeGeometry } from "./diagramLayout";

import {
  ATTENTION_DASH,
  AttentionMark,
  edgeStyles,
  KindBadgeGlyph,
  KindGlyph,
} from "@/features/federation/map/DeviceMapCanvas";
import { attributeValue } from "@/features/federation/map/mapFocus";
import { fitEnd, fitNames, SEMIBOLD } from "@/features/federation/map/text";

/*
 * The page's picture: this device in the middle and every device it syncs with around it,
 * each on a spoke drawn the way the device map draws data sync — the arrow points to the
 * device that receives the definitions. Every card and every spoke is a focusable control,
 * which opens that device's details. Too narrow, or too many devices, for a readable drawing,
 * and the devices are listed instead, each with a small drawing of its two directions.
 */

/** How a control of the diagram was used: from the keyboard, the details take focus. */
export type DiagramSelectVia = "pointer" | "keyboard";

/** What a control stands for: a device's card, or its spoke. */
export type DiagramPart = "card" | "spoke";

/**
 * Where the keyboard should be: on this device's card or spoke. A new `seq` asks for it now —
 * the details closed and give the keyboard back to what opened them.
 */
export interface DiagramFocusRequest {
  nodeId: string;
  part: DiagramPart;
  seq: number;
}

export interface SyncLinksDiagramProps {
  peers: SyncPeer[];
  self: {
    name: string;
    kinds: DataSyncKindCount[];
    sharingEnabled: boolean;
    headless: boolean;
  };
  selectedId?: string;
  onSelect: (nodeId: string, via: DiagramSelectVia, part: DiagramPart) => void;
  /** "Sync with another device"; no such control where it is not offered. */
  onAdd?: () => void;
  /** The width to lay out for before the diagram can be measured (tests, first paint). */
  initialWidth?: number;
  focusRequest?: DiagramFocusRequest;
  now?: number;
}

const PENDING_DASH = "6 5";

const onActivate = (run: () => void) => (event: KeyboardEvent<Element>) => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    run();
  }
};

/** A device's kind as the drawing can tell it: a headless server says so of itself. */
const kindOf = (peer: SyncPeer) =>
  peer.attention === undefined ? "unknown" : peer.attention.headless ? "server" : "desktop";

/** What a spoke says, for a screen reader: both directions, the mode and the status. */
export const spokeLabel = (t: TFunction, peer: SyncPeer, now?: number) => {
  const receive = receiveLane(peer);
  const read = readLane(peer);
  const mode = lineMode(peer);
  const parts = [
    receive === "none"
      ? t("dataSync.arrow.receive.off", { name: peer.name })
      : t(`federation.map.direction.sync.in.${receive}`, { name: peer.name }),
    read === "active"
      ? t("federation.map.direction.sync.out.active", { name: peer.name })
      : t("dataSync.arrow.read.off", { name: peer.name }),
    mode ? t(`federation.map.sync.mode.${mode}`) : undefined,
    linkStatus(t, peer, now).text,
  ];
  const issue = syncIssueOf(peer);

  if (issue)
    parts.push(
      t("federation.map.attention.sync.in", {
        name: peer.name,
        issue: t(`federation.map.issue.${issue}`),
      }),
    );

  return parts.filter(Boolean).join(". ");
};

/** What a card says, for a screen reader: its name, its status and what waits for a decision. */
const cardLabel = (t: TFunction, peer: SyncPeer, now?: number) => {
  const status = linkStatus(t, peer, now);

  return [
    peer.name,
    status.text,
    // Said once: the status already says it when that is what it is about.
    peer.openItems > 0 && status.code !== "NeedsYou"
      ? t("dataSync.diagram.openItems", { count: peer.openItems })
      : undefined,
    peer.attention?.openDecisions
      ? t("dataSync.status.NeedsYouThere", {
          name: peer.name,
          count: peer.attention.openDecisions,
        })
      : undefined,
  ]
    .filter(Boolean)
    .join(". ");
};

export default function SyncLinksDiagram({
  peers,
  self,
  selectedId,
  onSelect,
  onAdd,
  initialWidth = 960,
  focusRequest,
  now,
}: SyncLinksDiagramProps) {
  const { t } = useTranslation();
  const { ref, width } = useElementWidth<HTMLDivElement>(initialWidth);
  const layout = useMemo(
    () => layoutSyncDiagram(width, peers.length, !!onAdd),
    [width, peers.length, onAdd],
  );
  // What has the keyboard in the diagram, by what it stands for rather than by the element: the
  // drawing and the list replace each other as the width changes, and the keyboard stays on the
  // same device — its spoke, where the list's row stood for it.
  const held = useRef<{ nodeId: string; part: DiagramPart }>();
  const handledRequest = useRef<number>();

  useEffect(() => {
    const onFocusIn = (event: FocusEvent) => {
      const root = ref.current;
      const control =
        event.target instanceof Element ? event.target.closest("[data-sync-peer]") : null;

      if (!root || !control || !root.contains(control)) {
        // The reader went elsewhere: never pulled back.
        held.current = undefined;

        return;
      }
      const nodeId = control.getAttribute("data-sync-peer")!;
      const part = control.getAttribute("data-sync-part");

      held.current = {
        nodeId,
        part:
          part === "spoke"
            ? "spoke"
            : part === "row" && held.current?.nodeId === nodeId
              ? held.current.part
              : "card",
      };
    };

    // The reader points somewhere else: where the keyboard goes next is theirs to decide.
    const onPointerDown = (event: PointerEvent) => {
      if (!(event.target instanceof Node) || !ref.current?.contains(event.target))
        held.current = undefined;
    };

    document.addEventListener("focusin", onFocusIn, true);
    document.addEventListener("pointerdown", onPointerDown, true);

    return () => {
      document.removeEventListener("focusin", onFocusIn, true);
      document.removeEventListener("pointerdown", onPointerDown, true);
    };
  }, [ref]);

  useLayoutEffect(() => {
    const root = ref.current;
    const asked = !!focusRequest && focusRequest.seq !== handledRequest.current;

    if (asked) {
      handledRequest.current = focusRequest!.seq;
      held.current = { nodeId: focusRequest!.nodeId, part: focusRequest!.part };
    }
    const target = held.current;

    if (!root || !target) return;
    const active = document.activeElement;
    const lost = !active || active === document.body || !active.isConnected;

    if (!asked && !lost) return;
    const id = attributeValue(target.nodeId);
    const element =
      layout.mode === "list"
        ? root.querySelector<HTMLElement>(`[data-sync-peer="${id}"][data-sync-part="row"]`)
        : (root.querySelector<HTMLElement>(
            `[data-sync-peer="${id}"][data-sync-part="${target.part}"]`,
          ) ?? root.querySelector<HTMLElement>(`[data-sync-peer="${id}"]`));

    element?.focus();
  }, [focusRequest, layout.mode, ref]);

  const uid = useId().replace(/:/g, "");
  const markerId = `ds-${uid}-arrow`;
  const labels = useMemo(
    () =>
      fitNames(
        peers.map((peer) => ({
          id: peer.nodeId,
          name: peer.name,
          maxWidth: PEER_W - 58,
          fontSize: 13 * SEMIBOLD,
        })),
      ),
    [peers],
  );
  // What this device has, one type of definition at a time, in the page's order.
  const countLines = orderKinds(self.kinds.map((kind) => kind.kind))
    .map((kind) =>
      t(`dataSync.diagram.count.${kind}`, {
        count: self.kinds.find((item) => item.kind === kind)?.count ?? 0,
        defaultValue: "",
      }),
    )
    .filter(Boolean);
  const counts = countLines.join(" · ");

  return (
    <div
      ref={ref}
      className="relative w-full"
      data-mode={layout.mode}
      data-testid="data-sync-diagram"
    >
      {/* What the drawing shows, for a screen reader: every device, both ways. */}
      <ul className="sr-only" data-testid="data-sync-diagram-summary">
        <li>
          {t("dataSync.diagram.selfSummary", { name: self.name })}
          {counts ? `: ${counts}` : ""}
        </li>
        {peers.map((peer) => (
          <li key={peer.nodeId}>{spokeLabel(t, peer, now)}</li>
        ))}
      </ul>
      {layout.mode === "list" ? (
        <DiagramList
          now={now}
          peers={peers}
          selectedId={selectedId}
          self={self}
          selfLine={counts}
          onAdd={onAdd}
          onSelect={onSelect}
        />
      ) : (
        <svg
          aria-label={t("dataSync.diagram.label")}
          className="block w-full select-none"
          height={layout.height}
          role="group"
          viewBox={`0 0 ${layout.width} ${layout.height}`}
          width={layout.width}
        >
          <defs>
            <marker
              id={markerId}
              markerHeight={9}
              markerUnits="userSpaceOnUse"
              markerWidth={9}
              orient="auto-start-reverse"
              refX={9}
              refY={5}
              viewBox="0 0 10 10"
            >
              <path className={edgeStyles.sync.fill} d="M0 0.5 L10 5 L0 9.5 L2.5 5 z" />
            </marker>
          </defs>
          {peers.map((peer, index) => (
            <Spoke
              key={`spoke-${peer.nodeId}`}
              markerId={markerId}
              now={now}
              peer={peer}
              peerBox={layout.peers[index]}
              selected={selectedId === peer.nodeId}
              selfBox={layout.self}
              onSelect={(via) => onSelect(peer.nodeId, via, "spoke")}
            />
          ))}
          {layout.add && <AddSpoke peer={layout.add} self={layout.self} />}
          <SelfCard box={layout.self} counts={countLines} self={self} />
          {peers.map((peer, index) => (
            <PeerCard
              key={peer.nodeId}
              box={layout.peers[index]}
              label={labels.get(peer.nodeId) ?? peer.name}
              now={now}
              peer={peer}
              selected={selectedId === peer.nodeId}
              onSelect={(via) => onSelect(peer.nodeId, via, "card")}
            />
          ))}
          {layout.add && onAdd && <AddCard box={layout.add} onAdd={onAdd} />}
        </svg>
      )}
    </div>
  );
}

function SelfCard({
  box,
  self,
  counts,
}: {
  box: Box;
  self: SyncLinksDiagramProps["self"];
  /** One line per type of definition. */
  counts: string[];
}) {
  const { t } = useTranslation();
  const x = box.cx - box.w / 2;
  const y = box.cy - box.h / 2;
  const textX = x + 48;
  const room = box.w - 56;

  return (
    <g data-testid="data-sync-self">
      <rect
        className="fill-none stroke-primary/15"
        height={box.h + 12}
        rx={17}
        strokeWidth={6}
        width={box.w + 12}
        x={x - 6}
        y={y - 6}
      />
      <rect
        className="fill-primary-50 stroke-primary"
        height={box.h}
        rx={12}
        strokeWidth={2}
        width={box.w}
        x={x}
        y={y}
      />
      <g transform={`translate(${x + 26} ${box.cy})`}>
        <KindGlyph className="stroke-primary" kind={self.headless ? "server" : "desktop"} />
      </g>
      <text className="fill-foreground" fontSize={14} fontWeight={650} x={textX} y={y + 22}>
        {fitEnd(self.name, room, 14 * SEMIBOLD)}
      </text>
      <text
        className="fill-primary-600 dark:fill-primary-400"
        fontSize={11}
        fontWeight={600}
        x={textX}
        y={y + 38}
      >
        {fitEnd(
          self.sharingEnabled
            ? `${t("dataSync.thisDevice")} · ${t("dataSync.diagram.shares")}`
            : t("dataSync.thisDevice"),
          room,
          11,
        )}
      </text>
      {counts.slice(0, 2).map((line, index) => (
        <text
          key={line}
          className="fill-default-500"
          fontSize={11}
          x={textX}
          y={y + 56 + index * 15}
        >
          {fitEnd(line, room, 11)}
        </text>
      ))}
    </g>
  );
}

function Lane({
  from,
  to,
  status,
  attention,
  markerId,
  emphasised,
  direction,
}: {
  from: Point;
  to: Point;
  status: LaneStatus;
  attention: boolean;
  markerId: string;
  emphasised: boolean;
  direction: "receive" | "read";
}) {
  if (status === "none") return null;

  return (
    <line
      className={edgeStyles.sync.stroke}
      data-attention={attention || undefined}
      data-direction={direction}
      data-status={status}
      markerEnd={`url(#${markerId})`}
      strokeDasharray={status === "pending" ? PENDING_DASH : attention ? ATTENTION_DASH : undefined}
      strokeLinecap="round"
      strokeOpacity={status === "pending" ? 0.85 : 1}
      strokeWidth={edgeStyles.sync.width + (emphasised ? 1 : 0)}
      x1={from.x}
      x2={to.x}
      y1={from.y}
      y2={to.y}
    />
  );
}

function Spoke({
  peer,
  selfBox,
  peerBox,
  selected,
  markerId,
  now,
  onSelect,
}: {
  peer: SyncPeer;
  selfBox: Box;
  peerBox: Box;
  selected: boolean;
  markerId: string;
  now?: number;
  onSelect: (via: DiagramSelectVia) => void;
}) {
  const { t } = useTranslation();
  const geometry = spokeGeometry(selfBox, peerBox);
  const receive = receiveLane(peer);
  const read = readLane(peer);
  const issue = syncIssueOf(peer);
  const mode = lineMode(peer);
  const label = spokeLabel(t, peer, now);
  const faint = receive === "none" && read === "none";

  return (
    <g
      aria-label={label}
      aria-pressed={selected}
      className="group cursor-pointer outline-none"
      data-in={receive}
      data-mode={mode}
      data-out={read}
      data-sync-part="spoke"
      data-sync-peer={peer.nodeId}
      data-testid="data-sync-spoke"
      role="button"
      tabIndex={0}
      onClick={() => onSelect("pointer")}
      onKeyDown={onActivate(() => onSelect("keyboard"))}
    >
      <title>{label}</title>
      <line
        stroke="transparent"
        strokeLinecap="round"
        strokeWidth={18}
        x1={geometry.from.x}
        x2={geometry.to.x}
        y1={geometry.from.y}
        y2={geometry.to.y}
      />
      <line
        className="stroke-focus opacity-0 group-focus-visible:opacity-60"
        strokeLinecap="round"
        strokeWidth={12}
        x1={geometry.from.x}
        x2={geometry.to.x}
        y1={geometry.from.y}
        y2={geometry.to.y}
      />
      {faint && (
        // Nothing either way — an ended request, a link that is off: a faint dotted line.
        <line
          className="stroke-default-300"
          strokeDasharray="2 5"
          strokeLinecap="round"
          strokeWidth={1.5}
          x1={geometry.from.x}
          x2={geometry.to.x}
          y1={geometry.from.y}
          y2={geometry.to.y}
        />
      )}
      <Lane
        attention={!!issue}
        direction="receive"
        emphasised={selected}
        from={geometry.receive.from}
        markerId={markerId}
        status={receive}
        to={geometry.receive.to}
      />
      <Lane
        attention={false}
        direction="read"
        emphasised={selected}
        from={geometry.read.from}
        markerId={markerId}
        status={read}
        to={geometry.read.to}
      />
      <g transform={`translate(${geometry.badge.x} ${geometry.badge.y})`}>
        <circle
          className={`${selected ? edgeStyles.sync.fill : "fill-content1"} ${edgeStyles.sync.stroke}`}
          r={10}
          strokeWidth={selected ? 2 : 1.5}
        />
        <KindBadgeGlyph
          className={selected ? "stroke-background" : edgeStyles.sync.stroke}
          kind="sync"
        />
        {issue && <AttentionMark x={8} y={-8} />}
      </g>
      {mode && (
        <text
          className={edgeStyles.sync.fill}
          data-testid="data-sync-spoke-mode"
          dominantBaseline="central"
          fontSize={10}
          fontWeight={600}
          textAnchor="middle"
          x={geometry.label.x}
          y={geometry.label.y}
        >
          {t(`federation.map.sync.mode.${mode}`)}
        </text>
      )}
    </g>
  );
}

function PeerCard({
  peer,
  box,
  label,
  selected,
  now,
  onSelect,
}: {
  peer: SyncPeer;
  box: Box;
  label: string;
  selected: boolean;
  now?: number;
  onSelect: (via: DiagramSelectVia) => void;
}) {
  const { t } = useTranslation();
  const x = box.cx - box.w / 2;
  const y = box.cy - box.h / 2;
  const status = peerCardLine(t, peer, now);
  const offline = isOffline(peer) || peer.outcome === "rejected" || peer.outcome === "expired";
  const textX = x + 46;
  const waiting = peer.attention?.openDecisions ?? 0;

  return (
    <g
      aria-label={cardLabel(t, peer, now)}
      aria-pressed={selected}
      className="group cursor-pointer outline-none"
      data-sync-part="card"
      data-sync-peer={peer.nodeId}
      data-testid="data-sync-peer"
      data-tone={status.tone}
      role="button"
      tabIndex={0}
      onClick={() => onSelect("pointer")}
      onKeyDown={onActivate(() => onSelect("keyboard"))}
    >
      <title>{peer.name}</title>
      <rect
        className="fill-none stroke-focus opacity-0 group-focus-visible:opacity-100"
        height={box.h + 10}
        rx={16}
        strokeWidth={2.5}
        width={box.w + 10}
        x={x - 5}
        y={y - 5}
      />
      {selected && (
        <rect
          className="fill-none stroke-primary/35"
          height={box.h + 12}
          rx={17}
          strokeWidth={6}
          width={box.w + 12}
          x={x - 6}
          y={y - 6}
        />
      )}
      <rect
        className={
          selected
            ? "fill-content1 stroke-primary"
            : "fill-content1 stroke-default-300 group-hover:stroke-default-500"
        }
        height={box.h}
        opacity={offline ? 0.85 : 1}
        rx={12}
        strokeWidth={selected ? 2 : 1.2}
        width={box.w}
        x={x}
        y={y}
      />
      <g transform={`translate(${x + 23} ${box.cy})`}>
        <KindGlyph className="stroke-default-600" kind={kindOf(peer)} />
      </g>
      <text className="fill-foreground" fontSize={13} fontWeight={600} x={textX} y={box.cy - 4}>
        {label}
      </text>
      <text className="fill-default-500" fontSize={11} x={textX} y={box.cy + 13}>
        {fitEnd(status.text, box.w - (textX - x) - 10, 11)}
      </text>
      <circle
        className={toneFill[status.tone]}
        cx={x + box.w - 13}
        cy={y + 13}
        data-status-dot={status.tone}
        r={4.5}
        strokeWidth={1.5}
      />
      {peer.openItems > 0 && (
        <g
          aria-hidden
          data-testid="data-sync-open-items"
          transform={`translate(${x + 4} ${y + 2})`}
        >
          <circle className="fill-warning" r={9} />
          <text
            className="fill-warning-foreground"
            dominantBaseline="central"
            fontSize={10}
            fontWeight={700}
            textAnchor="middle"
          >
            {peer.openItems > 99 ? "99+" : peer.openItems}
          </text>
        </g>
      )}
      {waiting > 0 && (
        <g
          aria-hidden
          data-testid="data-sync-waits-there"
          transform={`translate(${x + box.w - 30} ${y + 13})`}
        >
          <circle className="fill-content1 stroke-warning" r={8} strokeWidth={1.4} />
          <text
            className="fill-warning-700 dark:fill-warning"
            dominantBaseline="central"
            fontSize={9}
            fontWeight={700}
            textAnchor="middle"
          >
            {waiting > 99 ? "99+" : waiting}
          </text>
        </g>
      )}
    </g>
  );
}

function AddSpoke({ self, peer }: { self: Box; peer: Box }) {
  const geometry = spokeGeometry(self, peer);

  return (
    <line
      aria-hidden
      className="stroke-default-300"
      strokeDasharray="3 5"
      strokeLinecap="round"
      strokeWidth={1.5}
      x1={geometry.from.x}
      x2={geometry.to.x}
      y1={geometry.from.y}
      y2={geometry.to.y}
    />
  );
}

function AddCard({ box, onAdd }: { box: Box; onAdd: () => void }) {
  const { t } = useTranslation();
  const x = box.cx - box.w / 2;
  const y = box.cy - box.h / 2;
  const label = t("dataSync.wizard.open");

  return (
    <g
      aria-label={label}
      className="group cursor-pointer outline-none"
      data-testid="data-sync-add"
      role="button"
      tabIndex={0}
      onClick={onAdd}
      onKeyDown={onActivate(onAdd)}
    >
      <title>{label}</title>
      <rect
        className="fill-none stroke-focus opacity-0 group-focus-visible:opacity-100"
        height={box.h + 10}
        rx={16}
        strokeWidth={2.5}
        width={box.w + 10}
        x={x - 5}
        y={y - 5}
      />
      <rect
        className="fill-content1 stroke-default-400 group-hover:stroke-primary"
        height={box.h}
        rx={12}
        strokeDasharray="5 4"
        strokeWidth={1.2}
        width={box.w}
        x={x}
        y={y}
      />
      <g transform={`translate(${x + 24} ${box.cy})`}>
        <circle
          className="fill-primary/10 stroke-primary group-hover:fill-primary/25"
          r={10}
          strokeWidth={1.4}
        />
        <path
          className="stroke-primary"
          d="M-4.5 0 H4.5 M0 -4.5 V4.5"
          strokeLinecap="round"
          strokeWidth={1.8}
        />
      </g>
      <text className="fill-default-600" fontSize={12} fontWeight={600} x={x + 44} y={box.cy + 4}>
        {fitEnd(label, box.w - 52, 12 * SEMIBOLD)}
      </text>
    </g>
  );
}

/** A direction as the list draws it: a short arrow, in the diagram's styles. */
function MiniArrow({
  status,
  towards,
  attention = false,
}: {
  status: LaneStatus;
  towards: "self" | "peer";
  /** Set up but not working: dotted, as the drawing draws it. */
  attention?: boolean;
}) {
  const none = status === "none";

  return (
    <svg aria-hidden className="h-2.5 w-10 shrink-0" viewBox="0 0 40 10">
      <line
        className={none ? "stroke-default-300" : edgeStyles.sync.stroke}
        data-attention={attention || undefined}
        strokeDasharray={
          status === "pending" ? "4 3" : none ? "1.5 3" : attention ? ATTENTION_DASH : undefined
        }
        strokeLinecap={attention ? "round" : undefined}
        strokeWidth={2}
        x1={towards === "self" ? 38 : 2}
        x2={towards === "self" ? 8 : 32}
        y1={5}
        y2={5}
      />
      {!none && (
        <path
          className={edgeStyles.sync.fill}
          d={towards === "self" ? "M9 1 L1 5 L9 9 z" : "M31 1 L39 5 L31 9 z"}
        />
      )}
    </svg>
  );
}

/** Every device in a row of its own, each with its two directions: the diagram, narrow. */
function DiagramList({
  peers,
  self,
  selfLine,
  selectedId,
  now,
  onSelect,
  onAdd,
}: {
  peers: SyncPeer[];
  self: SyncLinksDiagramProps["self"];
  selfLine: string;
  selectedId?: string;
  now?: number;
  onSelect: SyncLinksDiagramProps["onSelect"];
  onAdd?: () => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="space-y-2" data-testid="data-sync-diagram-list">
      <div className="rounded-xl border border-primary bg-primary-50 p-3 text-sm">
        <p className="font-semibold">{self.name}</p>
        <p className="text-xs text-primary">
          {t("dataSync.thisDevice")}
          {self.sharingEnabled ? ` · ${t("dataSync.diagram.shares")}` : ""}
        </p>
        {selfLine && <p className="text-xs text-default-500">{selfLine}</p>}
      </div>
      <ul className="space-y-2">
        {peers.map((peer) => {
          const status = linkStatus(t, peer, now);
          const receive = receiveLane(peer);
          const read = readLane(peer);
          const mode = lineMode(peer);

          return (
            <li key={peer.nodeId}>
              <button
                aria-label={spokeLabel(t, peer, now)}
                aria-pressed={selectedId === peer.nodeId}
                className={`flex w-full items-center gap-3 rounded-xl border p-3 text-left text-sm transition hover:bg-default-100 ${
                  selectedId === peer.nodeId ? "border-primary" : "border-default-200"
                }`}
                data-sync-part="row"
                data-sync-peer={peer.nodeId}
                data-testid="data-sync-peer-row"
                type="button"
                onClick={(event) =>
                  onSelect(peer.nodeId, event.detail === 0 ? "keyboard" : "pointer", "card")
                }
              >
                <span className="flex shrink-0 flex-col gap-1" data-in={receive} data-out={read}>
                  <MiniArrow attention={!!syncIssueOf(peer)} status={receive} towards="self" />
                  <MiniArrow status={read} towards="peer" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{peer.name}</span>
                  <span className={`flex items-start gap-1.5 text-xs ${toneText[status.tone]}`}>
                    <StatusDot tone={status.tone} />
                    <span className="min-w-0">
                      {status.text}
                      {mode ? ` · ${t(`federation.map.sync.mode.${mode}`)}` : ""}
                    </span>
                  </span>
                </span>
                <span className="flex shrink-0 items-center gap-1">
                  <CountBubble
                    count={peer.openItems}
                    label={t("dataSync.diagram.openItems", { count: peer.openItems })}
                  />
                  <CountBubble
                    hollow
                    count={peer.attention?.openDecisions ?? 0}
                    label={t("dataSync.status.NeedsYouThere", {
                      name: peer.name,
                      count: peer.attention?.openDecisions ?? 0,
                    })}
                  />
                </span>
              </button>
            </li>
          );
        })}
      </ul>
      {onAdd && (
        <button
          className="flex w-full items-center gap-2 rounded-xl border border-dashed border-default-400 p-3 text-sm text-default-600 hover:border-primary hover:text-primary"
          data-testid="data-sync-add"
          type="button"
          onClick={onAdd}
        >
          <span aria-hidden>＋</span>
          {t("dataSync.wizard.open")}
        </button>
      )}
    </div>
  );
}
